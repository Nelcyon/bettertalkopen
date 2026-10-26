using Aac.Core.Abstractions;

namespace Aac.Core.Input;

/// <summary>Logical actions. Physical keys (whatever the QuadStick sends) map onto these through <see cref="KeyMap"/>.</summary>
public enum InputAction
{
    MoveUp,
    MoveDown,
    MoveLeft,
    MoveRight,
    Select,
    Approve,
    Speak,
    StopVoice,
    MakeSentences,
    DeleteLetter,
    Undo,
    Home,
    ToggleWindow,
    /// <summary>Turn the Itch doll (only while Itch is open).</summary>
    DollTurnLeft,
    DollTurnRight,
    /// <summary>Zoom the Itch doll in or out (only while Itch is open).</summary>
    DollCloser,
    DollFarther,
    /// <summary>Move the view across the Itch doll (only while Itch is open).</summary>
    DollMoveUp,
    DollMoveDown,
    DollMoveLeft,
    DollMoveRight,
    /// <summary>Look at the doll from above or from below (only while Itch is open).</summary>
    DollTiltAbove,
    DollTiltBelow,
    /// <summary>Show the doll from the front or the back, or the whole body again (only while Itch is open).</summary>
    DollFront,
    DollBack,
    DollWholeBody,
    DeleteWord,
    /// <summary>Type a space: lets a sip, a chin switch or any other key stand in for the on-screen Space key.</summary>
    Space,
    Clear,
    /// <summary>Open or close the Quick, Phrases or Itch panel.</summary>
    Quick,
    Phrases,
    Itch,
    /// <summary>Say the last message again (the Last said key).</summary>
    SayLastAgain,
}

/// <summary>What a system-wide hotkey (one that works while another program has focus) can do.</summary>
public enum HotkeyAction
{
    /// <summary>Show the window if it is hidden or behind another program, otherwise hide it.</summary>
    ShowOrHide,
    Show,
    Hide,
    /// <summary>Show the window with the Itch figure open.</summary>
    ShowWithItch,
}

/// <summary>One action with the words the Settings window shows for it.</summary>
public sealed record ActionInfo(string Name, string Label, string Group);

/// <summary>The actions a person can give keys to, in the order and groups the Settings window lists them.</summary>
public static class ActionCatalog
{
    public const string AnywhereGroup = "Anywhere in Windows";
    public const string OpenGroup = "While BetterTalk is open";
    public const string FigureGroup = "The Itch figure";

    /// <summary>System-wide hotkeys (see <see cref="HotkeySpec"/> for what keys are allowed).</summary>
    public static readonly IReadOnlyList<ActionInfo> Hotkeys = new[]
    {
        new ActionInfo(nameof(HotkeyAction.ShowOrHide), "Show or hide BetterTalk", AnywhereGroup),
        new ActionInfo(nameof(HotkeyAction.Show), "Show BetterTalk", AnywhereGroup),
        new ActionInfo(nameof(HotkeyAction.Hide), "Hide BetterTalk", AnywhereGroup),
        new ActionInfo(nameof(HotkeyAction.ShowWithItch), "Show BetterTalk with the Itch figure open", AnywhereGroup),
    };

