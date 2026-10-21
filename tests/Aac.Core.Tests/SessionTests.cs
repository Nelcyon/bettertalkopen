using Aac.Core.Prediction;
using Aac.Core.Abstractions;
using Aac.Core.Board;
using Aac.Core.Input;
using Aac.Core.Session;
using Aac.Core.Speech;
using Aac.Core.Storage;

namespace Aac.Core.Tests;

public class SessionTests
{
    private static string CellId(CommunicationSession s, Func<Cell, bool> pick) =>
        s.Rows.SelectMany(r => r.Cells).First(c => pick(c.Cell)).Cell.Id;

    private static void Click(Rig rig, Func<Cell, bool> pick)
    {
        rig.Clock.AdvanceMs(1000);
        rig.Session.ActivateCell(CellId(rig.Session, pick));
    }

    private static void ClickCommand(Rig rig, CommandId cmd) => Click(rig, c => c.Command == cmd);

    [Fact]
    public void Starts_on_the_keyboard_with_the_first_letter_focused_and_no_drawer()
    {
        var rig = new Rig();
        Assert.Equal(Drawer.None, rig.Session.Drawer);
        Assert.Equal("q", rig.Session.FocusedCell?.Payload);
        Assert.DoesNotContain(rig.Session.Rows, r => r.Name.StartsWith(CommunicationSession.DrawerRowPrefix));
    }

    private static Rig WithQuickOpen(SessionOptions? options = null)
    {
        var rig = new Rig(options);
        rig.Session.Open(Drawer.Quick);
        return rig;
    }

    [Fact]
    public void Quick_opens_below_the_keyboard_with_the_first_phrase_focused()
    {
        var rig = WithQuickOpen();
        Assert.Equal(Drawer.Quick, rig.Session.Drawer);
        Assert.Equal(PhraseLibrary.DefaultQuickPhrases[0], rig.Session.FocusedCell?.Label);
        Assert.StartsWith(CommunicationSession.DrawerRowPrefix, rig.Session.Rows[rig.Session.FocusRow].Name);
    }

    [Fact]
    public void One_select_on_a_quick_phrase_speaks_it_without_ai_and_the_drawer_stays_open()
    {
        var rig = WithQuickOpen();
        rig.Session.HandleAction(InputAction.Select);
        Assert.Single(rig.Engine.Spoken);
        Assert.Equal(PhraseLibrary.DefaultQuickPhrases[0], rig.Engine.Spoken[0].DisplayText);
        Assert.Equal(UtteranceKind.QuickPhrase, rig.Engine.Spoken[0].Kind);
        Assert.Equal(Drawer.Quick, rig.Session.Drawer);
    }

    [Fact]
    public void Quick_phrases_can_load_into_the_message_instead()
    {
        var rig = WithQuickOpen(new SessionOptions { QuickPhrasesSpeakImmediately = false });
        rig.Session.HandleAction(InputAction.Select);
        Assert.Empty(rig.Engine.Spoken);
        Assert.Equal(PhraseLibrary.DefaultQuickPhrases[0], rig.Session.Draft);
        Assert.Equal(CommandId.Speak, rig.Session.FocusedCell?.Command);
    }

    [Fact]
    public void Typing_never_speaks_until_speak_is_pressed_and_then_says_the_whole_message_once()
    {
        var rig = new Rig();
        rig.Type("hello there");
        Assert.Empty(rig.Engine.Spoken);
        rig.Session.HandleAction(InputAction.Speak);
        Assert.Equal("hello there", Assert.Single(rig.Engine.Spoken).DisplayText);
        Assert.Equal(UtteranceKind.Message, rig.Engine.Spoken[0].Kind);
    }

    [Fact]
    public void Speak_with_an_empty_message_says_nothing()
    {
        var rig = new Rig();
        rig.Session.HandleAction(InputAction.Speak);
        Assert.Empty(rig.Engine.Spoken);
        Assert.Contains("Nothing", rig.Session.StatusText);
        Assert.False(rig.Session.Rows.SelectMany(r => r.Cells).First(c => c.Cell.Command == CommandId.Speak).Cell.IsEnabled);
    }

