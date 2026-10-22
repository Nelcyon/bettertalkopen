using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Speech.Synthesis;
using System.Text;
using System.Windows.Input;
using System.Windows.Interop;
using Aac.Core.Abstractions;
using Aac.Core.Input;
using Aac.Core.Speech;
using Aac.Core.Storage;

namespace Aac.App;

public static class AppPaths
{
    /// <summary>%APPDATA%\BetterTalk, or the folder in BETTERTALK_DATA_DIR (for testing without touching the real settings).</summary>
    public static readonly string Root = Environment.GetEnvironmentVariable("BETTERTALK_DATA_DIR") is { Length: > 0 } dir
        ? dir
        : MigratedRoot(Environment.SpecialFolder.ApplicationData);

    /// <summary>The folder this app was called before it was renamed; its contents move to the new name once.</summary>
    internal const string OldFolderName = "QuadStickAac";

    /// <summary>Returns the BetterTalk folder under <paramref name="parent"/>, moving an older QuadStickAac folder there first (settings and buttons only; downloaded voices are never moved).</summary>
    internal static string MigratedRoot(Environment.SpecialFolder parent)
    {
        var folder = Environment.GetFolderPath(parent);
        var current = Path.Combine(folder, "BetterTalk");
        var old = Path.Combine(folder, OldFolderName);
        try
        {
            if (!Directory.Exists(current) && Directory.Exists(old)) Directory.Move(old, current);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return current;
    }
    public static string Settings => Path.Combine(Root, "settings.json");
    public static string Draft => Path.Combine(Root, "draft.bin");
    public static string Learned => Path.Combine(Root, "learned.bin");
    /// <summary>The button words (urgent row, quick phrases, categories), saved the moment they change.</summary>
    public static string Buttons => Path.Combine(Root, "buttons.json");
    public static string ErrorLog => Path.Combine(Root, "errors.log");
    /// <summary>One line per start and exit (with the reason), so a vanished window can be explained.</summary>
    public static string AppLog => Path.Combine(Root, "app.log");
    public static string WindowSize => Path.Combine(Root, "window.json");
    /// <summary>How the Itch panels are arranged (moved in move mode), saved the moment it changes.</summary>
    public static string Layout => Path.Combine(Root, "layout.json");
    /// <summary>Other programs turned down while BetterTalk speaks; there only until they are back up (so a crash cannot leave them quiet).</summary>
    public static string DuckedSounds => Path.Combine(Root, "ducked-sounds.json");
}

/// <summary>Windows DPAPI, current-user scope: files are unreadable by other Windows accounts or on another PC.</summary>
public sealed class DpapiProtector : IDataProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("QuadStickAac.v1") /* unchanged on purpose: it keeps files saved before the rename readable */;
    public byte[] Protect(byte[] plain) => ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
    public byte[] Unprotect(byte[] cipher) => ProtectedData.Unprotect(cipher, Entropy, DataProtectionScope.CurrentUser);
}

/// <summary>Offline speech through SAPI 5 (System.Speech). Uses the voices installed in Windows.</summary>
public sealed class SapiSpeechEngine : ISpeechEngine, ISpeechProgress, IDisposable
{
    private readonly SpeechSynthesizer _synth = new();
    /// <summary>Each queued prompt: whose utterance it is, where its text starts in it, and whether it is the last part.</summary>
    private readonly Dictionary<Prompt, (long Id, int Offset, bool Last)> _ids = new();
    /// <summary>For a prompt with pauses: where each part's words sit in the prompt's markup, and in the text.</summary>
    private readonly Dictionary<Prompt, List<(int MarkupStart, int TextStart)>> _markup = new();
    private readonly HashSet<long> _ended = new();
    private readonly object _lock = new();

    public SapiSpeechEngine(SpeechSettings settings)
    {
        _synth.SetOutputToDefaultAudioDevice();
        _synth.Rate = Math.Clamp(settings.Rate, -10, 10);
        _synth.Volume = Math.Clamp(settings.Volume, 0, 100);
        if (!string.IsNullOrWhiteSpace(settings.VoiceName))
        {
            try { _synth.SelectVoice(settings.VoiceName); }
            catch (ArgumentException) { VoiceProblem = $"Voice \"{settings.VoiceName}\" was not found; using the default voice."; }
        }
        _synth.SpeakCompleted += OnCompleted;
        // Windows reports each word as it starts, so the message box can mark it.
        _synth.SpeakProgress += (_, e) =>
        {
            (long Id, int Offset, bool Last) part;
            var position = e.CharacterPosition;
            lock (_lock)
            {
                if (!_ids.TryGetValue(e.Prompt, out part)) return;
                // A prompt with pauses reports places in its markup: turn them back into places in the text.
                if (_markup.TryGetValue(e.Prompt, out var map) && map.FindLast(m => m.MarkupStart <= position) is var (from, to) && map.Count > 0)
                    position = to + Math.Max(0, position - from);
            }
            Progress?.Invoke(this, new SpeechProgressEventArgs(part.Id, part.Offset + position));
        };
    }

