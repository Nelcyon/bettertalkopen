using System.Text.Json;
using Aac.Core.Board;
using Aac.Core.Input;
using Aac.Core.Prediction;
using Aac.Core.Session;
using Aac.Core.Storage;

namespace Aac.Core.Tests;

/// <summary>The Itch screen: "Move the figure" under the figure, and the words said out loud in the strip at the top.</summary>
public class SpokenPanelTests
{
    private static IEnumerable<RowView> Figure(Rig rig) => rig.Session.Rows.Where(r => r.Name.StartsWith(CommunicationSession.ItchRowPrefix, StringComparison.Ordinal));
    private static RowView Strip(Rig rig) => rig.Session.Rows.Single(r => r.Name == CommunicationSession.PinnedRow);
    private static IEnumerable<CellView> Spoken(Rig rig) => Strip(rig).Cells.Where(c => c.Cell.Kind == CellKind.Spoken);

    [Fact]
    public void The_figure_has_a_panel_and_the_controls_a_titled_block_of_tool_buttons_that_the_controller_icon_folds_away()
    {
        var rig = new Rig();
        rig.Session.OpenItch(true);

        Assert.Equal(new[] { "itch-view", CommunicationSession.ItchMoveTitleRow, "itch-move-turn", "itch-move-up", "itch-move-middle", "itch-move-down" }, Figure(rig).Select(r => r.Name));
        var title = Figure(rig).Single(r => r.Name == CommunicationSession.ItchMoveTitleRow);
        Assert.Equal("Move the figure", title.Title);
        Assert.Empty(title.Cells);
        // Every button in the controls is a tool; there is no Say button here (speaking is the keyboard's Speak key).
        var buttons = Figure(rig).SelectMany(r => r.Cells).Select(c => c.Cell).ToList();
        Assert.Equal(13, buttons.Count);
        Assert.All(buttons, c => Assert.Equal(CellKind.Tool, c.Kind));
        Assert.DoesNotContain(buttons, c => c.Id == "itch-say");
        Assert.Contains(buttons, c => c.Id == "itch-centre" && c.Caption == "Whole body");

        // Tools work like commands: pressing Turn turns the figure.
        var yaw = rig.Session.ItchYaw;
        rig.Session.ActivateCell("itch-turn-right");
        Assert.NotEqual(yaw, rig.Session.ItchYaw);

        // The controller icon over the figure folds the controls panel away (its rows go) and brings it back.
        Assert.True(rig.Session.ItchControlsOpen);
        var told = 0;
        rig.Session.PropertyChanged += (_, _) => told++;
        rig.Session.ToggleItchControls();
        Assert.False(rig.Session.ItchControlsOpen);
        Assert.True(told > 0);
        Assert.Equal(new[] { "itch-view" }, Figure(rig).Select(r => r.Name));
        rig.Session.ToggleItchControls();
        Assert.Equal(4 + 1 + 1, Figure(rig).Count()); // figure, title and four rows of buttons
        // Folded away, it stays folded the next time Itch opens (never opened by itself), and the change is told so it is saved.
        var saved = 0;
        rig.Session.ItchControlsChanged += (_, _) => saved++;
        rig.Session.ToggleItchControls();
        rig.Session.OpenItch(false);
        rig.Session.OpenItch(true);
        Assert.False(rig.Session.ItchControlsOpen);
        Assert.Equal(1, saved);
    }