    [Fact]
    public void A_bounced_or_repeated_speak_press_never_replays_the_message()
    {
        var rig = new Rig(new SessionOptions { ClearMessageAfterSpeaking = false });
        rig.Type("I want coffee");
        rig.Session.HandleAction(InputAction.Speak);
        rig.Session.HandleAction(InputAction.Speak); // still speaking
        rig.Engine.FinishLast();
        rig.Clock.AdvanceMs(300);
        rig.Session.HandleAction(InputAction.Speak); // bounce inside the cooldown
        Assert.Single(rig.Engine.Spoken);
    }

    [Fact]
    public void Message_clears_after_speaking_and_undo_restores_it_without_speaking()
    {
        var rig = new Rig();
        rig.Type("see you later");
        rig.Session.HandleAction(InputAction.Speak);
        Assert.Equal("", rig.Session.Draft);
        Assert.Equal("see you later", rig.Session.LastSpoken);
        rig.Session.HandleAction(InputAction.Undo);
        Assert.Equal("see you later", rig.Session.Draft);
        Assert.Single(rig.Engine.Spoken);
    }

    private static SessionOptions EachWord => new() { SpeakEachWord = true };

    [Fact]
    public void Word_mode_says_each_word_once_when_it_is_finished()
    {
        var rig = new Rig(EachWord);
        rig.Type("hel");
        Assert.Empty(rig.Engine.Spoken);
        rig.Type("lo ");
        rig.Type("there.");
        Assert.Equal(new[] { "hello", "there" }, rig.Engine.Spoken.Select(u => u.DisplayText));
        Assert.All(rig.Engine.Spoken, u => Assert.Equal(UtteranceKind.Word, u.Kind));
        rig.Type(" don'");
        Assert.Equal(2, rig.Engine.Spoken.Count); // an apostrophe does not finish a word
        rig.Type("t ");
        Assert.Equal("don't", rig.Engine.Spoken[^1].DisplayText);
    }

    [Fact]
    public void Word_mode_speaks_on_space_key_and_on_a_word_suggestion()
    {
        var rig = new Rig(EachWord);
        Click(rig, c => c.Payload == "h");
        Click(rig, c => c.Payload == "i");
        Click(rig, c => c.Command == CommandId.Space);
        Assert.Equal("hi", Assert.Single(rig.Engine.Spoken).DisplayText);
        rig.Engine.FinishLast();
        rig.Type("doc");
        Click(rig, c => c.Kind == CellKind.Prediction && c.Payload == "doctor");
        Assert.Equal("doctor", rig.Engine.Spoken[^1].DisplayText);
    }

    [Fact]
    public void Word_mode_never_respeaks_after_delete_or_undo_and_speak_still_reads_everything()
    {
        var rig = new Rig(EachWord);
        rig.Type("good ");
        rig.Type("morning ");
        Assert.Equal(2, rig.Engine.Spoken.Count);
        rig.Engine.FinishLast();
        rig.Session.HandleAction(InputAction.DeleteLetter); // "good morning"
        rig.Type(" ");                                      // same word finished again
        rig.Session.HandleAction(InputAction.Undo);
        rig.Session.HandleAction(InputAction.Undo);
        Assert.Equal(2, rig.Engine.Spoken.Count);

        rig.Session.HandleAction(InputAction.Speak);
        Assert.Equal("good morning", rig.Engine.Spoken[^1].DisplayText);
        Assert.Equal(UtteranceKind.Message, rig.Engine.Spoken[^1].Kind);
    }

    [Fact]
    public void Word_mode_is_off_by_default()
    {
        var rig = new Rig();
        rig.Type("hello there ");
        Assert.Empty(rig.Engine.Spoken);
    }

    [Fact]
    public void Drawers_are_mutually_exclusive_toggle_and_return_focus_to_their_button()
    {
        var rig = new Rig();
        rig.Type("water please");
        ClickCommand(rig, CommandId.GoPhrases);
        Assert.Equal(Drawer.Phrases, rig.Session.Drawer);

        ClickCommand(rig, CommandId.GoQuick); // opening another closes the first
        Assert.Equal(Drawer.Quick, rig.Session.Drawer);
        Assert.True(rig.Session.Rows.SelectMany(r => r.Cells).Single(c => c.Cell.Command == CommandId.GoQuick).Cell.IsOn);

        ClickCommand(rig, CommandId.GoQuick); // the same button again closes it
        Assert.Equal(Drawer.None, rig.Session.Drawer);
        Assert.Equal(CommandId.GoQuick, rig.Session.FocusedCell?.Command);

        ClickCommand(rig, CommandId.GoPhrases);
        rig.Session.HandleAction(InputAction.Home); // Home closes too
        Assert.Equal(Drawer.None, rig.Session.Drawer);
        Assert.Equal(CommandId.GoPhrases, rig.Session.FocusedCell?.Command);
        Assert.Equal("water please", rig.Session.Draft);
    }

