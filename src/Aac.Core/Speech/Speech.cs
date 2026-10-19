using System.Text.RegularExpressions;
using Aac.Core.Abstractions;

namespace Aac.Core.Speech;

public enum UtteranceKind
{
    /// <summary>A whole message the user composed, spoken when they pressed Speak.</summary>
    Message,
    /// <summary>One word, spoken as soon as it was finished (word-by-word mode).</summary>
    Word,
    /// <summary>A fixed rapid-response phrase (Yes, No, I need help...). Never AI generated.</summary>
    QuickPhrase,
}

public sealed record Utterance(long Id, string DisplayText, string SpokenText, UtteranceKind Kind);

public sealed class SpeechEndedEventArgs(long utteranceId, bool cancelled) : EventArgs
{
    public long UtteranceId { get; } = utteranceId;
    public bool Cancelled { get; } = cancelled;
}

/// <summary>How far an engine has got through an utterance: the character it is saying, in its spoken text.</summary>
public sealed class SpeechProgressEventArgs(long utteranceId, int characterPosition) : EventArgs
{
    public long UtteranceId { get; } = utteranceId;
    public int CharacterPosition { get; } = characterPosition;
}

/// <summary>An engine that can say which part of the text it is speaking (so the words can be highlighted).</summary>
public interface ISpeechProgress
{
    event EventHandler<SpeechProgressEventArgs>? Progress;
}

/// <summary>A TTS backend. Speak must not block; Ended fires once per utterance (completed or cancelled).</summary>
public interface ISpeechEngine
{
    event EventHandler<SpeechEndedEventArgs>? Ended;
    void Speak(Utterance utterance);
    void CancelAll();
}

public enum SpeakOutcome
{
    Started,
    CoolingDown,
    Busy,
    Empty,
}

public sealed class SpeechOptions
{
    /// <summary>After any speech starts, further Speak presses are ignored for this long (bounce / double-press guard).</summary>
    public int CooldownMs { get; set; } = 1500;

    /// <summary>The same quick phrase cannot be re-triggered within this window (a different one can, immediately).</summary>
    public int QuickPhraseRepeatGuardMs { get; set; } = 2000;

    /// <summary>When a new quick phrase is triggered while something is being spoken, interrupt it.</summary>
    public bool QuickPhraseInterrupts { get; set; } = true;
}

/// <summary>
/// The only path to the speaker. Enforces the safety rules:
/// - a message is spoken only when the user presses Speak, once per press (the separate Approve step
///   was removed);
/// - cooldown blocks bounced or double presses;
/// - quick phrases are deterministic and guard against repeats;
/// - nothing here ever re-speaks history on its own.
/// </summary>
public sealed class SpeechController
{
    private readonly ISpeechEngine _engine;
    private readonly IClock _clock;
    private readonly SpeechOptions _options;
    private readonly PronunciationLexicon _lexicon;
    private readonly object _gate = new();
    private long _nextId;
    private Utterance? _current;
    private DateTimeOffset? _lastStart;
    private (string Text, DateTimeOffset At)? _lastQuick;

    public SpeechController(ISpeechEngine engine, IClock clock, SpeechOptions? options = null, PronunciationLexicon? lexicon = null)
    {
        _engine = engine;
        _clock = clock;
        _options = options ?? new SpeechOptions();
        _lexicon = lexicon ?? new PronunciationLexicon();
        _engine.Ended += OnEngineEnded;
        if (_engine is ISpeechProgress progress) progress.Progress += OnEngineProgress;
    }

    /// <summary>Raised as speech moves on to the next word (see <see cref="SpokenWord"/>).</summary>
    public event EventHandler? ProgressChanged;

    /// <summary>
    /// Which word of the current utterance's displayed text is being said (0 = the first), or null when
    /// nothing is being spoken or the engine has not said yet.
    /// </summary>
    public int? SpokenWord { get { lock (_gate) return _current is null ? null : _spokenWord; } }
    private int? _spokenWord;

    private void OnEngineProgress(object? sender, SpeechProgressEventArgs e)
    {
        bool changed;
        lock (_gate)
        {
            if (_current is null || _current.Id != e.UtteranceId) return;
            var word = WordAt(_current, e.CharacterPosition);
            changed = word != _spokenWord;
            _spokenWord = word;
        }
        if (changed) ProgressChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// The displayed word at a character of the spoken text. Pronunciation rules can change the spoken
    /// words, so the word number is carried over in proportion.
    /// </summary>
    public static int WordAt(Utterance u, int spokenPosition)
    {
        var spoken = Words(u.SpokenText);
        var shown = Words(u.DisplayText);
        if (spoken.Count == 0 || shown.Count == 0) return 0;
        var i = spoken.FindLastIndex(w => w.Start <= spokenPosition);
        if (i < 0) i = 0;
        return spoken.Count == shown.Count ? i : Math.Min(shown.Count - 1, (int)Math.Round(i * (shown.Count - 1) / (double)Math.Max(1, spoken.Count - 1)));
    }

    /// <summary>The words of a text, as (start, length), split on spaces.</summary>
    public static List<(int Start, int Length)> Words(string text) =>
        Regex.Matches(text, @"\S+").Select(m => (m.Index, m.Length)).ToList();

    public event EventHandler? StateChanged;

    public bool IsSpeaking { get { lock (_gate) return _current is not null; } }
    public Utterance? Current { get { lock (_gate) return _current; } }
    public string? LastSpokenText { get; private set; }

    /// <summary>Every utterance ever sent to the engine, in order. Used by tests and metrics.</summary>
    public IReadOnlyList<Utterance> SpokenLog => _log;
    private readonly List<Utterance> _log = new();

    /// <summary>Speaks a composed message now. One call speaks once; presses during speech or the cooldown are ignored.</summary>
    public SpeakOutcome SpeakMessage(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0) return SpeakOutcome.Empty;
        Utterance u;
        lock (_gate)
        {
            if (_current is not null) return SpeakOutcome.Busy;
            if (InCooldown()) return SpeakOutcome.CoolingDown;
            u = Start(trimmed, UtteranceKind.Message);
        }
        _engine.Speak(u);
        Raise();
        return SpeakOutcome.Started;
    }

