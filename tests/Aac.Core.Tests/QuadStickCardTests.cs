using Aac.Core.Input;
using Aac.Core.Storage;
using Xunit;

namespace Aac.Core.Tests;

public class QuadStickCardTests
{
    [Fact]
    public void Select_speaks_unless_the_focus_frame_is_showing()
    {
        Assert.Equal(InputAction.Speak, FocusHighlightPolicy.Resolve(InputAction.Select, frameShown: false));
        Assert.Equal(InputAction.Select, FocusHighlightPolicy.Resolve(InputAction.Select, frameShown: true));
        Assert.Equal(InputAction.DeleteLetter, FocusHighlightPolicy.Resolve(InputAction.DeleteLetter, frameShown: false));
    }

    [Fact]
    public void Squares_are_sorted_into_what_they_mean_to_BetterTalk()
    {
        Assert.Equal(CardSendKind.Key, QuadStickCard.KindOf(new CardCell("Enter")));
        Assert.Equal(CardSendKind.Key, QuadStickCard.KindOf(new CardCell("RightClick")));
        Assert.Equal(CardSendKind.LeftClick, QuadStickCard.KindOf(new CardCell("LeftClick")));
        Assert.Equal(CardSendKind.Clipboard, QuadStickCard.KindOf(new CardCell("Ctrl+V")));
        Assert.Equal(CardSendKind.Elsewhere, QuadStickCard.KindOf(new CardCell(Note: "MODE +")));
        Assert.Equal(CardSendKind.Nothing, QuadStickCard.KindOf(new CardCell()));
        Assert.Equal(CardSendKind.Nothing, QuadStickCard.KindOf(null));
    }

    [Fact]
    public void Keys_are_written_the_way_people_say_them()
    {
        Assert.Equal("Backspace", QuadStickCard.Display("Back"));
        Assert.Equal("Right click", QuadStickCard.Display("RightClick"));
        Assert.Equal("Ctrl-C", QuadStickCard.Display("Ctrl+C"));
        Assert.Equal("Escape", QuadStickCard.Display("Escape"));
    }

    [Fact]
    public void The_usual_card_works_with_the_default_keys_and_survives_a_save()
    {
        var card = QuadStickCard.Default();
        foreach (var id in card.Keys)
        {
            var parts = id.Split('|');
            Assert.Contains(parts[0], QuadStickCard.Gestures);
            Assert.Contains(parts[1], QuadStickCard.Tubes);
        }
        Assert.Equal(nameof(InputAction.Select), KeyMap.DefaultBindings[card[QuadStickCard.CellId("Sip", "Right")].Key!]);
        Assert.Equal(nameof(InputAction.DeleteLetter), KeyMap.DefaultBindings[card[QuadStickCard.CellId("Puff", "Right")].Key!]);

        var json = System.Text.Json.JsonSerializer.Serialize(new AppSettings());
        var back = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json)!;
        Assert.Equal(card[QuadStickCard.CellId("Sip", "Side")], back.QuadStickCard[QuadStickCard.CellId("Sip", "Side")]);
        Assert.Equal(card.Count, back.QuadStickCard.Count);
    }
}
