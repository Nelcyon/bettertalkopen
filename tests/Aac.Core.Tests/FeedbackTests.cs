using Aac.Core.Board;
using Aac.Core.Abstractions;
using Aac.Core.Input;

namespace Aac.Core.Tests;

public class FeedbackTests
{
    private static Cell SpeakKey(Rig rig) => rig.Session.Rows.SelectMany(r => r.Cells).First(c => c.Cell.Id == "speak").Cell;

    [Fact]
    public void Feedback_mode_puts_the_message_aside_and_brings_it_back()
    {
        var rig = new Rig();
        rig.Type("I want tea");
        rig.Session.SetFeedbackMode(true);
        Assert.Equal("", rig.Session.Draft);
        Assert.Equal("Send feedback", SpeakKey(rig).Label);
        rig.Type("the keys are too small");
        rig.Session.SetFeedbackMode(false);
        Assert.Equal("I want tea", rig.Session.Draft);
        Assert.Equal("Speak", SpeakKey(rig).Label);
    }

    [Fact]
    public void Send_feedback_hands_over_the_text_and_never_speaks_it()
    {
        var rig = new Rig();
        string? sent = null;
        rig.Session.FeedbackSubmitted += (_, text) => sent = text;
        rig.Session.SetFeedbackMode(true);
        rig.Type("  please add a bigger font ");
        rig.Session.HandleAction(InputAction.Speak);
        Assert.Equal("please add a bigger font", sent);
        Assert.Empty(rig.Engine.Spoken);
        Assert.False(SpeakKey(rig).IsEnabled); // no second send while the first is on its way
    }

    [Fact]
    public void Feedback_is_never_learned()
    {
        var predictor = Prediction.WordPredictor.CreateDefault();
        var clock = new ManualClock();
        var session = new Session.CommunicationSession(new Speech.SpeechController(new FakeSpeechEngine(), clock, new Speech.SpeechOptions()),
            predictor, Prediction.PhraseLibrary.CreateDefault(), new Session.SessionOptions(), clock);
        session.SetFeedbackMode(true);
        session.TypeText("zebracorn widgets zebracorn widgets");
        session.Speak();
        Assert.False(predictor.HasLearned);
    }

    [Fact]
    public void Sent_feedback_ends_feedback_mode_and_a_failure_keeps_the_text()
    {
        var rig = new Rig();
        rig.Type("hello");
        rig.Session.SetFeedbackMode(true);
        rig.Type("it crashed");
        rig.Session.Speak();
        rig.Session.FeedbackDone(finished: false, "Could not send.");
        Assert.True(rig.Session.FeedbackMode);
        Assert.Equal("it crashed", rig.Session.Draft);
        Assert.True(SpeakKey(rig).IsEnabled);
        rig.Session.Speak();
        rig.Session.FeedbackDone(finished: true, "Thank you.");
        Assert.False(rig.Session.FeedbackMode);
        Assert.Equal("hello", rig.Session.Draft);
    }

    [Fact]
    public void Feedback_text_is_not_kept_as_the_unfinished_message()
    {
        var path = Path.Combine(Path.GetTempPath(), $"bt-feedback-{Guid.NewGuid():N}.bin");
        try
        {
            var drafts = new Storage.DraftStore(path, new PassThroughProtector());
            var rig = new Rig(drafts: drafts);
            rig.Type("my message");
            rig.Session.SetFeedbackMode(true);
            rig.Type("secret feedback");
            Assert.Equal("my message", drafts.Load());
        }
        finally { File.Delete(path); }
    }
}

public class FeedbackSendingTests
{
    [Fact]
    public void Only_the_words_and_the_two_versions_are_sent()
    {
        var json = Feedback.FeedbackItem.Create(" too slow ", "0.3.9", "Windows 11 (10.0.22631)", new DateTime(2026, 10, 22, 0, 0, 0, DateTimeKind.Utc)).ToJson();
        var doc = System.Text.Json.JsonDocument.Parse(json).RootElement;
        Assert.Equal(new[] { "text", "version", "windows", "written" }, doc.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal("too slow", doc.GetProperty("text").GetString());
    }

    [Theory]
    [InlineData("{\"url\": \"\"}", false)]
    [InlineData("{\"url\": \"http://example.com/x\"}", false)]
    [InlineData("not json", false)]
    [InlineData("{\"url\": \"https://script.google.com/macros/s/abc/exec\"}", true)]
    public void Only_an_https_address_switches_sending_on(string json, bool on) =>
        Assert.Equal(on, Feedback.FeedbackConfig.Parse(json).Endpoint is not null);

    [Fact]
    public async Task Unsent_feedback_waits_and_goes_later_in_order()
    {
        var path = Path.Combine(Path.GetTempPath(), $"bt-outbox-{Guid.NewGuid():N}.json");
        try
        {
            var outbox = new Feedback.FeedbackOutbox(path);
            outbox.Add(Feedback.FeedbackItem.Create("one", "0.3.9", "w", DateTime.UtcNow));
            outbox.Add(Feedback.FeedbackItem.Create("two", "0.3.9", "w", DateTime.UtcNow));
            Assert.Equal(0, await outbox.FlushAsync(_ => throw new HttpRequestException("offline")));
            Assert.Equal(2, outbox.Load().Count);
            var sent = new List<string>();
            Assert.Equal(2, await outbox.FlushAsync(i => { sent.Add(i.Text); return Task.FromResult(true); }));
            Assert.Equal(new[] { "one", "two" }, sent);
            Assert.Empty(outbox.Load());
            Assert.False(File.Exists(path));
        }
        finally { File.Delete(path); }
    }
}