    [Fact]
    public void Opening_or_closing_a_drawer_never_moves_the_keyboard()
    {
        var rig = new Rig();
        string Keyboard() => string.Join("|", rig.Session.Rows.TakeWhile(r => !r.Name.StartsWith(CommunicationSession.DrawerRowPrefix))
            .Select(r => r.Name + ":" + string.Join(",", r.Cells.Select(c => $"{c.Cell.Id}@{c.Cell.Gap}+{c.Cell.Span}"))));
        var closed = Keyboard();
        foreach (var d in new[] { Drawer.Quick, Drawer.Phrases, Drawer.None })
        {
            rig.Session.Open(d);
            Assert.Equal(closed, Keyboard());
            Assert.InRange(rig.Session.Rows.Count(r => r.Name.StartsWith(CommunicationSession.DrawerRowPrefix)), 0, CommunicationSession.DrawerRows);
        }
    }

    [Fact]
    public void In_edit_mode_buttons_are_edited_not_spoken_and_changes_apply()
    {
        var rig = new Rig();
        PhraseEdit? asked = null;
        var saved = 0;
        rig.Session.EditRequested += (_, e) => asked = e;
        rig.Session.PhrasesChanged += (_, _) => saved++;
        rig.Session.SetEditMode(true);

        Click(rig, c => c.Kind == CellKind.Pinned && c.Payload == "Feet");
        Assert.Empty(rig.Engine.Spoken);
        Assert.Equal(new PhraseEdit(PhraseList.Urgent, null, "Feet"), asked);
        rig.Session.ApplyEdit(asked!, "Please open the window.");
        Assert.Contains(rig.Session.Rows[0].Cells, c => c.Cell.Payload == "Please open the window.");

        rig.Session.Open(Drawer.Quick);
        Click(rig, c => c.Id == "add-quick");
        Assert.Equal(new PhraseEdit(PhraseList.Quick, null, null), asked);
        rig.Session.ApplyEdit(asked!, "Turn on the fan.");
        Assert.Contains(rig.Session.Rows.SelectMany(r => r.Cells), c => c.Cell.Payload == "Turn on the fan.");

        rig.Session.Open(Drawer.Phrases);
        Click(rig, c => c.Kind == CellKind.Category && c.Payload == "Needs");
        Click(rig, c => c.Kind == CellKind.Phrase && c.Payload == "I need water.");
        Assert.Equal(new PhraseEdit(PhraseList.Category, "Needs", "I need water."), asked);
        rig.Session.ApplyEdit(asked!, ""); // empty text deletes it
        Assert.DoesNotContain(rig.Session.Rows.SelectMany(r => r.Cells), c => c.Cell.Payload == "I need water.");
        Assert.Equal(3, saved);

        rig.Session.SetEditMode(false);
        Click(rig, c => c.Kind == CellKind.Pinned && c.Payload == "Please open the window.");
        Assert.Equal("Please open the window.", Assert.Single(rig.Engine.Spoken).DisplayText);
        Assert.DoesNotContain(rig.Session.Rows.SelectMany(r => r.Cells), c => c.Cell.Command == CommandId.AddPhrase);
    }