    public event EventHandler<SpeechEndedEventArgs>? Ended;
    public event EventHandler<SpeechProgressEventArgs>? Progress;
    public string? VoiceProblem { get; }
    public string CurrentVoice => _synth.Voice.Name;

    /// <summary>Says the utterance; an ellipsis in it becomes a pause (a break inside the one prompt).</summary>
    public void Speak(Utterance utterance)
    {
        var parts = SpeechPauses.Split(utterance.SpokenText);
        Prompt prompt;
        if (parts.Count <= 1) prompt = new Prompt(utterance.SpokenText);
        else
        {
            var builder = new PromptBuilder();
            for (var i = 0; i < parts.Count; i++)
            {
                if (i > 0) builder.AppendBreak(TimeSpan.FromMilliseconds(SpeechPauses.PauseMs));
                builder.AppendText(parts[i].Text);
            }
            prompt = new Prompt(builder);
            var xml = builder.ToXml();
            var map = new List<(int, int)>();
            var at = 0;
            foreach (var (text, start) in parts)
            {
                var found = xml.IndexOf(System.Security.SecurityElement.Escape(text), at, StringComparison.Ordinal);
                if (found < 0) continue;
                map.Add((found, start));
                at = found + 1;
            }
            lock (_lock) _markup[prompt] = map;
        }
        lock (_lock) _ids[prompt] = (utterance.Id, 0, true);
        _synth.SpeakAsync(prompt);
    }

    public void CancelAll() => _synth.SpeakAsyncCancelAll();

    /// <summary>The utterance ends with its last part, or with the first part cut short by Stop (once either way).</summary>
    private void OnCompleted(object? sender, SpeakCompletedEventArgs e)
    {
        (long Id, int Offset, bool Last) part;
        lock (_lock)
        {
            if (!_ids.Remove(e.Prompt, out part)) return;
            _markup.Remove(e.Prompt);
            if (!part.Last && !e.Cancelled) return;
            if (!_ended.Add(part.Id)) return;
            if (_ended.Count > 64) _ended.Clear();
        }
        Ended?.Invoke(this, new SpeechEndedEventArgs(part.Id, e.Cancelled));
    }

    public void Dispose() => _synth.Dispose();
}

/// <summary>
/// The system-wide hotkeys (RegisterHotKey): they work while another program has focus. Each is a
/// <see cref="HotkeySpec"/> such as "Ctrl+Shift+F12" with the action it runs. None are set unless the
/// person assigns them in Settings → Hotkeys.
/// </summary>
public sealed class GlobalHotkeys : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const uint ModNoRepeat = 0x4000;
    private const int FirstId = 0xA11;
    private readonly HwndSource _source;
    private readonly Dictionary<int, Action> _actions = new();

    private GlobalHotkeys(HwndSource source)
    {
        _source = source;
        _source.AddHook(Hook);
    }

    /// <summary>
    /// Registers every hotkey it can and reports, in plain words, each one it could not (a key another
    /// program already owns, or a spec that cannot be read).
    /// </summary>
    public static GlobalHotkeys Register(IntPtr hwnd, IEnumerable<(string Spec, Action Run)> hotkeys, List<string> problems)
    {
        var hotkeysOwner = new GlobalHotkeys(HwndSource.FromHwnd(hwnd));
        var id = FirstId;
        foreach (var (spec, run) in hotkeys)
        {
            if (!HotkeySpec.TryParse(spec, out var mods, out var keyName, out var problem))
            {
                problems.Add($"Hotkey {spec}: {problem}");
                continue;
            }
            if (!Enum.TryParse<Key>(keyName, true, out var key) || KeyInterop.VirtualKeyFromKey(key) == 0)
            {
                problems.Add($"Hotkey {spec}: \"{keyName}\" is not a key name Windows knows.");
                continue;
            }
            if (!RegisterHotKey(hwnd, id, (uint)mods | ModNoRepeat, (uint)KeyInterop.VirtualKeyFromKey(key)))
            {
                problems.Add($"Hotkey {spec} is already used by another program, so it does nothing here.");
                continue;
            }
            hotkeysOwner._actions[id++] = run;
        }
        return hotkeysOwner;
    }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && _actions.TryGetValue(wParam.ToInt32(), out var run))
        {
            run();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        foreach (var id in _actions.Keys) UnregisterHotKey(_source.Handle, id);
        _actions.Clear();
        _source.RemoveHook(Hook);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
