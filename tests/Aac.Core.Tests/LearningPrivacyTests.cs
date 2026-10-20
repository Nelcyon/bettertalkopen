using System.Text.Json;
using Aac.Core.Abstractions;
using Aac.Core.Prediction;
using Aac.Core.Session;
using Aac.Core.Speech;
using Aac.Core.Storage;

namespace Aac.Core.Tests;

/// <summary>What the learning keeps on disk: a way to suggest the user's own words, not a record of everything said.</summary>
public class LearningPrivacyTests
{
    private static readonly DateTimeOffset Day1 = new(2026, 3, 1, 14, 35, 12, TimeSpan.Zero);

    private static (PhraseMemory Memory, PersonalData Data) Saved(Action<PhraseMemory> act, WordPredictor? predictor = null)
    {
        var memory = new PhraseMemory();
        act(memory);
        var data = new PersonalData();
        predictor?.ExportPersonal(data);
        memory.Export(data);
        return (memory, data);
    }

    [Fact]
    public void A_message_said_once_is_kept_only_as_a_fingerprint_and_its_text_comes_when_it_repeats()
    {
        var (memory, data) = Saved(m => m.Record("My knee hurts when I lie flat", Day1));
        var once = Assert.Single(data.Phrases);
        Assert.Equal("", once.Value.Text);
        Assert.DoesNotContain("knee", JsonSerializer.Serialize(data));
        Assert.Empty(memory.Frequent(10));

        memory.Record("my knee hurts when I lie flat!", Day1.AddDays(2)); // same message again, other case and punctuation
        memory.Export(data);
        Assert.Equal(new[] { "my knee hurts when I lie flat!" }, memory.Frequent(10));
        Assert.Contains("knee", JsonSerializer.Serialize(data)); // now it is one of the user's frequent phrases
    }

    [Fact]
    public void Only_the_day_is_kept_and_the_fingerprint_cannot_be_read_without_this_PCs_secret()
    {
        var (_, data) = Saved(m => m.Record("see you at the clinic", Day1));
        Assert.Equal(new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero), data.Phrases.Single().Value.LastUsed);
        Assert.False(string.IsNullOrEmpty(data.Salt));
        Assert.DoesNotContain("clinic", data.Phrases.Single().Key);

        // Another PC (another secret) makes a different fingerprint for the same words.
        var (_, other) = Saved(m => m.Record("see you at the clinic", Day1));
        Assert.NotEqual(data.Phrases.Single().Key, other.Phrases.Single().Key);
    }

    [Fact]
    public void Things_that_look_private_are_not_kept_or_learned()
    {
        foreach (var text in new[] { "my password is purple", "call me on 5551234", "send it to dana@example.com", "the pin is 12", "my card number" })
        {
            Assert.True(PhraseMemory.LooksPrivate(text), text);
            var (memory, data) = Saved(m => { m.Record(text, Day1); m.Record(text, Day1); });
            Assert.Equal(0, memory.Count);
            Assert.Empty(data.Phrases);
        }
        Assert.False(PhraseMemory.LooksPrivate("could you open the window"));

        // And the session does not even teach the word model from them.
        var engine = new FakeSpeechEngine();
        var clock = new ManualClock();
        var predictor = WordPredictor.CreateDefault();
        var session = new CommunicationSession(new SpeechController(engine, clock, new SpeechOptions()), predictor, PhraseLibrary.CreateDefault(),
            new SessionOptions(), clock, null, new PhraseMemory());
        session.TypeText("my password is zebrafish");
        session.Speak();
        Assert.False(predictor.HasLearned);
        clock.AdvanceMs(5000);
        engine.FinishLast();
        session.ActivateCell("clear");
        session.TypeText("open the window please");
        session.Speak();
        Assert.True(predictor.HasLearned);
    }

    [Fact]
    public void Word_pairs_are_saved_only_once_they_have_been_said_twice_and_a_year_unused_is_forgotten()
    {
        var predictor = WordPredictor.CreateDefault();
        predictor.Learn("take my blue pill");
        predictor.Learn("take my red pill");
        var (memory, data) = Saved(_ => { }, predictor);
        Assert.Contains("take my", data.WordPairs.Keys);
        Assert.DoesNotContain("my blue", data.WordPairs.Keys);
        Assert.DoesNotContain("blue pill", data.WordPairs.Keys);

        memory.Record("old news", Day1);
        memory.Record("old news", Day1);
        memory.Record("something new", Day1.AddDays(PhraseMemory.MaxAgeDays + 5)); // a year and more later
        Assert.Equal(1, memory.Count);
        Assert.Empty(memory.Frequent(10));
    }

    [Fact]
    public void Files_from_older_versions_lose_the_text_of_one_off_messages_when_they_are_read()
    {
        var old = new PersonalData();
        old.Phrases["said once about my doctor"] = new PhraseUse { Text = "Said once about my doctor", Count = 1, LastUsed = Day1 };
        old.Phrases["see you tomorrow"] = new PhraseUse { Text = "See you tomorrow", Count = 3, LastUsed = Day1 };
        old.Phrases["my password is zebra"] = new PhraseUse { Text = "My password is zebra", Count = 4, LastUsed = Day1 };

        var memory = new PhraseMemory();
        memory.Import(old);
        Assert.Equal(new[] { "See you tomorrow" }, memory.Frequent(10));
        var now = new PersonalData();
        memory.Export(now);
        Assert.DoesNotContain("doctor", JsonSerializer.Serialize(now));
        Assert.DoesNotContain("zebra", JsonSerializer.Serialize(now));
        Assert.Equal(2, now.Phrases.Count); // the repeated phrase and the fingerprint of the one-off
    }
}
