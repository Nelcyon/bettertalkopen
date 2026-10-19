using Aac.Core.Speech;

namespace Aac.Core.Tests;

public class ReadAloudTests
{
    [Fact]
    public void The_spoken_message_stays_in_the_box_with_the_word_being_said_marked_until_it_finishes()
    {
        var rig = new Rig();
        rig.Type("I need water now");
        rig.Session.Speak();
        Assert.Equal("", rig.Session.Draft); // cleared for the next message
        Assert.Equal("I need water now", rig.Session.ReadAloudText); // but still shown while it is said
        Assert.Null(rig.Session.ReadAloudWord);

        var moved = 0;
        rig.Session.SpeechProgress += (_, _) => moved++;
        var u = rig.Engine.Spoken[^1];
        rig.Engine.Reach(u, 0);
        Assert.Equal(0, rig.Session.ReadAloudWord);
        rig.Engine.Reach(u, "I need ".Length + 2); // inside "water"
        Assert.Equal(2, rig.Session.ReadAloudWord);
        rig.Engine.Reach(u, "I need wa".Length); // same word again: no new event
        Assert.Equal(2, moved);

        rig.Engine.FinishLast();
        Assert.Null(rig.Session.ReadAloudText);
        Assert.Null(rig.Session.ReadAloudWord);
    }

    [Fact]
    public void Typing_the_next_message_takes_the_box_back_and_single_words_are_not_shown()
    {
        var rig = new Rig();
        rig.Type("Hello there");
        rig.Session.Speak();
        rig.Type("ok");
        Assert.Null(rig.Session.ReadAloudText);
        Assert.Equal("ok", rig.Session.Draft);
    }

    [Fact]
    public void Pronunciation_changes_still_mark_the_right_shown_word()
    {
        var u = new Utterance(1, "Call ALS team now", "Call A L S team now", UtteranceKind.Message);
        Assert.Equal(0, SpeechController.WordAt(u, 0));
        Assert.Equal(3, SpeechController.WordAt(u, "Call A L S team ".Length)); // "now"
        var same = new Utterance(2, "Yes please", "Yes please", UtteranceKind.QuickPhrase);
        Assert.Equal(1, SpeechController.WordAt(same, 5));
    }
}

public class SpeechPauseTests
{
    [Theory]
    [InlineData("Hang on... let me think", new[] { "Hang on", "let me think" })]
    [InlineData("Hang on… let me think", new[] { "Hang on", "let me think" })]
    [InlineData("Wait.... ok....... now", new[] { "Wait", "ok", "now" })]
    [InlineData("Hello there.", new[] { "Hello there." })]
    [InlineData("...Hello...", new[] { "Hello" })]
    public void An_ellipsis_splits_what_is_said_into_parts_with_a_pause_between(string text, string[] parts) =>
        Assert.Equal(parts, SpeechPauses.Split(text).Select(p => p.Text));

    [Fact]
    public void Each_part_knows_where_it_starts_so_the_words_can_still_be_highlighted()
    {
        var parts = SpeechPauses.Split("Hang on... let me think");
        Assert.Equal(0, parts[0].Start);
        Assert.Equal("Hang on... ".Length, parts[1].Start);
    }
}
