using Aac.Core.Abstractions;
using Aac.Core.Prediction;
using Aac.Core.Session;
using Aac.Core.Speech;

namespace Aac.Core.Tests;

/// <summary>Records what would have been spoken. Speech "finishes" only when the test says so.</summary>
public sealed class FakeSpeechEngine : ISpeechEngine, ISpeechProgress
{
    public event EventHandler<SpeechEndedEventArgs>? Ended;
    public event EventHandler<SpeechProgressEventArgs>? Progress;

    /// <summary>Pretends the voice has reached a character of the utterance's spoken text.</summary>
    public void Reach(Utterance u, int position) => Progress?.Invoke(this, new SpeechProgressEventArgs(u.Id, position));
    public List<Utterance> Spoken { get; } = new();
    public int CancelCount { get; private set; }

    public void Speak(Utterance utterance) => Spoken.Add(utterance);

    public void CancelAll()
    {
        CancelCount++;
        foreach (var u in Spoken.ToList()) Ended?.Invoke(this, new SpeechEndedEventArgs(u.Id, cancelled: true));
    }

    public void Finish(Utterance u) => Ended?.Invoke(this, new SpeechEndedEventArgs(u.Id, cancelled: false));
    public void FinishLast() => Finish(Spoken[^1]);
}

public sealed class Rig
{
    public ManualClock Clock { get; } = new();
    public FakeSpeechEngine Engine { get; } = new();
    public SpeechController Speech { get; }
    public CommunicationSession Session { get; }

    public Rig(SessionOptions? options = null, Storage.DraftStore? drafts = null, SpeechOptions? speechOptions = null)
    {
        Speech = new SpeechController(Engine, Clock, speechOptions ?? new SpeechOptions());
        Session = new CommunicationSession(Speech, WordPredictor.CreateDefault(), PhraseLibrary.CreateDefault(), options ?? new SessionOptions(), Clock, drafts);
    }

    public void Type(string text) => Session.TypeText(text);
}