    [Fact]
    public void Phrases_show_category_chips_and_choosing_a_phrase_loads_it_closes_and_focuses_speak()
    {
        var rig = new Rig();
        ClickCommand(rig, CommandId.GoPhrases);
        var chips = rig.Session.Rows.First(r => r.Name == CommunicationSession.DrawerRowPrefix + "0").Cells.Select(c => c.Cell).ToList();
        Assert.Contains(chips, c => c.Kind == CellKind.Category && c.Payload == "Needs");
        Click(rig, c => c.Kind == CellKind.Category && c.Payload == "Needs");
        Assert.Equal(Drawer.Phrases, rig.Session.Drawer); // the grid switches inside the drawer
        Click(rig, c => c.Kind == CellKind.Phrase && c.Payload == "I need water.");
        Assert.Equal("I need water.", rig.Session.Draft);
        Assert.Equal(Drawer.None, rig.Session.Drawer);
        Assert.Equal(CommandId.Speak, rig.Session.FocusedCell?.Command);
    }

    [Fact]
    public void Stop_is_a_segment_of_the_speak_key_that_only_works_while_speaking()
    {
        var rig = new Rig();
        Cell StopKey() => rig.Session.Rows.SelectMany(r => r.Cells).Single(c => c.Cell.Command == CommandId.StopVoice).Cell;
        var row = rig.Session.Rows.First(r => r.Name == "keys-1").Cells.Select(c => c.Cell).ToList();
        Assert.Equal((CommandId.Speak, KeyPart.Start), (row[^2].Command, row[^2].Part));
        Assert.Equal((CommandId.StopVoice, KeyPart.End), (row[^1].Command, row[^1].Part));
        Assert.False(StopKey().IsEnabled);
        rig.Type("a long message");
        rig.Session.HandleAction(InputAction.Speak);
        Assert.True(StopKey().IsEnabled);
        Click(rig, c => c.Command == CommandId.StopVoice);
        Assert.False(rig.Session.IsSpeaking);
        Assert.False(StopKey().IsEnabled);
    }

    [Fact]
    public void The_suggestion_row_always_has_six_slots_so_the_keyboard_never_moves()
    {
        var rig = new Rig();
        string Layout() => string.Join("|", rig.Session.Rows.Select(r => r.Name + ":" + string.Join(",", r.Cells.Select(c => c.Cell.Id))));
        var empty = Layout();
        Assert.Equal(CommunicationSession.SuggestionSlots, rig.Session.Rows.First(r => r.Name == CommunicationSession.SuggestionsRow).Cells.Count);
        foreach (var text in new[] { "i", " want", " zzzzqx", " the documentary was great and the ending was ridiculous" })
        {
            rig.Type(text);
            Assert.Equal(empty, Layout());
        }
        var slots = rig.Session.Rows.First(r => r.Name == CommunicationSession.SuggestionsRow).Cells.Select(c => c.Cell).ToList();
        Assert.All(slots.Where(c => c.Label == ""), c => Assert.False(c.IsEnabled));
    }