    /// <summary>Keys that work while the window has focus (single keys, so a QuadStick gesture can send each).</summary>
    public static readonly IReadOnlyList<ActionInfo> Keys = new[]
    {
        new ActionInfo(nameof(InputAction.Speak), "Speak the message", OpenGroup),
        new ActionInfo(nameof(InputAction.StopVoice), "Stop speaking", OpenGroup),
        new ActionInfo(nameof(InputAction.SayLastAgain), "Say the last message again", OpenGroup),
        new ActionInfo(nameof(InputAction.DeleteLetter), "Delete a letter", OpenGroup),
        new ActionInfo(nameof(InputAction.DeleteWord), "Delete a word", OpenGroup),
        new ActionInfo(nameof(InputAction.Space), "Type a space", OpenGroup),
        new ActionInfo(nameof(InputAction.Undo), "Undo", OpenGroup),
        new ActionInfo(nameof(InputAction.Clear), "Clear the message", OpenGroup),
        new ActionInfo(nameof(InputAction.Quick), "Open or close Quick", OpenGroup),
        new ActionInfo(nameof(InputAction.Phrases), "Open or close Phrases", OpenGroup),
        new ActionInfo(nameof(InputAction.Itch), "Open or close Itch", OpenGroup),
        new ActionInfo(nameof(InputAction.Home), "Close the open panel", OpenGroup),
        new ActionInfo(nameof(InputAction.ToggleWindow), "Hide BetterTalk", OpenGroup),
        new ActionInfo(nameof(InputAction.Select), "Press the selected key (speaks when no key is selected)", OpenGroup),
        new ActionInfo(nameof(InputAction.MoveUp), "Move the selection up", OpenGroup),
        new ActionInfo(nameof(InputAction.MoveDown), "Move the selection down", OpenGroup),
        new ActionInfo(nameof(InputAction.MoveLeft), "Move the selection left", OpenGroup),
        new ActionInfo(nameof(InputAction.MoveRight), "Move the selection right", OpenGroup),
        new ActionInfo(nameof(InputAction.DollTurnLeft), "Turn the figure left", FigureGroup),
        new ActionInfo(nameof(InputAction.DollTurnRight), "Turn the figure right", FigureGroup),
        new ActionInfo(nameof(InputAction.DollFront), "Show the front", FigureGroup),
        new ActionInfo(nameof(InputAction.DollBack), "Show the back", FigureGroup),
        new ActionInfo(nameof(InputAction.DollCloser), "Closer", FigureGroup),
        new ActionInfo(nameof(InputAction.DollFarther), "Farther", FigureGroup),
        new ActionInfo(nameof(InputAction.DollWholeBody), "Show the whole body", FigureGroup),
        new ActionInfo(nameof(InputAction.DollMoveUp), "Move the view up", FigureGroup),
        new ActionInfo(nameof(InputAction.DollMoveDown), "Move the view down", FigureGroup),
        new ActionInfo(nameof(InputAction.DollMoveLeft), "Move the view left", FigureGroup),
        new ActionInfo(nameof(InputAction.DollMoveRight), "Move the view right", FigureGroup),
        new ActionInfo(nameof(InputAction.DollTiltAbove), "Look at the figure from above", FigureGroup),
        new ActionInfo(nameof(InputAction.DollTiltBelow), "Look at the figure from below", FigureGroup),
    };
}

[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Win = 8,
}

/// <summary>
/// A system-wide hotkey written as "Ctrl+Shift+F12": modifiers and one key. It needs a modifier or an
/// F-key, so a plain letter or arrow (which a QuadStick sends for typing) is never taken from every program.
/// </summary>
public static class HotkeySpec
{
    public static bool TryParse(string? spec, out HotkeyModifiers modifiers, out string key, out string? problem)
    {
        modifiers = HotkeyModifiers.None;
        key = "";
        problem = null;
        if (string.IsNullOrWhiteSpace(spec)) { problem = "No key."; return false; }
        foreach (var part in spec.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "control": modifiers |= HotkeyModifiers.Control; break;
                case "shift": modifiers |= HotkeyModifiers.Shift; break;
                case "alt": modifiers |= HotkeyModifiers.Alt; break;
                case "win" or "windows": modifiers |= HotkeyModifiers.Win; break;
                default:
                    if (key.Length > 0) { problem = $"\"{spec}\" has more than one key."; return false; }
                    key = KeyMap.Normalize(part);
                    break;
            }
        }
        if (key.Length == 0) { problem = $"\"{spec}\" has no key, only Ctrl, Alt, Shift or Win."; return false; }
        if (modifiers == HotkeyModifiers.None && !IsFunctionKey(key))
        {
            problem = $"\"{spec}\" would take that key from every program. Add Ctrl, Alt, Shift or Win, or use an F-key.";
            return false;
        }
        return true;
    }

    /// <summary>F1 to F24.</summary>
    public static bool IsFunctionKey(string key) =>
        key.Length is 2 or 3 && (key[0] is 'F' or 'f') && int.TryParse(key.AsSpan(1), out var n) && n is >= 1 and <= 24;

    public static string Format(HotkeyModifiers modifiers, string key)
    {
        var parts = new List<string>(5);
        if (modifiers.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");
        parts.Add(key);
        return string.Join("+", parts);
    }
}

/// <summary>Maps key names (WPF <c>Key.ToString()</c> spelling, e.g. "Left", "F9", "Enter") to actions.</summary>
public sealed class KeyMap
{
    private readonly Dictionary<string, InputAction> _map = new(StringComparer.OrdinalIgnoreCase);

