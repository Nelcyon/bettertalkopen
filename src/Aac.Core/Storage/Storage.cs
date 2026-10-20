using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aac.Core.Abstractions;
using Aac.Core.Input;
using Aac.Core.Layout;
using Aac.Core.Prediction;
using Aac.Core.Speech;

namespace Aac.Core.Storage;

/// <summary>All user-adjustable behaviour. Stored as readable JSON so a caregiver or SLP can review it.</summary>
public sealed class AppSettings
{
    public double UiScale { get; set; } = 1.0;
    public double FontSize { get; set; } = 30;
    public bool StartFullscreen { get; set; }
    public bool AlwaysOnTop { get; set; }
    public bool WrapNavigation { get; set; }

    /// <summary>Key name → action name. See <see cref="KeyMap.DefaultBindings"/>.</summary>
    public Dictionary<string, string> KeyBindings { get; set; } = new(KeyMap.DefaultBindings);

    /// <summary>
    /// System-wide hotkeys, "Ctrl+Shift+F12" → a <see cref="HotkeyAction"/> name; they work while any other
    /// program has focus. None by default: the person assigns them in Settings → Hotkeys. (Older versions had
    /// a fixed Ctrl+Shift+F12 in a GlobalHotkey setting, which is now ignored.)
    /// </summary>
    public Dictionary<string, string> Hotkeys { get; set; } = new();

    /// <summary>The QuadStick card in Settings: card square ("Sip|Right") → what that gesture sends. See <see cref="Aac.Core.Input.QuadStickCard"/>.</summary>
    public Dictionary<string, CardCell> QuadStickCard { get; set; } = Aac.Core.Input.QuadStickCard.Default();

    /// <summary>Which corner of the screen the window sits in. Panels and drawers always open away from it.</summary>
    public WindowCorner Anchor { get; set; } = WindowCorner.TopLeft;
    /// <summary>The screen the window sits on, by its Windows device name (e.g. \\.\DISPLAY2); null or unknown means the main screen.</summary>
    public string? Screen { get; set; }

    /// <summary>The corner the window sits in opens the app (see <see cref="CornerTrigger"/>).</summary>
    public bool OpenFromCorner { get; set; } = true;
    /// <summary>The older name of <see cref="OpenFromCorner"/>, still read from settings files written before the window could be anchored elsewhere.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public bool? OpenFromTopLeftCorner { get => null; set { if (value is { } on) OpenFromCorner = on; } }
    /// <summary>How close to the corner, in pixels, the pointer has to be: 3 means a 4-pixel square.</summary>
    public int CornerPixels { get; set; } = 3;
    /// <summary>Click: a click with the pointer in the corner opens it. Rest: just resting the pointer there does.</summary>
    public CornerTrigger CornerTrigger { get; set; } = CornerTrigger.Click;
    /// <summary>For <see cref="CornerTrigger.Rest"/>: how long the pointer must stay, so a quick pass does not open it.</summary>
    public int CornerDwellMs { get; set; } = 300;
    /// <summary>Start hidden in the notification area (taskbar tray) instead of showing the window.</summary>
    public bool StartHidden { get; set; }
    /// <summary>Start BetterTalk when the person signs in to Windows (a Run entry for this Windows account).</summary>
    public bool StartWithWindows { get; set; }
    /// <summary>Hide to the tray once the mouse pointer has been off the window for <see cref="AutoHideAfterMs"/>.</summary>
    public bool AutoHideWhenMouseLeaves { get; set; } = false;
    /// <summary>The Itch controls panel is shown (the 🎮 icon over the figure folds it away); remembered.</summary>
    public bool ItchControlsOpen { get; set; } = true;
    public int AutoHideAfterMs { get; set; } = 2000;
    /// <summary>When the selection highlight shows; pressed keys always flash briefly.</summary>
    public FocusHighlight SelectionHighlight { get; set; } = FocusHighlight.WhileUsingKeys;
    /// <summary>How the words that replace the urgent strip while Itch is open are coloured.</summary>
    public SpokenLook SpokenButtonLook { get; set; } = SpokenLook.BlueRedOutline;
    /// <summary>Stop the window being resized or maximized (its saved size is kept); fullscreen (F11) is ignored too.</summary>
    public bool LockWindowSize { get; set; }