    [Fact]
    public void The_likeliest_word_sits_in_the_middle_and_the_rest_alternate_outward()
    {
        Assert.Equal(new[] { 4, 2, 0, 1, 3, 5 }, Enumerable.Range(0, CommunicationSession.SuggestionSlots).Select(s => CommunicationSession.SuggestionRank(s, true)));
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5 }, Enumerable.Range(0, CommunicationSession.SuggestionSlots).Select(s => CommunicationSession.SuggestionRank(s, false)));

        string[] Row(Rig r) => r.Session.Rows.First(x => x.Name == CommunicationSession.SuggestionsRow).Cells.Select(c => c.Cell.Label).ToArray();
        var middle = new Rig();
        var left = new Rig(new SessionOptions { SuggestionsFromMiddle = false });
        middle.Type("the d");
        left.Type("the d");
        var ranked = Row(left);
        Assert.Equal(new[] { ranked[4], ranked[2], ranked[0], ranked[1], ranked[3], ranked[5] }, Row(middle));
    }

    [Fact]
    public void Prediction_completes_the_current_word()
    {
        var rig = new Rig();
                rig.Type("the doc");
        Click(rig, c => c.Kind == CellKind.Prediction && c.Payload == "documentary");
        Assert.Equal("the documentary ", rig.Session.Draft);
    }

    [Fact]
    public void Letters_space_and_delete_edit_the_message()
    {
        var rig = new Rig();
                Click(rig, c => c.Payload == "h");
        Click(rig, c => c.Payload == "i");
        ClickCommand(rig, CommandId.Space);
        Click(rig, c => c.Payload == "e");
        ClickCommand(rig, CommandId.DeleteLetter);
        Assert.Equal("hi ", rig.Session.Draft);
        ClickCommand(rig, CommandId.Clear);
        Assert.Equal("", rig.Session.Draft);
        ClickCommand(rig, CommandId.Undo);
        Assert.Equal("hi ", rig.Session.Draft);
    }

    [Fact]
    public void Double_click_on_the_same_target_is_ignored()
    {
        var rig = new Rig();
                var id = CellId(rig.Session, c => c.Payload == "a");
        rig.Clock.AdvanceMs(1000);
        rig.Session.ActivateCell(id);
        rig.Clock.AdvanceMs(100);
        rig.Session.ActivateCell(id);
        Assert.Equal("a", rig.Session.Draft);
    }

    [Fact]
    public void Phrase_categories_load_phrases_without_speaking()
    {
        var rig = new Rig();
        ClickCommand(rig, CommandId.GoPhrases);
        Click(rig, c => c.Kind == CellKind.Category && c.Payload == "Needs");
        Click(rig, c => c.Kind == CellKind.Phrase && c.Payload == "I need water.");
        Assert.Equal("I need water.", rig.Session.Draft);
        Assert.Empty(rig.Engine.Spoken);
        Assert.Equal(CommandId.Speak, rig.Session.FocusedCell?.Command);
    }

    [Fact]
    public void Learning_can_be_turned_off()
    {
        var rig = new Rig(new SessionOptions { LearnFromSpokenMessages = false });
        rig.Type("zelda tonight");
        rig.Session.HandleAction(InputAction.Speak);
                rig.Type("z");
        Assert.DoesNotContain(rig.Session.Rows.SelectMany(r => r.Cells), c => c.Cell.Payload == "zelda");
    }

    [Fact]
    public void Learning_when_enabled_uses_spoken_messages()
    {
        var rig = new Rig(new SessionOptions { LearnFromSpokenMessages = true });
        rig.Type("zelda tonight");
        rig.Session.HandleAction(InputAction.Speak);
                rig.Type("z");
        Assert.Contains(rig.Session.Rows.SelectMany(r => r.Cells), c => c.Cell.Payload == "zelda");
    }

    private static void SayTyped(Rig rig, string text)
    {
        rig.Clock.AdvanceMs(5000);
        rig.Type(text);
        rig.Session.HandleAction(InputAction.Speak);
        rig.Engine.FinishLast();
    }

    [Fact]
    public void Messages_said_twice_become_most_used_phrases()
    {
        var rig = new Rig();
        var learned = 0;
        rig.Session.LearningChanged += (_, _) => learned++;
        SayTyped(rig, "Can you turn on the hockey game");
        ClickCommand(rig, CommandId.GoPhrases);
        Assert.DoesNotContain(rig.Session.Rows.SelectMany(r => r.Cells), c => c.Cell.Payload == CommunicationSession.FrequentCategory);

        SayTyped(rig, "can you turn on the hockey game");
        rig.Session.Open(Drawer.Phrases); // still open from before; pressing Phrases again would close it
        Click(rig, c => c.Payload == CommunicationSession.FrequentCategory);
        Click(rig, c => c.Kind == CellKind.Phrase && c.Payload == "can you turn on the hockey game");
        Assert.Equal("can you turn on the hockey game", rig.Session.Draft);
        Assert.Empty(rig.Engine.Spoken.Skip(2)); // loading a phrase never speaks it
        Assert.Equal(2, learned);
    }

    [Fact]
    public void Forget_learning_needs_two_selections_and_anything_else_cancels()
    {
        var rig = new Rig();
        SayTyped(rig, "zelda tonight");
        SayTyped(rig, "zelda tonight");
        ClickCommand(rig, CommandId.GoPhrases);

        ClickCommand(rig, CommandId.ForgetLearning);
        Assert.Contains("Confirm forget", rig.Session.Rows.SelectMany(r => r.Cells).Select(c => c.Cell.Label));
        Click(rig, c => c.Payload == CommunicationSession.FrequentCategory); // something else: cancels
        ClickCommand(rig, CommandId.ForgetLearning);
        Assert.Contains(rig.Session.Rows.SelectMany(r => r.Cells), c => c.Cell.Payload == CommunicationSession.FrequentCategory);

        ClickCommand(rig, CommandId.ForgetLearning);
        Assert.DoesNotContain(rig.Session.Rows.SelectMany(r => r.Cells), c => c.Cell.Payload == CommunicationSession.FrequentCategory);
        Assert.Contains("Forgot", rig.Session.StatusText);
    }

    [Fact]
    public void Metrics_count_actions_per_message_without_storing_text()
    {
        var rig = WithQuickOpen();
        rig.Session.HandleAction(InputAction.MoveRight); // to the second quick phrase
        rig.Session.HandleAction(InputAction.Select);
        var m = Assert.Single(rig.Session.CompletedMessages);
        Assert.Equal(2, m.Actions);
        Assert.Equal(MessageKind.Quick, m.Kind);
        Assert.Equal(PhraseLibrary.DefaultQuickPhrases[1].Length, m.Characters);
    }

    [Fact]
    public void Recovered_draft_is_shown_but_never_spoken_on_its_own()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            var path = Path.Combine(dir.FullName, "draft.bin");
            new DraftStore(path, new PassThroughProtector()).Save("I was saying something ");
            var rig = new Rig(new SessionOptions { SpeakEachWord = true }, drafts: new DraftStore(path, new PassThroughProtector()));
            Assert.Equal("I was saying something ", rig.Session.Draft);
            Assert.Contains("Recovered", rig.Session.StatusText);
            rig.Session.HandleAction(InputAction.MoveRight);
            rig.Type("else ");
            Assert.Equal("else", Assert.Single(rig.Engine.Spoken).DisplayText); // only the new word
        }
        finally { dir.Delete(true); }
    }

    [Theory]
    [InlineData(Drawer.Quick)]
    [InlineData(Drawer.None)]
    [InlineData(Drawer.Phrases)]
    public void Urgent_phrases_are_one_selection_away_whatever_is_open(Drawer drawer)
    {
        var rig = new Rig();
        rig.Session.Open(drawer);
        Assert.Equal(CommunicationSession.PinnedRow, rig.Session.Rows[0].Name);
        Assert.NotEqual(CommunicationSession.PinnedRow, rig.Session.Rows[rig.Session.FocusRow].Name);
        Click(rig, c => c.Kind == CellKind.Pinned && c.Payload == "Tube Feeding please!");
        Assert.Equal("Tube Feeding please!", Assert.Single(rig.Engine.Spoken).DisplayText);
    }

    [Fact]
    public void Older_settings_without_pinned_phrases_get_the_defaults_and_an_empty_list_hides_the_strip()
    {
        Assert.Contains(PhraseLibrary.DefaultPinnedPhrases[0], new PhraseLibrary().Pinned);
        Assert.Empty(new PhraseLibrary { PinnedPhrases = new() }.Pinned);
    }

    [Fact]
    public void Keyboard_has_backspace_after_p_as_a_split_key_and_a_wide_space_bar()
    {
        var rig = new Rig();
                var top = rig.Session.Rows.First(r => r.Name == "keys-0").Cells.Select(c => c.Cell).ToList();
        Assert.Equal(new[] { "p", null, null }, top.Skip(9).Select(c => c.Payload));
        Assert.Equal((CommandId.DeleteLetter, KeyPart.Start), (top[10].Command, top[10].Part));
        Assert.Equal((CommandId.DeleteWord, KeyPart.End), (top[11].Command, top[11].Part));

        var edit = rig.Session.Rows.First(r => r.Name == "edit").Cells.Select(c => c.Cell).ToList();
        Assert.Equal(new[] { CommandId.GoQuick, CommandId.GoPhrases, CommandId.Undo, CommandId.Space, CommandId.Clear, CommandId.OpenSettings, CommandId.HideWindow },
            edit.Select(c => c.Command));
        Assert.True(edit.Single(c => c.Command == CommandId.Space).Span > 1);
        Assert.Equal(CommandId.HideWindow, edit[^1].Command);
        Assert.Equal(12, edit.Sum(c => c.Gap + c.Span)); // Settings and Hide end exactly on the keyboard's right edge
    }

    [Fact]
    public void Speak_sits_where_enter_would_be_and_is_the_only_speak_button()
    {
        var rig = new Rig();
        var middle = rig.Session.Rows.First(r => r.Name == "keys-1").Cells.Select(c => c.Cell).ToList();
        Assert.Equal(CommandId.Speak, middle[^2].Command);
        Assert.Equal("Speak", middle[^2].Label);
        Assert.Single(rig.Session.Rows.SelectMany(r => r.Cells), c => c.Cell.Command == CommandId.Speak);
        rig.Session.Open(Drawer.Phrases);
        Assert.Single(rig.Session.Rows.SelectMany(r => r.Cells), c => c.Cell.Command == CommandId.Speak);
    }

    [Fact]
    public void Without_the_hide_key_nothing_else_on_the_bottom_row_moves()
    {
        static string Layout(Rig r) => string.Join(",", r.Session.Rows.First(x => x.Name == "edit").Geometry.Cells.Select(c => $"{c.Start}+{c.Width}"));
        var with = new Rig();
        var without = new Rig(new SessionOptions { ShowHideKey = false });
        Assert.DoesNotContain(without.Session.Rows.SelectMany(r => r.Cells), c => c.Cell.Command == CommandId.HideWindow);
        Assert.StartsWith(Layout(without), Layout(with));
        Assert.Equal(12, without.Session.Rows.First(x => x.Name == "edit").Geometry.Total);
    }

    [Fact]
    public void Keyboard_keys_are_all_one_size_and_rows_are_staggered_like_a_real_keyboard()
    {
        var rig = new Rig();
        var keyRows = rig.Session.Rows.Where(r => r.Name.StartsWith("keys-") || r.Name == "edit").ToList();
        Assert.All(keyRows, r => Assert.Equal(12, r.Units));
        Assert.All(keyRows.SelectMany(r => r.Cells).Where(c => c.Cell.Kind == CellKind.Letter), c => Assert.Equal(1, c.Cell.Span));
        Assert.Equal(new[] { 0.0, 0.5, 1.5 }, keyRows.Take(3).Select(r => r.Geometry.Cells[0].Start));
        // Q sits above A and Z is under S, as on a real keyboard.
        Click(rig, c => c.Payload == "s");
        rig.Session.HandleAction(InputAction.MoveDown);
        Assert.Equal("z", rig.Session.FocusedCell?.Payload);
    }

    [Fact]
    public void On_screen_delete_keys_take_fast_repeated_clicks_but_letters_keep_the_bounce_guard()
    {
        var rig = new Rig();
        rig.Type("abcdef");
        for (var i = 0; i < 3; i++)
        {
            rig.Session.ActivateCell("del-letter");
            rig.Clock.AdvanceMs(60);
        }
        Assert.Equal("abc", rig.Session.Draft);
        rig.Session.ActivateCell("key-x");
        rig.Clock.AdvanceMs(60);
        rig.Session.ActivateCell("key-x"); // a bounce
        Assert.Equal("abcx", rig.Session.Draft);
    }

    [Fact]
    public void Up_from_the_space_bar_lands_on_the_letters_drawn_above_it()
    {
        var rig = new Rig();
                Click(rig, c => c.Command == CommandId.Space);
        rig.Session.HandleAction(InputAction.MoveUp);
        Assert.Contains(rig.Session.FocusedCell?.Payload, new[] { "c", "v", "b", "n" });
    }

    [Fact]
    public void Settings_key_asks_the_host_to_open_settings_and_stays_when_hide_is_off()
    {
        var rig = new Rig(new SessionOptions { ShowHideKey = false });
        var asked = 0;
        rig.Session.SettingsRequested += (_, _) => asked++;
        ClickCommand(rig, CommandId.OpenSettings);
        Assert.Equal(1, asked);
    }

    [Fact]
    public void Hide_asks_the_host_to_hide_the_window()
    {
        var rig = new Rig();
        var asked = 0;
        rig.Session.WindowToggleRequested += (_, _) => asked++;
        ClickCommand(rig, CommandId.HideWindow);
        Assert.Equal(1, asked);
    }

    [Fact]
    public void Stop_voice_is_reachable_and_silences()
    {
        var rig = new Rig();
        rig.Type("a long message");
        rig.Session.HandleAction(InputAction.Speak);
        Assert.True(rig.Session.IsSpeaking);
        rig.Session.HandleAction(InputAction.StopVoice);
        Assert.False(rig.Session.IsSpeaking);
    }
}

