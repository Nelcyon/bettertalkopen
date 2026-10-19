namespace Aac.Core.Prediction;

/// <summary>What the app has learned from how the user communicates. Saved encrypted on this PC only.</summary>
public sealed class PersonalData
{
    public Dictionary<string, double> Words { get; set; } = new();
    /// <summary>Key is "previous next".</summary>
    public Dictionary<string, double> WordPairs { get; set; } = new();
    public Dictionary<string, PhraseUse> Phrases { get; set; } = new();
}

public sealed class PhraseUse
{
    public string Text { get; set; } = "";
    public int Count { get; set; }
    public DateTimeOffset LastUsed { get; set; }
}

/// <summary>
/// Remembers the messages the user actually says so that their own frequent sentences become
/// one-selection phrases. Never reorders the fixed quick responses (their positions stay predictable).
/// </summary>
public sealed class PhraseMemory
{
    public const int MaxPhrases = 500;
    public const int MaxLength = 200;
    /// <summary>A message must be said this many times before it is offered as a frequent phrase.</summary>
    public const int MinUses = 2;

    private readonly Dictionary<string, PhraseUse> _phrases = new(StringComparer.Ordinal);

    public void Record(string text, DateTimeOffset now)
    {
        var display = text.Trim();
        if (display.Length == 0 || display.Length > MaxLength) return;
        var key = Key(display);
        if (!_phrases.TryGetValue(key, out var use)) _phrases[key] = use = new PhraseUse();
        use.Text = display;
        use.Count++;
        use.LastUsed = now;
        if (_phrases.Count > MaxPhrases)
        {
            var weakest = _phrases.OrderBy(kv => kv.Value.Count).ThenBy(kv => kv.Value.LastUsed).First().Key;
            _phrases.Remove(weakest);
        }
    }

    /// <summary>Most-used first; ties go to the most recent.</summary>
    public IReadOnlyList<string> Frequent(int max) => _phrases.Values
        .Where(p => p.Count >= MinUses)
        .OrderByDescending(p => p.Count)
        .ThenByDescending(p => p.LastUsed)
        .Take(max)
        .Select(p => p.Text)
        .ToList();

    public int Count => _phrases.Count;

    public void Clear() => _phrases.Clear();

    public void Export(PersonalData data) => data.Phrases = _phrases.ToDictionary(kv => kv.Key, kv => kv.Value);

    public void Import(PersonalData data)
    {
        foreach (var (k, v) in data.Phrases) _phrases[k] = v;
    }

    private static string Key(string text) =>
        new string(text.ToLowerInvariant().Where(c => char.IsLetterOrDigit(c) || c == ' ' || c == '\'').ToArray()).Trim();
}
