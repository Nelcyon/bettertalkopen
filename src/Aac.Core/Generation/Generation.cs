namespace Aac.Core.Generation;

public enum CandidateSource
{
    /// <summary>Exactly what the user typed, tidied (capital letter, full stop). Always offered first.</summary>
    AsTyped,
    /// <summary>Offline rule-based expansion. Deterministic.</summary>
    Offline,
    /// <summary>A language model running on this computer.</summary>
    LocalAi,
}

/// <summary>A proposal. Never spoken unless the user selects it, approves it, and presses Speak.</summary>
public sealed record SentenceCandidate(string Text, CandidateSource Source, IReadOnlyList<string> AddedWords);

public sealed record GenerationRequest(string Keywords, IReadOnlyList<string> RecentMessages, int MaxCandidates);

public interface ISentenceGenerator
{
    string Name { get; }
    CandidateSource Source { get; }
    Task<IReadOnlyList<string>> GenerateAsync(GenerationRequest request, CancellationToken cancellationToken);
}

public static class TextTidy
{
    /// <summary>Capitalises the first letter, capitalises a standalone "i", and adds a full stop if no end punctuation.</summary>
    public static string AsSentence(string text)
    {
        var t = System.Text.RegularExpressions.Regex.Replace(text.Trim(), @"\s+", " ");
        if (t.Length == 0) return t;
        t = System.Text.RegularExpressions.Regex.Replace(t, @"(?<![\w'])i(?=('m|'ll|'d|'ve)?(?![\w']))", "I");
        t = char.ToUpperInvariant(t[0]) + t[1..];
        if (!".!?".Contains(t[^1])) t += ".";
        return t;
    }
}
