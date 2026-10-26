using Aac.Core.Board;
using Aac.Core.Session;

namespace Aac.Core.Tests;

/// <summary>The Last said key under Speak: says the last message again without touching the one being written.</summary>
public class LastSaidTests
{
    private static Cell Key(Rig rig) => rig.Session.Rows.SelectMany(r => r.Cells).Single(c => c.Cell.Id == "say-last").Cell;

    [Fact]
    public void It_sits_after_the_question_mark_and_fills_the_row_to_full_width()
    {
        var rig = new Rig();
        var row = rig.Session.Rows.Single(r => r.Name == "keys-2");
        Assert.Equal("key-?", row.Cells[^2].Cell.Id);
        Assert.Equal("say-last", row.Cells[^1].Cell.Id);
        Assert.Equal(12, row.Cells.Sum(c => c.Cell.Gap + c.Cell.Span));
        Assert.False(Key(rig).IsEnabled); // nothing said yet
    }

    [Fact]
    public void It_says_the_last_message_again_and_keeps_the_new_message()
    {
        var rig = new Rig();
        rig.Type("I would like some water");
        rig.Session.Speak();
        rig.Clock.AdvanceMs(5000);
        rig.Engine.FinishLast();
        Assert.True(Key(rig).IsEnabled);

        rig.Type("and a straw");
        rig.Session.ActivateCell("say-last");

        Assert.Equal("I would like some water", rig.Engine.Spoken[^1].DisplayText);
        Assert.Equal("and a straw", rig.Session.Draft);
        Assert.Single(rig.Session.CompletedMessages); // a repeat is not a new message
        Assert.False(Key(rig).IsEnabled); // greyed out while speaking
    }

    [Fact]
    public void A_quick_phrase_counts_as_the_last_thing_said_and_the_hotkey_works_too()
    {
        var rig = new Rig();
        var phrase = rig.Session.Rows.Single(r => r.Name == CommunicationSession.PinnedRow).Cells[0].Cell;
        rig.Session.ActivateCell(phrase.Id);
        rig.Clock.AdvanceMs(5000);
        rig.Engine.FinishLast();

        rig.Session.HandleAction(Input.InputAction.SayLastAgain);
        Assert.Equal(phrase.Payload, rig.Engine.Spoken[^1].DisplayText);
    }
}
