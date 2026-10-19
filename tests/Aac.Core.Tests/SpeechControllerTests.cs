using Aac.Core.Abstractions;
using Aac.Core.Speech;

namespace Aac.Core.Tests;

public class SpeechControllerTests
{
    private readonly ManualClock _clock = new();
    private readonly FakeSpeechEngine _engine = new();
    private SpeechController Make(SpeechOptions? o = null, PronunciationLexicon? lex = null) => new(_engine, _clock, o, lex);

    [Fact]
    public void Blank_messages_are_not_spoken()
    {
        var s = Make();
        Assert.Equal(SpeakOutcome.Empty, s.SpeakMessage("   "));
        Assert.Empty(_engine.Spoken);
    }

    [Fact]
    public void One_press_speaks_the_message_once()
    {
        var s = Make();
        Assert.Equal(SpeakOutcome.Started, s.SpeakMessage("I want coffee."));
        Assert.Equal("I want coffee.", Assert.Single(_engine.Spoken).DisplayText);
        Assert.Equal(UtteranceKind.Message, _engine.Spoken[0].Kind);
    }

    [Fact]
    public void A_bounced_second_press_inside_the_cooldown_does_not_replay()
    {
        var s = Make(new SpeechOptions { CooldownMs = 1500 });
        s.SpeakMessage("one");
        _engine.FinishLast();
        _clock.AdvanceMs(500);
        Assert.Equal(SpeakOutcome.CoolingDown, s.SpeakMessage("one"));
        _clock.AdvanceMs(1100);
        Assert.Equal(SpeakOutcome.Started, s.SpeakMessage("two"));
        Assert.Equal(new[] { "one", "two" }, _engine.Spoken.Select(u => u.DisplayText));
    }

    [Fact]
    public void Speak_while_speaking_is_refused()
    {
        var s = Make(new SpeechOptions { CooldownMs = 0 });
        s.SpeakMessage("first");
        Assert.Equal(SpeakOutcome.Busy, s.SpeakMessage("second"));
        Assert.Single(_engine.Spoken);
    }

    [Fact]
    public void Stop_silences_and_clears_speaking_state()
    {
        var s = Make();
        s.SpeakMessage("a long message");
        Assert.True(s.IsSpeaking);
        s.Stop();
        Assert.False(s.IsSpeaking);
        Assert.Equal(1, _engine.CancelCount);
    }

    [Fact]
    public void Late_end_event_from_an_old_utterance_does_not_clear_a_new_one()
    {
        var s = Make(new SpeechOptions { CooldownMs = 0 });
        s.SpeakMessage("old");
        var old = _engine.Spoken[^1];
        _engine.Finish(old);
        s.SpeakMessage("new");
        _engine.Finish(old); // duplicate/late event
        Assert.True(s.IsSpeaking);
        Assert.Equal("new", s.Current?.DisplayText);
    }

    [Fact]
    public void Words_are_spoken_without_starting_the_speak_cooldown_and_a_new_word_cuts_off_the_last()
    {
        var s = Make(new SpeechOptions { CooldownMs = 1500 });
        Assert.Equal(SpeakOutcome.Started, s.SpeakWord("hello"));
        Assert.Equal(SpeakOutcome.Started, s.SpeakWord("there"));
        Assert.Equal(1, _engine.CancelCount);
        Assert.Equal(UtteranceKind.Word, s.Current?.Kind);
        _engine.FinishLast();
        Assert.Equal(SpeakOutcome.Started, s.SpeakMessage("hello there"));
    }

    [Fact]
    public void A_word_never_cuts_off_a_whole_message()
    {
        var s = Make();
        s.SpeakMessage("a long story");
        Assert.Equal(SpeakOutcome.Busy, s.SpeakWord("next"));
        Assert.Equal(0, _engine.CancelCount);
    }

    [Fact]
    public void Quick_phrase_interrupts_current_speech()
    {
        var s = Make();
        s.SpeakMessage("a long story");
        Assert.Equal(SpeakOutcome.Started, s.SpeakQuickPhrase("Stop."));
        Assert.Equal(1, _engine.CancelCount);
        Assert.Equal("Stop.", s.Current?.DisplayText);
        Assert.Equal(UtteranceKind.QuickPhrase, s.Current?.Kind);
    }

    [Fact]
    public void Same_quick_phrase_is_not_repeated_by_a_bounce_but_a_different_one_is_immediate()
    {
        var s = Make(new SpeechOptions { QuickPhraseRepeatGuardMs = 2000 });
        s.SpeakQuickPhrase("Yes.");
        _clock.AdvanceMs(300);
        Assert.Equal(SpeakOutcome.CoolingDown, s.SpeakQuickPhrase("Yes."));
        Assert.Equal(SpeakOutcome.Started, s.SpeakQuickPhrase("No."));
        _clock.AdvanceMs(2500);
        Assert.Equal(SpeakOutcome.Started, s.SpeakQuickPhrase("Yes."));
    }

    [Fact]
    public void Pronunciation_changes_audio_only()
    {
        var lex = new PronunciationLexicon(new Dictionary<string, string> { ["QuadStick"] = "quad stick", ["ALS"] = "A L S" });
        var s = Make(lex: lex);
        s.SpeakMessage("My QuadStick and ALS.");
        Assert.Equal("My QuadStick and ALS.", _engine.Spoken[0].DisplayText);
        Assert.Equal("My quad stick and A L S.", _engine.Spoken[0].SpokenText);
    }

    [Fact]
    public void Pronunciation_matches_whole_words_only()
    {
        var lex = new PronunciationLexicon(new Dictionary<string, string> { ["tv"] = "tee vee" });
        Assert.Equal("tee vee and tvs", lex.Apply("tv and tvs"));
    }
}