    [Fact]
    public void Every_way_of_moving_the_figure_has_its_own_button_so_left_clicks_are_enough()
    {
        var rig = new Rig();
        rig.Session.OpenItch(true);
        var ids = Figure(rig).SelectMany(r => r.Cells).Select(c => c.Cell.Id).ToHashSet();
        foreach (var id in new[] { "itch-turn-left", "itch-turn-right", "itch-tilt-above", "itch-tilt-below", "itch-pan-up", "itch-pan-down",
                                    "itch-pan-left", "itch-pan-right", "itch-zoom-in", "itch-zoom-out", "itch-front", "itch-back", "itch-centre" })
            Assert.Contains(id, ids);

        void Press(string id) { rig.Clock.AdvanceMs(1000); rig.Session.ActivateCell(id); }

        var pitch = rig.Session.ItchPitch;
        Press("itch-tilt-above");
        Assert.Equal(pitch + CommunicationSession.ItchTiltStep, rig.Session.ItchPitch);
        Press("itch-tilt-below"); Press("itch-tilt-below");
        Assert.Equal(pitch - CommunicationSession.ItchTiltStep, rig.Session.ItchPitch);

        Press("itch-zoom-in"); Press("itch-zoom-in"); // zoomed in, so there is room to slide
        var home = rig.Session.ItchTarget;
        Press("itch-pan-up");
        Assert.True(rig.Session.ItchTarget.Y > home.Y);
        Press("itch-pan-down"); Press("itch-pan-down");
        Assert.True(rig.Session.ItchTarget.Y < home.Y);
        var x = rig.Session.ItchTarget.X;
        Press("itch-pan-right");
        var right = rig.Session.ItchTarget.X;
        Press("itch-pan-left"); Press("itch-pan-left");
        Assert.NotEqual(right, rig.Session.ItchTarget.X);
        Assert.NotEqual(x, right);

        Press("itch-centre");
        Assert.Equal(1, rig.Session.ItchZoom);
        Assert.Equal(CommunicationSession.ItchHome, rig.Session.ItchTarget);

        // The same movements can be given keys in Settings.
        rig.Session.HandleAction(InputAction.DollTiltAbove);
        Assert.Equal(CommunicationSession.ItchTiltStep, rig.Session.ItchPitch);
    }

    [Fact]
    public void Opening_Itch_swaps_the_urgent_strip_for_the_spoken_words_and_closing_brings_it_back()
    {
        var rig = new Rig();
        var urgent = Strip(rig).Cells.Select(c => c.Cell).ToList();
        Assert.All(urgent.SkipLast(1), c => Assert.Equal(CellKind.Pinned, c.Kind));
        Assert.Equal(PhraseLibrary.DefaultPinnedPhrases, urgent.SkipLast(1).Select(c => c.Payload));
        Assert.Equal("itch", urgent[^1].Id);
        Assert.Equal("Itch ▶", urgent[^1].Label); // the arrow says it opens to the right
        Assert.False(urgent[^1].IsOn);

        rig.Session.OpenItch(true);
        var open = Strip(rig).Cells.Select(c => c.Cell).ToList();
        Assert.Equal(CommunicationSession.PinnedRow, rig.Session.Rows[0].Name); // same place: the keyboard does not move
        Assert.Equal("itch", open[^1].Id); // the Itch button stays at the right end
        Assert.Equal("Itch ◀", open[^1].Label);
        Assert.True(open[^1].IsOn);
        var words = open.SkipLast(1).ToList();
        Assert.All(words, c => Assert.Equal(CellKind.Spoken, c.Kind));
        Assert.Equal(new[] { "Up", "Down", "Left", "Right" }.Concat(PhraseLibrary.DefaultSpokenPhrases), words.Select(c => c.Payload));
        Assert.Equal(new[] { "▲ Up", "▼ Down", "◀ Left", "▶ Right" }, words.Take(4).Select(c => c.Label));
        foreach (var old in new[] { "Harder", "Gentler", "Right there!", "A little more" })
            Assert.DoesNotContain(words, c => c.Payload == old);
        // No panel of their own any more: only the figure's rows are added.
        Assert.DoesNotContain(rig.Session.Rows, r => r.Name.StartsWith("spoken-", StringComparison.Ordinal));

        rig.Clock.AdvanceMs(1000);
        rig.Session.ActivateCell("itch");
        Assert.Equal(urgent.Select(c => c.Id), Strip(rig).Cells.Select(c => c.Cell.Id).ToList());
    }

    [Fact]
    public void The_Itch_arrow_follows_the_side_the_panel_opens_on()
    {
        var rig = new Rig(new SessionOptions { ItchOpensRight = false });
        Assert.Equal("◀ Itch", Strip(rig).Cells[^1].Cell.Label);
        rig.Session.OpenItch(true);
        Assert.Equal("▶ Itch", Strip(rig).Cells[^1].Cell.Label);
    }

    [Fact]
    public void Spoken_words_are_said_at_once_even_when_quick_phrases_are_not()
    {
        var rig = new Rig(new SessionOptions { QuickPhrasesSpeakImmediately = false });
        rig.Session.OpenItch(true);
        rig.Type("left shoulder");

        rig.Session.ActivateCell("say-Stop");
        rig.Engine.FinishLast();
        Assert.Equal("Stop", Assert.Single(rig.Engine.Spoken).DisplayText);
        Assert.Equal("left shoulder", rig.Session.Draft); // the message being written is untouched

        rig.Clock.AdvanceMs(3000);
        rig.Session.ActivateCell("dir-Up");
        rig.Engine.FinishLast();
        Assert.Equal(new[] { "Stop", "Up" }, rig.Engine.Spoken.Select(u => u.DisplayText));
    }