    public InputGateOptions Input { get; set; } = new();
    public SpeechSettings Speech { get; set; } = new();
    public GenerationSettings Generation { get; set; } = new();
    public PrivacySettings Privacy { get; set; } = new();
    public PhraseLibrary Phrases { get; set; } = PhraseLibrary.CreateDefault();
}

public enum CornerTrigger
{
    Click,
    Rest,
}

public sealed class SpeechSettings
{
    public string? VoiceName { get; set; }
    /// <summary>-10 (slow) to 10 (fast), SAPI scale.</summary>
    public int Rate { get; set; }
    /// <summary>0–100.</summary>
    public int Volume { get; set; } = 100;
    public SpeechOptions Safety { get; set; } = new();
    /// <summary>Quick phrases speak with one selection. If false, they load into the message to Speak.</summary>
    public bool QuickPhrasesSpeakImmediately { get; set; } = true;
    /// <summary>Also say each word as soon as it is finished (Space, punctuation or a word suggestion). Speak still reads the whole message.</summary>
    public bool SpeakEachWord { get; set; }
    /// <summary>After speaking, clear the message so the next one starts fresh (Undo brings it back).</summary>
    public bool ClearMessageAfterSpeaking { get; set; } = true;
    public Dictionary<string, string> Pronunciations { get; set; } = new();
}

public sealed class GenerationSettings
{
    public int MaxCandidates { get; set; } = 4;
    /// <summary>Off by default. Requires a model server on this computer (Ollama, llama.cpp, LM Studio).</summary>
    public bool LocalAiEnabled { get; set; }
    public string LocalAiEndpoint { get; set; } = "http://localhost:11434/v1/chat/completions";
    public string LocalAiModel { get; set; } = "";
    public int LocalAiTimeoutMs { get; set; } = 8000;
    /// <summary>Share the last few spoken messages with the local model as background. Off by default.</summary>
    public bool ShareRecentMessagesWithLocalAi { get; set; }
}

/// <summary>Everything stored is encrypted for this Windows user and never leaves the PC.</summary>
public sealed class PrivacySettings
{
    /// <summary>Keep the unfinished message (encrypted, this Windows user only) so a crash does not lose it.</summary>
    public bool RecoverUnfinishedMessage { get; set; } = true;
    /// <summary>Learn the user's words, word pairs and frequent messages from what the user speaks (encrypted, this PC only).</summary>
    public bool LearnFromSpokenMessages { get; set; } = true;
}

public static class SettingsStore
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Loads settings; on a missing or unreadable file returns defaults and reports why, never throws.</summary>
    public static AppSettings Load(string path, out string? problem)
    {
        problem = null;
        try
        {
            if (!File.Exists(path)) return new AppSettings();
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or NotSupportedException)
        {
            problem = $"Settings could not be read ({ex.Message}); using defaults.";
            return new AppSettings();
        }
    }

    public static void Save(string path, AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        AtomicFile.Write(path, Encoding.UTF8.GetBytes(JsonSerializer.Serialize(settings, JsonOptions)));
    }
}

/// <summary>Encrypted single-slot store for the unfinished message. Recovered text is never approved.</summary>
public sealed class DraftStore
{
    private readonly string _path;
    private readonly IDataProtector _protector;

    public DraftStore(string path, IDataProtector protector)
    {
        _path = path;
        _protector = protector;
    }