    public static KeyMap Default() => FromDictionary(DefaultBindings);

    /// <summary>
    /// Default bindings use single keys only (no chords), so a QuadStick mode can emit each with one gesture.
    /// These are placeholders until the user's actual QuadStick configuration is known.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> DefaultBindings = new Dictionary<string, string>
    {
        ["Up"] = nameof(InputAction.MoveUp),
        ["Down"] = nameof(InputAction.MoveDown),
        ["Left"] = nameof(InputAction.MoveLeft),
        ["Right"] = nameof(InputAction.MoveRight),
        ["Enter"] = nameof(InputAction.Select),
        ["F5"] = nameof(InputAction.MakeSentences),
        ["F8"] = nameof(InputAction.Approve),
        ["F9"] = nameof(InputAction.Speak),
        ["Escape"] = nameof(InputAction.StopVoice),
        ["Back"] = nameof(InputAction.DeleteLetter),
        ["F4"] = nameof(InputAction.Undo),
        ["Home"] = nameof(InputAction.Home),
        ["F6"] = nameof(InputAction.DollTurnLeft),
        ["F7"] = nameof(InputAction.DollTurnRight),
        ["PageUp"] = nameof(InputAction.DollCloser),
        ["Next"] = nameof(InputAction.DollFarther), // WPF calls Page Down "Next"
        ["NumPad8"] = nameof(InputAction.DollMoveUp),
        ["NumPad2"] = nameof(InputAction.DollMoveDown),
        ["NumPad4"] = nameof(InputAction.DollMoveLeft),
        ["NumPad6"] = nameof(InputAction.DollMoveRight),
        ["Delete"] = nameof(InputAction.DeleteWord),
    };

    public static KeyMap FromDictionary(IEnumerable<KeyValuePair<string, string>> bindings)
    {
        var map = new KeyMap();
        foreach (var (key, action) in bindings)
        {
            if (!Enum.TryParse<InputAction>(action, ignoreCase: true, out var a))
                throw new ArgumentException($"Unknown action '{action}' bound to key '{key}'.");
            map._map[Normalize(key)] = a;
        }
        return map;
    }

    public InputAction? Resolve(string key) => _map.TryGetValue(Normalize(key), out var a) ? a : null;

    public IReadOnlyDictionary<string, InputAction> Bindings => _map;

    /// <summary>
    /// The mouse buttons an in-app key can be (a QuadStick or switch box can send them instead of keys). The left
    /// button never: it presses BetterTalk's own buttons.
    /// </summary>
    /// <summary>WPF reports Enter as "Return" and Backspace as "Back"; accept the common spellings.</summary>
    public static string Normalize(string key) => key.Trim() switch
    {
        var k when k.Equals("Return", StringComparison.OrdinalIgnoreCase) => "Enter",
        var k when k.Equals("Backspace", StringComparison.OrdinalIgnoreCase) => "Back",
        var k when k.Equals("Esc", StringComparison.OrdinalIgnoreCase) => "Escape",
        var k => k,
    };
}

public sealed class InputGateOptions
{
    /// <summary>Minimum time between two firings of the same action (contact bounce, double sip).</summary>
    public int DebounceMs { get; set; } = 300;

    /// <summary>Debounce for directional moves, which are deliberately repeated more often.</summary>
    public int MoveDebounceMs { get; set; } = 120;

    /// <summary>Debounce for Delete letter (Backspace), which is tapped quickly in a row.</summary>
    public int DeleteDebounceMs { get; set; } = 40;

    /// <summary>Holding Backspace repeats it after this long...</summary>
    public int DeleteRepeatDelayMs { get; set; } = 400;

    /// <summary>...and then every this many milliseconds. No other action ever repeats while held.</summary>
    public int DeleteRepeatIntervalMs { get; set; } = 60;

    /// <summary>Key presses that begin within this window after the window gains focus are ignored.</summary>
    public int FocusGuardMs { get; set; } = 400;

    /// <summary>
    /// Actions that only fire on release after being held at least this long (rejects brief bumps).
    /// Empty by default: hold requirements must be tuned with the user, not assumed.
    /// </summary>
    public Dictionary<InputAction, int> HoldToActivateMs { get; set; } = new();
}

public readonly record struct GateResult(bool Handled, InputAction? Action)
{
    public static readonly GateResult PassThrough = new(false, null);
    public static readonly GateResult Swallowed = new(true, null);
}