    [Fact]
    public void Spoken_words_can_be_renamed_added_and_deleted_in_edit_mode_but_the_arrows_cannot()
    {
        var rig = new Rig();
        PhraseEdit? asked = null;
        var saved = 0;
        rig.Session.EditRequested += (_, e) => asked = e;
        rig.Session.PhrasesChanged += (_, _) => saved++;
        rig.Session.OpenItch(true);
        rig.Session.SetEditMode(true);

        rig.Session.ActivateCell("say-Keep going");
        Assert.Empty(rig.Engine.Spoken);
        Assert.Equal(new PhraseEdit(PhraseList.Spoken, null, "Keep going"), asked);
        rig.Session.ApplyEdit(asked!, "A bit more");
        Assert.Contains(Spoken(rig), c => c.Cell.Payload == "A bit more");

        rig.Clock.AdvanceMs(1000);
        rig.Session.ActivateCell("add-spoken");
        Assert.Equal(new PhraseEdit(PhraseList.Spoken, null, null), asked);
        rig.Session.ApplyEdit(asked!, "Other side");
        Assert.Contains(Spoken(rig), c => c.Cell.Payload == "Other side");

        rig.Session.ApplyEdit(new PhraseEdit(PhraseList.Spoken, null, "Stop"), ""); // empty text deletes it
        Assert.DoesNotContain(Spoken(rig), c => c.Cell.Payload == "Stop");
        Assert.Equal(3, saved);

        // The arrows are fixed words: in edit mode they are still said, never edited (like "Most used" phrases).
        asked = null;
        rig.Clock.AdvanceMs(1000);
        rig.Session.ActivateCell("dir-Up");
        Assert.Null(asked);
        Assert.Equal("Up", Assert.Single(rig.Engine.Spoken).DisplayText);
    }

    [Fact]
    public void Older_button_files_without_spoken_words_get_the_defaults_and_new_files_carry_them()
    {
        var old = JsonSerializer.Deserialize<PhraseLibrary>("""{"QuickPhrases":["Hi"],"Categories":[]}""", SettingsStore.JsonOptions)!;
        Assert.Null(old.SpokenPhrases);
        Assert.Equal(PhraseLibrary.DefaultSpokenPhrases, old.Spoken);

        var fresh = JsonSerializer.Serialize(PhraseLibrary.CreateDefault(), SettingsStore.JsonOptions);
        Assert.Contains("SpokenPhrases", fresh);
        Assert.Contains("Scratch softer", fresh);
    }
}

/// <summary>Button edit mode: a button can be moved earlier or later in its row.</summary>
public class ButtonOrderTests
{
    [Fact]
    public void A_red_button_moves_earlier_and_later_and_stops_at_the_ends()
    {
        var rig = new Rig();
        rig.Session.SetEditMode(true);
        var first = PhraseLibrary.DefaultPinnedPhrases[0];
        var second = PhraseLibrary.DefaultPinnedPhrases[1];
        var edit = new PhraseEdit(PhraseList.Urgent, null, second);
        Assert.Equal((2, PhraseLibrary.DefaultPinnedPhrases.Count), rig.Session.PlaceOf(edit));
        Assert.True(rig.Session.MoveButton(edit, -1));
        Assert.Equal(new[] { second, first }, Strip(rig).Take(2));
        Assert.False(rig.Session.MoveButton(edit, -1)); // already first
        Assert.True(rig.Session.MoveButton(edit, +1));
        Assert.Equal(new[] { first, second }, Strip(rig).Take(2));
    }

    [Fact]
    public void Spoken_words_can_be_reordered_too()
    {
        var rig = new Rig();
        var words = PhraseLibrary.DefaultSpokenPhrases;
        var edit = new PhraseEdit(PhraseList.Spoken, null, words[^1]);
        Assert.True(rig.Session.MoveButton(edit, -(words.Count - 1)));
        Assert.Equal((1, words.Count), rig.Session.PlaceOf(edit));
    }

    private static IEnumerable<string?> Strip(Rig rig) =>
        rig.Session.Rows[0].Cells.Select(c => c.Cell).Where(c => c.Kind == CellKind.Pinned).Select(c => c.Payload);
}