    public void Save(string text)
    {
        try
        {
            if (string.IsNullOrEmpty(text)) { Clear(); return; }
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
            AtomicFile.Write(_path, _protector.Protect(Encoding.UTF8.GetBytes(text)));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public string? Load()
    {
        try
        {
            if (!File.Exists(_path)) return null;
            return Encoding.UTF8.GetString(_protector.Unprotect(File.ReadAllBytes(_path)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
    }

    public void Clear()
    {
        try { if (File.Exists(_path)) File.Delete(_path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

/// <summary>
/// The button words (urgent row, quick phrases, phrase categories) in their own file, buttons.json, next to
/// the settings. Every change is written at once, so nothing the user sets up is ever only in memory. Older
/// installs kept them in settings.json; the first start moves them over.
/// </summary>
public static class ButtonStore
{
    /// <summary>The buttons from buttons.json, or (first start after the update) the ones settings.json had.</summary>
    public static PhraseLibrary Load(string path, PhraseLibrary fromSettings, out string? problem)
    {
        problem = null;
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<PhraseLibrary>(File.ReadAllText(path), SettingsStore.JsonOptions) ?? fromSettings;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or NotSupportedException)
        {
            // Keep the unreadable file for a person to look at, and carry on with the last good copy.
            problem = $"The buttons file could not be read ({ex.Message}); using the copy in settings. The file was kept as buttons.unreadable.json.";
            try { File.Copy(path, Path.ChangeExtension(path, ".unreadable.json"), overwrite: true); } catch (IOException) { }
            return fromSettings;
        }
        Save(path, fromSettings);
        return fromSettings;
    }

    public static void Save(string path, PhraseLibrary buttons)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        AtomicFile.Write(path, Encoding.UTF8.GetBytes(JsonSerializer.Serialize(buttons, SettingsStore.JsonOptions)));
    }
}

/// <summary>layout.json: how the Itch panels are arranged. A missing or unreadable file means the usual arrangement.</summary>
public static class LayoutStore
{
    public static PanelLayout Load(string path)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<PanelLayout>(File.ReadAllText(path), SettingsStore.JsonOptions) ?? new PanelLayout();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or NotSupportedException) { }
        return new PanelLayout();
    }

    public static void Save(string path, PanelLayout layout)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        AtomicFile.Write(path, Encoding.UTF8.GetBytes(JsonSerializer.Serialize(layout, SettingsStore.JsonOptions)));
    }
}

internal static class AtomicFile
{
    /// <summary>Write to a temp file then replace, so a crash mid-write never leaves a truncated file.</summary>
    public static void Write(string path, byte[] data)
    {
        var tmp = path + ".tmp";
        File.WriteAllBytes(tmp, data);
        File.Move(tmp, path, overwrite: true);
    }
}

/// <summary>Encrypted store for what the app has learned (words, word pairs, frequent messages).</summary>
public sealed class PersonalModelStore
{
    private readonly string _path;
    private readonly IDataProtector _protector;

    public PersonalModelStore(string path, IDataProtector protector)
    {
        _path = path;
        _protector = protector;
    }

    public void Save(PersonalData data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
        AtomicFile.Write(_path, _protector.Protect(JsonSerializer.SerializeToUtf8Bytes(data)));
    }

    public PersonalData Load()
    {
        try
        {
            if (!File.Exists(_path)) return new();
            return JsonSerializer.Deserialize<PersonalData>(_protector.Unprotect(File.ReadAllBytes(_path))) ?? new();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        {
            return new();
        }
    }

    public void Clear()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }
}

/// <summary>How the spoken words in the top strip (shown while Itch is open) are coloured, so they read as the Itch menu.</summary>
public enum SpokenLook
{
    /// <summary>Blue buttons with a red outline: the colour of the Itch screen, the outline of the urgent strip they replace.</summary>
    BlueRedOutline,
    /// <summary>Orange buttons (the colour of quick phrases) with a blue outline.</summary>
    OrangeBlueOutline,
    /// <summary>The same colours as the Itch button.</summary>
    LikeItchButton,
}
