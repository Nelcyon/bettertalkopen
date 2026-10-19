using Aac.Core.Input;

namespace Aac.Core.Tests;

public class AutoHideTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
    private static bool Hide(double awayForMs, double lastInputMsAgo, bool busy = false, bool keepOnTop = false, bool over = false) =>
        AutoHidePolicy.ShouldHide(T0, over ? null : T0.AddMilliseconds(-awayForMs), T0.AddMilliseconds(-lastInputMsAgo), busy, keepOnTop, 2000);

    [Fact]
    public void Hides_once_the_pointer_has_been_away_and_nothing_was_pressed_for_the_delay() => Assert.True(Hide(2100, 5000));

    [Fact]
    public void Does_not_hide_before_the_delay() => Assert.False(Hide(1500, 5000));

    [Fact]
    public void Does_not_hide_while_the_pointer_is_over_the_app() => Assert.False(Hide(0, 5000, over: true));

    [Fact]
    public void Key_presses_keep_it_open_even_with_the_mouse_parked_elsewhere() => Assert.False(Hide(60_000, 800));

    [Fact]
    public void Never_hides_while_busy_or_kept_on_top()
    {
        Assert.False(Hide(60_000, 60_000, busy: true));
        Assert.False(Hide(60_000, 60_000, keepOnTop: true));
    }
}

public class FocusHighlightTests
{
    [Fact]
    public void By_default_the_frame_shows_only_after_arrow_keys_and_a_click_hides_it()
    {
        Assert.True(FocusHighlightPolicy.ShowFrame(FocusHighlight.WhileUsingKeys, lastInputWasNavigationKey: true));
        Assert.False(FocusHighlightPolicy.ShowFrame(FocusHighlight.WhileUsingKeys, lastInputWasNavigationKey: false));
        Assert.True(FocusHighlightPolicy.ShowFrame(FocusHighlight.Always, false));
        Assert.False(FocusHighlightPolicy.ShowFrame(FocusHighlight.Never, true));
    }
}
