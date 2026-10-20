namespace Aac.Core.Input;

/// <summary>
/// Decides when the window may hide itself after the mouse pointer leaves it. Hiding mid-message could
/// cost the user their voice, so anything that suggests the app is still in use keeps it open.
/// </summary>
public static class AutoHidePolicy
{
    /// <param name="pointerAwaySince">When the pointer last left the window; null while it is over the window (or any of the app's windows).</param>
    /// <param name="lastInputAt">The last key press, click or touch that reached the app.</param>
    /// <param name="busy">Speaking, the sentence panel, Settings or a menu is open, or the window is being moved or resized.</param>
    public static bool ShouldHide(DateTimeOffset now, DateTimeOffset? pointerAwaySince, DateTimeOffset lastInputAt, bool busy, bool keepOnTop, int delayMs)
    {
        if (keepOnTop || busy || pointerAwaySince is null) return false;
        var delay = TimeSpan.FromMilliseconds(Math.Max(250, delayMs));
        return now - pointerAwaySince.Value >= delay && now - lastInputAt >= delay;
    }
}

/// <summary>When the selection highlight (focus frame) is shown.</summary>
public enum FocusHighlight
{
    /// <summary>Only while moving with arrow keys or switches; a mouse click or touch hides it again.</summary>
    WhileUsingKeys,
    Always,
    /// <summary>Never: a pressed key only flashes.</summary>
    Never,
}

public static class FocusHighlightPolicy
{
    public static bool ShowFrame(FocusHighlight mode, bool lastInputWasNavigationKey) => mode switch
    {
        FocusHighlight.Always => true,
        FocusHighlight.Never => false,
        _ => lastInputWasNavigationKey,
    };

    /// <summary>
    /// Select (Enter, a QuadStick's usual sip) presses the framed key while stepping with the arrows; with no frame
    /// showing, nothing looks selected, so it speaks the message instead.
    /// </summary>
    public static InputAction Resolve(InputAction action, bool frameShown) =>
        action == InputAction.Select && !frameShown ? InputAction.Speak : action;
}
