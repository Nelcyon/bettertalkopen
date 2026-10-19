namespace Aac.Core.Generation;

/// <summary>
/// Offline, deterministic keyword-to-sentence expansion using small word lists and templates.
/// It only arranges the user's own keywords with function words; it never adds nouns, names or events.
/// Deliberately modest: it is the fallback that always works, not the main source of fluent sentences.
/// </summary>
public sealed class RuleBasedGenerator : ISentenceGenerator
{
    public string Name => "Offline rules";
    public CandidateSource Source => CandidateSource.Offline;

    private static readonly HashSet<string> PastTime = new() { "yesterday", "earlier", "lastnight" };
    private static readonly HashSet<string> TimeWords = new() { "yesterday", "today", "tonight", "tomorrow", "now", "later", "earlier", "lastnight", "morning", "afternoon", "evening", "weekend" };
    private static readonly HashSet<string> QuestionWords = new() { "what", "when", "where", "who", "why", "how", "which" };
    private static readonly HashSet<string> Negations = new() { "not", "no", "don't", "dont", "never" };
    private static readonly HashSet<string> Verbs = new()
    {
        "want", "need", "like", "love", "hate", "watch", "play", "see", "go", "eat", "drink", "call", "help",
        "move", "turn", "open", "close", "read", "listen", "talk", "sleep", "rest", "try", "buy", "make", "take",
        "give", "tell", "know", "remember", "forget", "feel", "visit", "finish", "start",
    };
    private static readonly HashSet<string> Adjectives = new()
    {
        "ridiculous", "good", "bad", "great", "funny", "boring", "amazing", "awesome", "terrible", "hot", "cold",
        "tired", "hungry", "thirsty", "sore", "comfortable", "uncomfortable", "happy", "sad", "angry", "worried",
        "excited", "scary", "interesting", "beautiful", "loud", "quiet", "slow", "fast", "easy", "hard",
        "difficult", "nice", "wrong", "right", "perfect", "weird", "strange", "cool", "fun", "stupid", "awful",
        "fantastic", "brilliant", "annoying", "confusing", "sick", "better", "worse", "ok", "okay", "fine",
        "painful", "itchy", "dark", "bright", "late", "early", "long", "short", "expensive", "cheap", "ready",
        "bored", "cute", "silly", "unfair", "sorry", "dry", "wet",
    };

    public Task<IReadOnlyList<string>> GenerateAsync(GenerationRequest request, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<string>>(Generate(request.Keywords).Take(request.MaxCandidates).ToList());

    public static IEnumerable<string> Generate(string keywords)
    {
        var raw = CandidateValidator.Tokens(keywords.Replace("last night", "lastnight", StringComparison.OrdinalIgnoreCase));
        if (raw.Count == 0) yield break;

        string? time = null, adj = null, verb = null, question = null;
        var neg = false;
        var polite = raw.Contains("please");
        var nouns = new List<string>();
        foreach (var t in raw)
        {
            if (time is null && TimeWords.Contains(t)) time = t;
            else if (question is null && QuestionWords.Contains(t) && nouns.Count == 0 && verb is null) question = t;
            else if (Negations.Contains(t)) neg = true;
            else if (verb is null && Verbs.Contains(t)) verb = t;
            else if (adj is null && Adjectives.Contains(t)) adj = t;
            else if (!CandidateValidator.GlueWords.Contains(t)) nouns.Add(t);
        }

        var timeText = time == "lastnight" ? "last night" : time;
        var past = time is not null && PastTime.Contains(time);
        var be = past ? "was" : "is";
        var beNeg = neg ? $"{be} not" : be;
        string T(string s) => TextTidy.AsSentence(s);
        string WithTime(string s) => timeText is null ? s : $"{s} {timeText}";

        if (question is not null && nouns.Count >= 1 && verb is null && adj is null)
        {
            yield return T($"{question} {be} the {string.Join(" ", nouns)}{(timeText is null ? "" : " " + timeText)}?");
        }

        if (verb is not null && question is null && adj is null)
        {
            var obj = nouns.Count > 0 ? " " + string.Join(" and ", nouns) : "";
            var doNeg = neg ? "don't " : "";
            yield return T(WithTime($"I {doNeg}{verb}{obj}"));
            if (!neg)
            {
                // Requests: the most common use ("call nurse" -> "Can you call the nurse?").
                var theObj = nouns.Count > 0 ? " the " + string.Join(" and ", nouns) : "";
                yield return T(WithTime($"Can you {verb}{theObj}") + (polite ? ", please?" : "?"));
                if (verb is not ("want" or "need" or "like" or "love" or "hate" or "know" or "remember" or "forget" or "feel"))
                {
                    yield return T(WithTime($"I want to {verb}{obj}"));
                    yield return T(WithTime($"Please {verb}{theObj}"));
                }
                if (nouns.Count > 0) yield return T(WithTime($"Do you {verb}{obj}") + "?");
            }
            if (neg && nouns.Count > 0) yield return T(WithTime($"I really don't {verb}{obj}"));
        }

        // Just things ("water", "tv remote", "blanket please"): the everyday requests and questions about them.
        if (nouns.Count > 0 && verb is null && adj is null && question is null)
        {
            var things = string.Join(" ", nouns);
            var please = polite ? ", please" : "";
            if (neg)
            {
                yield return T(WithTime($"I don't want {things}"));
                yield return T(WithTime($"I don't need {things}"));
                yield return T(WithTime($"No {things}") + (polite ? ", please" : ""));
            }
            else
            {
                yield return T(WithTime($"I need {things}") + please);
                yield return T(WithTime($"Can I have some {things}") + please + "?");
                yield return T(WithTime($"I want {things}") + please);
                yield return T(WithTime($"Can you get me the {things}") + please + "?");
                yield return T(WithTime($"Where is the {things}") + "?");
            }
        }

        if (adj is not null && verb is null && question is null)
        {
            if (nouns.Count == 0)
            {
                yield return T(WithTime($"I'm {(neg ? "not " : "")}{adj}"));
                yield return T(WithTime($"I feel {(neg ? "not " : "")}{adj}"));
                yield return T(WithTime($"Are you {(neg ? "not " : "")}{adj}") + "?");
            }
            else if (nouns.Count == 1)
            {
                yield return T($"The {WithTime(nouns[0])} {beNeg} {adj}");
                yield return T($"I think the {WithTime(nouns[0])} {beNeg} {adj}");
                yield return T($"{(past ? "Was" : "Is")} the {WithTime(nouns[0])} {(neg ? "not " : "")}{adj}?");
            }
            else if (nouns.Count == 2)
            {
                yield return T($"The {nouns[1]} of the {WithTime(nouns[0])} {beNeg} {adj}");
                if (past) yield return $"Did you see the {WithTime(nouns[0])}? The {nouns[1]} {beNeg} {adj}.";
                yield return T($"I think the {nouns[1]} of the {WithTime(nouns[0])} {beNeg} {adj}");
            }
        }
    }
}