    /// <summary>
    /// Speaks one just-finished word (word-by-word mode). A new word cuts off one still being said, and
    /// words do not start the Speak cooldown, so reading the whole message right after still works.
    /// </summary>
    public SpeakOutcome SpeakWord(string word)
    {
        var trimmed = word.Trim();
        if (trimmed.Length == 0) return SpeakOutcome.Empty;
        Utterance u;
        bool interrupt;
        lock (_gate)
        {
            if (_current is { Kind: not UtteranceKind.Word }) return SpeakOutcome.Busy;
            interrupt = _current is not null;
            _current = null;
            u = Start(trimmed, UtteranceKind.Word, startsCooldown: false);
        }
        if (interrupt) _engine.CancelAll();
        _engine.Speak(u);
        Raise();
        return SpeakOutcome.Started;
    }

    /// <summary>Speaks a fixed rapid-response phrase immediately. No approval needed; never passes through AI.</summary>
    public SpeakOutcome SpeakQuickPhrase(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0) return SpeakOutcome.Empty;
        Utterance u;
        bool interrupt;
        lock (_gate)
        {
            var now = _clock.Now;
            if (_lastQuick is { } lq && lq.Text == trimmed && (now - lq.At).TotalMilliseconds < _options.QuickPhraseRepeatGuardMs)
                return SpeakOutcome.CoolingDown;
            if (_current is not null && !_options.QuickPhraseInterrupts) return SpeakOutcome.Busy;
            interrupt = _current is not null;
            _current = null;
            _lastQuick = (trimmed, now);
            u = Start(trimmed, UtteranceKind.QuickPhrase);
        }
        if (interrupt) _engine.CancelAll();
        _engine.Speak(u);
        Raise();
        return SpeakOutcome.Started;
    }

    /// <summary>Silences speech immediately. Always available.</summary>
    public void Stop()
    {
        lock (_gate) _current = null;
        _engine.CancelAll();
        Raise();
    }

    private bool InCooldown() =>
        _lastStart is { } t && (_clock.Now - t).TotalMilliseconds < _options.CooldownMs;

    private Utterance Start(string text, UtteranceKind kind, bool startsCooldown = true)
    {
        var u = new Utterance(++_nextId, text, _lexicon.Apply(text), kind);
        _current = u;
        _spokenWord = null;
        if (startsCooldown) _lastStart = _clock.Now;
        LastSpokenText = text;
        _log.Add(u);
        return u;
    }

    private void OnEngineEnded(object? sender, SpeechEndedEventArgs e)
    {
        bool changed;
        lock (_gate)
        {
            // Only the utterance we are tracking may clear the speaking state; late events for
            // cancelled utterances are ignored so they cannot disturb a newer one.
            changed = _current is not null && _current.Id == e.UtteranceId;
            if (changed) _current = null;
        }
        if (changed) Raise();
    }

    private void Raise() => StateChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// An ellipsis ("..." or "…") in what is said is a pause: "Hang on... let me think" says "Hang on", waits a
/// second, then says "let me think". Engines speak the parts in turn with the pause between them.
/// </summary>
public static class SpeechPauses
{
    public const int PauseMs = 1000;

    private static readonly Regex Ellipsis = new(@"\.{3,}|…", RegexOptions.Compiled);

    /// <summary>The parts to say, each with where it starts in the text; a pause goes between each two.</summary>
    public static IReadOnlyList<(string Text, int Start)> Split(string text)
    {
        var parts = new List<(string, int)>();
        var at = 0;
        void Add(int end)
        {
            var piece = text[at..end];
            // Only parts with something to say: "..." on its own, or at either end, adds no pause.
            if (piece.Any(char.IsLetterOrDigit)) parts.Add((piece.Trim(), at + piece.Length - piece.TrimStart().Length));
        }
        foreach (Match m in Ellipsis.Matches(text))
        {
            Add(m.Index);
            at = m.Index + m.Length;
        }
        Add(text.Length);
        return parts;
    }
}

/// <summary>Whole-word replacements applied only to the audio, never to the displayed text.</summary>
public sealed class PronunciationLexicon
{
    private readonly List<(Regex Pattern, string Replacement)> _rules = new();

    public PronunciationLexicon(IDictionary<string, string>? entries = null)
    {
        if (entries is null) return;
        foreach (var (word, sayAs) in entries) Add(word, sayAs);
    }

    public void Add(string word, string sayAs)
    {
        if (string.IsNullOrWhiteSpace(word)) return;
        _rules.Add((new Regex($@"(?<![\w']){Regex.Escape(word.Trim())}(?![\w'])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), sayAs));
    }

    public string Apply(string text)
    {
        foreach (var (pattern, replacement) in _rules) text = pattern.Replace(text, replacement);
        return text;
    }
}