public class StorageTests
{
    [Fact]
    public void Corner_opens_with_a_click_by_default_and_older_files_keep_it_on()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            Assert.True(new AppSettings().OpenFromCorner);
            Assert.Equal(CornerTrigger.Click, new AppSettings().CornerTrigger);

            var path = Path.Combine(dir.FullName, "settings.json");
            File.WriteAllText(path, "{ \"FontSize\": 30 }"); // written before the corner existed
            var old = SettingsStore.Load(path, out _);
            Assert.True(old.OpenFromCorner);
            Assert.Equal(CornerTrigger.Click, old.CornerTrigger);

            File.WriteAllText(path, "{ \"OpenFromTopLeftCorner\": true, \"CornerTrigger\": \"Rest\" }"); // the setting's older name
            var rest = SettingsStore.Load(path, out _);
            Assert.True(rest.OpenFromCorner);
            Assert.Equal(CornerTrigger.Rest, rest.CornerTrigger);
        }
        finally { dir.Delete(true); }
    }

    [Fact]
    public void Settings_round_trip_and_bad_files_fall_back_to_defaults()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            var path = Path.Combine(dir.FullName, "settings.json");
            var s = new AppSettings { FontSize = 44 };
            s.KeyBindings["NumPad5"] = "Select";
            SettingsStore.Save(path, s);
            var loaded = SettingsStore.Load(path, out var problem);
            Assert.Null(problem);
            Assert.Equal(44, loaded.FontSize);
            Assert.Equal("Select", loaded.KeyBindings["NumPad5"]);
            Assert.True(loaded.Privacy.LearnFromSpokenMessages);
            Assert.False(loaded.LockWindowSize);
            s.LockWindowSize = true;
            SettingsStore.Save(path, s);
            Assert.True(SettingsStore.Load(path, out _).LockWindowSize);
            Assert.False(loaded.Generation.LocalAiEnabled);

            File.WriteAllText(path, "{ not json");
            var fallback = SettingsStore.Load(path, out problem);
            Assert.NotNull(problem);
            Assert.Equal(new AppSettings().FontSize, fallback.FontSize);
        }
        finally { dir.Delete(true); }
    }

    [Fact]
    public void Draft_store_encrypts_through_the_protector_and_clears()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            var path = Path.Combine(dir.FullName, "draft.bin");
            var store = new DraftStore(path, new XorProtector());
            store.Save("secret words");
            Assert.DoesNotContain("secret", File.ReadAllText(path));
            Assert.Equal("secret words", store.Load());
            store.Save("");
            Assert.False(File.Exists(path));
        }
        finally { dir.Delete(true); }
    }

    [Fact]
    public void Learned_model_round_trips_encrypted()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            var p = WordPredictor.CreateDefault();
            var memory = new PhraseMemory();
            p.Learn("play zelda tonight");
            p.Learn("play zelda tonight"); // word pairs are only saved once they have been said twice
            memory.Record("Play Zelda tonight", DateTimeOffset.UnixEpoch);
            memory.Record("play zelda tonight", DateTimeOffset.UnixEpoch);
            var data = new PersonalData();
            p.ExportPersonal(data);
            memory.Export(data);

            var store = new PersonalModelStore(Path.Combine(dir.FullName, "learned.bin"), new XorProtector());
            store.Save(data);
            Assert.DoesNotContain("zelda", File.ReadAllText(Path.Combine(dir.FullName, "learned.bin")));

            var loaded = store.Load();
            var p2 = WordPredictor.CreateDefault();
            var m2 = new PhraseMemory();
            p2.ImportPersonal(loaded);
            m2.Import(loaded);
            Assert.Equal("zelda", p2.Predict("play ")[0]);
            Assert.Equal(new[] { "play zelda tonight" }, m2.Frequent(5));
        }
        finally { dir.Delete(true); }
    }

    private sealed class XorProtector : IDataProtector
    {
        public byte[] Protect(byte[] plain) => plain.Select(b => (byte)(b ^ 0x5A)).ToArray();
        public byte[] Unprotect(byte[] cipher) => Protect(cipher);
    }
}