/// <summary>
/// Turns raw key events into deliberate, edge-triggered actions.
/// - OS auto-repeat from a held key never produces a second action, except Delete letter (Backspace),
///   which repeat after a short hold like a normal keyboard's backspace.
/// - A key must be released before it can act again.
/// - A key-up with no matching key-down (key held while the window was switched) does nothing.
/// - Presses right after focus changes are ignored, so switching from a game can't trigger Speak.
/// - Optional hold-to-activate per action.
/// </summary>
public sealed class InputGate
{
    private readonly KeyMap _map;
    private readonly InputGateOptions _options;
    private readonly IClock _clock;
    private readonly Dictionary<string, DateTimeOffset> _down = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _ignoredUntilRelease = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<InputAction, DateTimeOffset> _lastFired = new();
    private DateTimeOffset _focusGainedAt = DateTimeOffset.MinValue;
    private bool _hasFocus = true;

    public InputGate(KeyMap map, InputGateOptions options, IClock clock)
    {
        _map = map;
        _options = options;
        _clock = clock;
    }

    public GateResult KeyDown(string key, bool isRepeat)
    {
        key = KeyMap.Normalize(key);
        var action = _map.Resolve(key);
        if (action is null) return GateResult.PassThrough;
        if (!_hasFocus) return GateResult.Swallowed;
        if (isRepeat || _down.ContainsKey(key))
            return IsDelete(action.Value) && _down.TryGetValue(key, out var heldSince) && !_ignoredUntilRelease.Contains(key)
                ? RepeatWhileHeld(action.Value, heldSince)
                : GateResult.Swallowed;

        var now = _clock.Now;
        _down[key] = now;
        if ((now - _focusGainedAt).TotalMilliseconds < _options.FocusGuardMs)
        {
            _ignoredUntilRelease.Add(key);
            return GateResult.Swallowed;
        }
        if (HoldFor(action.Value) > 0) return GateResult.Swallowed;
        return Fire(action.Value);
    }

    public GateResult KeyUp(string key)
    {
        key = KeyMap.Normalize(key);
        var action = _map.Resolve(key);
        if (action is null) return GateResult.PassThrough;
        if (!_down.Remove(key, out var downAt)) return GateResult.Swallowed;
        if (_ignoredUntilRelease.Remove(key)) return GateResult.Swallowed;

        var hold = HoldFor(action.Value);
        if (hold > 0 && (_clock.Now - downAt).TotalMilliseconds >= hold) return Fire(action.Value);
        return GateResult.Swallowed;
    }

    /// <summary>A click or touch on an on-screen control. Debounced like a key press.</summary>
    public InputAction? Pointer(InputAction action) => _hasFocus ? Fire(action).Action : null;

    public void FocusGained()
    {
        _hasFocus = true;
        _focusGainedAt = _clock.Now;
        _down.Clear();
        _ignoredUntilRelease.Clear();
    }

    public void FocusLost()
    {
        _hasFocus = false;
        _down.Clear();
        _ignoredUntilRelease.Clear();
    }

    public static bool IsDelete(InputAction a) => a is InputAction.DeleteLetter;

    private GateResult RepeatWhileHeld(InputAction action, DateTimeOffset heldSince)
    {
        var now = _clock.Now;
        if ((now - heldSince).TotalMilliseconds < _options.DeleteRepeatDelayMs) return GateResult.Swallowed;
        if (_lastFired.TryGetValue(action, out var last) && (now - last).TotalMilliseconds < _options.DeleteRepeatIntervalMs)
            return GateResult.Swallowed;
        return Fire(action);
    }

    private int HoldFor(InputAction a) => _options.HoldToActivateMs.TryGetValue(a, out var ms) ? ms : 0;

    private GateResult Fire(InputAction action)
    {
        var now = _clock.Now;
        var debounce = action is InputAction.MoveUp or InputAction.MoveDown or InputAction.MoveLeft or InputAction.MoveRight
            ? _options.MoveDebounceMs
            : IsDelete(action) ? _options.DeleteDebounceMs : _options.DebounceMs;
        if (_lastFired.TryGetValue(action, out var last) && (now - last).TotalMilliseconds < debounce)
            return GateResult.Swallowed;
        _lastFired[action] = now;
        return new GateResult(true, action);
    }
}
