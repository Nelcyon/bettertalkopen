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
