using Aac.Core.Abstractions;
using Aac.Core.Input;

namespace Aac.Core.Tests;

public class InputGateTests
{
    private readonly ManualClock _clock = new();

    private InputGate Make(InputGateOptions? o = null)
    {
        var g = new InputGate(KeyMap.Default(), o ?? new InputGateOptions(), _clock);
        _clock.AdvanceMs(5000); // well past any start-up focus guard
        return g;
    }

    [Fact]
    public void Unmapped_keys_pass_through_for_typing()
    {
        var g = Make();
        Assert.Equal(GateResult.PassThrough, g.KeyDown("A", false));
    }

    [Fact]
    public void Mapped_key_fires_once_on_press()
    {
        var g = Make();
        Assert.Equal(InputAction.Speak, g.KeyDown("F9", false).Action);
        Assert.Null(g.KeyUp("F9").Action);
    }

    [Fact]
    public void Backspace_held_repeats_after_a_short_delay_like_a_normal_keyboard()
    {
        var g = Make();
        Assert.Equal(InputAction.DeleteLetter, g.KeyDown("Back", false).Action);
        var fired = 0;
        for (var t = 0; t < 1000; t += 30) // Windows auto-repeat every 30 ms for a second
        {
            _clock.AdvanceMs(30);
            if (g.KeyDown("Back", isRepeat: true).Action == InputAction.DeleteLetter) fired++;
        }
        // Nothing for the first 400 ms, then one every 60 ms: about (1000 - 400) / 60.
        Assert.InRange(fired, 8, 11);
        g.KeyUp("Back");
        _clock.AdvanceMs(30);
        Assert.Null(g.KeyDown("Back", isRepeat: true).Action); // a repeat with no key down is ignored
    }

    [Fact]
    public void Fast_backspace_taps_all_count()
    {
        var g = Make();
        var fired = 0;
        for (var i = 0; i < 10; i++)
        {
            if (g.KeyDown("Back", false).Action == InputAction.DeleteLetter) fired++;
            _clock.AdvanceMs(30);
            g.KeyUp("Back");
            _clock.AdvanceMs(70);
        }
        Assert.Equal(10, fired);
    }

    [Fact]
    public void Speak_and_select_never_repeat_while_held_and_keep_their_debounce()
    {
        var g = Make();
        Assert.Equal(InputAction.Speak, g.KeyDown("F9", false).Action);
        Assert.Equal(InputAction.Select, g.KeyDown("Enter", false).Action);
        for (var t = 0; t < 1500; t += 30)
        {
            _clock.AdvanceMs(30);
            Assert.Null(g.KeyDown("F9", isRepeat: true).Action);
            Assert.Null(g.KeyDown("Enter", isRepeat: true).Action);
        }
        g.KeyUp("F9");
        Assert.Equal(InputAction.Speak, g.KeyDown("F9", false).Action);
        g.KeyUp("F9");
        _clock.AdvanceMs(100);
        Assert.Null(g.KeyDown("F9", false).Action); // a bounce 100 ms later is still ignored
    }

    [Fact]
    public void Held_key_auto_repeat_never_fires_again()
    {
        var g = Make();
        Assert.Equal(InputAction.Speak, g.KeyDown("F9", false).Action);
        for (var i = 0; i < 20; i++)
        {
            _clock.AdvanceMs(50);
            var r = g.KeyDown("F9", isRepeat: true);
            Assert.True(r.Handled);
            Assert.Null(r.Action);
        }
    }

    [Fact]
    public void Repeat_without_os_flag_is_still_ignored_until_release()
    {
        var g = Make();
        g.KeyDown("F9", false);
        _clock.AdvanceMs(1000);
        Assert.Null(g.KeyDown("F9", false).Action);
        g.KeyUp("F9");
        Assert.Equal(InputAction.Speak, g.KeyDown("F9", false).Action);
    }

    [Fact]
    public void Bounce_within_debounce_is_ignored()
    {
        var g = Make(new InputGateOptions { DebounceMs = 300 });
        Assert.Equal(InputAction.Select, g.KeyDown("Enter", false).Action);
        g.KeyUp("Enter");
        _clock.AdvanceMs(80);
        Assert.Null(g.KeyDown("Enter", false).Action);
        g.KeyUp("Enter");
        _clock.AdvanceMs(400);
        Assert.Equal(InputAction.Select, g.KeyDown("Enter", false).Action);
    }

    [Fact]
    public void Moves_use_their_shorter_debounce()
    {
        var g = Make(new InputGateOptions { DebounceMs = 300, MoveDebounceMs = 100 });
        g.KeyDown("Right", false); g.KeyUp("Right");
        _clock.AdvanceMs(150);
        Assert.Equal(InputAction.MoveRight, g.KeyDown("Right", false).Action);
    }

    [Fact]
    public void Key_held_while_switching_into_the_app_does_nothing()
    {
        var g = Make();
        g.FocusLost();
        // user is in a game holding the Speak key, then switches to the AAC window
        g.FocusGained();
        _clock.AdvanceMs(600);
        Assert.Null(g.KeyDown("F9", isRepeat: true).Action);
        Assert.Null(g.KeyUp("F9").Action);
    }

    [Fact]
    public void Presses_right_after_focus_gain_are_ignored_until_released()
    {
        var g = Make(new InputGateOptions { FocusGuardMs = 400 });
        g.FocusGained();
        _clock.AdvanceMs(100);
        Assert.Null(g.KeyDown("F9", false).Action);
        _clock.AdvanceMs(1000);
        Assert.Null(g.KeyUp("F9").Action);
        Assert.Equal(InputAction.Speak, g.KeyDown("F9", false).Action);
    }

    [Fact]
    public void Nothing_fires_while_window_is_not_focused()
    {
        var g = Make();
        g.FocusLost();
        Assert.Null(g.KeyDown("F9", false).Action);
        Assert.Null(g.Pointer(InputAction.Speak));
    }

    [Fact]
    public void Hold_to_activate_fires_on_release_after_minimum_hold()
    {
        var o = new InputGateOptions();
        o.HoldToActivateMs[InputAction.Speak] = 250;
        var g = Make(o);

        Assert.Null(g.KeyDown("F9", false).Action);
        _clock.AdvanceMs(100);
        Assert.Null(g.KeyUp("F9").Action); // a bump: too short

        _clock.AdvanceMs(1000);
        g.KeyDown("F9", false);
        _clock.AdvanceMs(300);
        Assert.Equal(InputAction.Speak, g.KeyUp("F9").Action);
    }

    [Fact]
    public void Key_names_are_normalised()
    {
        var g = Make();
        Assert.Equal(InputAction.Select, g.KeyDown("Return", false).Action);
        Assert.Equal(InputAction.DeleteLetter, g.KeyDown("Backspace", false).Action);
    }

    [Fact]
    public void Custom_bindings_are_validated()
    {
        Assert.Throws<ArgumentException>(() => KeyMap.FromDictionary(new Dictionary<string, string> { ["F1"] = "SpeakTwice" }));
        var map = KeyMap.FromDictionary(new Dictionary<string, string> { ["NumPad5"] = "Select" });
        Assert.Equal(InputAction.Select, map.Resolve("numpad5"));
    }
}
