using System.Text.RegularExpressions;

namespace Aac.Core.Generation;

public sealed record ValidationResult(bool Accepted, string? Reason, IReadOnlyList<string> AddedWords);

/// <summary>
/// Deterministic checks applied to every generated sentence before the user sees it.
/// A candidate is rejected if it drops a keyword, flips negation, changes numbers, or adds a name.
/// Content words it adds (e.g. "watched") are reported so the UI can show what was invented.
/// </summary>
public static partial class CandidateValidator
{
    public const int MaxLength = 220;

    private static readonly HashSet<string> Negators = new(StringComparer.Ordinal)
    {
        "not", "no", "never", "nothing", "nobody", "none", "neither", "nor", "nowhere",
        "don't", "dont", "doesn't", "doesnt", "didn't", "didnt", "can't", "cant", "cannot", "won't", "wont",
        "isn't", "isnt", "aren't", "arent", "wasn't", "wasnt", "weren't", "werent", "shouldn't", "wouldn't",
        "couldn't", "haven't", "hasn't", "hadn't", "mustn't",
    };

    /// <summary>Function words that may be added freely without being reported as invented content.</summary>
    internal static readonly HashSet<string> GlueWords = new(StringComparer.Ordinal)
    {
        "a", "an", "the", "and", "or", "but", "so", "to", "of", "in", "on", "at", "for", "from", "with", "about",
        "by", "as", "is", "are", "was", "were", "be", "been", "am", "it", "it's", "its", "that", "this", "these",
        "those", "there", "here", "i", "i'm", "me", "my", "you", "your", "we", "us", "our", "he", "she", "they",
        "them", "do", "did", "does", "can", "could", "would", "will", "should", "please", "some", "any", "very",
        "really", "just", "too", "what", "how", "if", "then", "than", "up", "out", "like",
    };

    public static ValidationResult Validate(string keywords, string candidate)
    {
        var text = candidate.Trim();
        if (text.Length == 0) return Reject("empty");
        if (text.Length > MaxLength) return Reject("too long");

        var input = Tokens(keywords);
        var output = Tokens(text);

        foreach (var kw in input.Where(t => !GlueWords.Contains(t) && !Negators.Contains(t)))
        {
            if (!output.Any(o => SameWord(kw, o))) return Reject($"drops \"{kw}\"");
        }

        if (input.Any(IsNegation) != output.Any(IsNegation)) return Reject("changes negation");

        var inNumbers = input.Where(IsNumber).ToHashSet();
        var outNumbers = output.Where(IsNumber).ToHashSet();
        if (!inNumbers.SetEquals(outNumbers)) return Reject("changes numbers");

        foreach (var name in CapitalisedWords(text))
        {
            if (!input.Any(i => SameWord(i, name.ToLowerInvariant()))) return Reject($"adds name \"{name}\"");
        }

        var added = output
            .Where(o => !GlueWords.Contains(o) && !IsNegation(o) && !input.Any(i => SameWord(i, o)))
            .Distinct()
            .ToList();
        return new ValidationResult(true, null, added);
    }

    private static ValidationResult Reject(string reason) => new(false, reason, Array.Empty<string>());

    internal static List<string> Tokens(string text) =>
        TokenRegex().Matches(text.ToLowerInvariant().Replace('’', '\'')).Select(m => m.Value).ToList();

    private static bool IsNegation(string t) => Negators.Contains(t) || t.EndsWith("n't", StringComparison.Ordinal);

    private static bool IsNumber(string t) => t.All(char.IsDigit);

    /// <summary>Loose morphological match: watch / watched / watching, end / ending.</summary>
    internal static bool SameWord(string a, string b)
    {
        if (a == b) return true;
        var sa = Stem(a);
        var sb = Stem(b);
        if (sa == sb) return true;
        return Math.Min(sa.Length, sb.Length) >= 4 && (sa.StartsWith(sb, StringComparison.Ordinal) || sb.StartsWith(sa, StringComparison.Ordinal));
    }

    internal static string Stem(string w)
    {
        foreach (var suffix in new[] { "ing", "ed", "es", "s", "ly" })
        {
            if (w.Length > suffix.Length + 2 && w.EndsWith(suffix, StringComparison.Ordinal)) return w[..^suffix.Length];
        }
        return w;
    }

    /// <summary>Capitalised words that are not at the start of a sentence and are not "I"/"I'm"...</summary>
    private static IEnumerable<string> CapitalisedWords(string text)
    {
        var sentenceStart = true;
        foreach (Match m in RawWordRegex().Matches(text))
        {
            var word = m.Value;
            var preceding = m.Index > 0 ? text[..m.Index].TrimEnd() : "";
            if (preceding.Length == 0 || ".!?:".Contains(preceding[^1])) sentenceStart = true;
            if (!sentenceStart && char.IsUpper(word[0]) && !Regex.IsMatch(word, "^I('[a-z]+)?$")) yield return word.TrimEnd('\'');
            sentenceStart = false;
        }
    }

    [GeneratedRegex(@"[a-z0-9]+(?:'[a-z]+)?")]
    private static partial Regex TokenRegex();

    [GeneratedRegex(@"[A-Za-z0-9]+(?:'[A-Za-z]+)?")]
    private static partial Regex RawWordRegex();
}
