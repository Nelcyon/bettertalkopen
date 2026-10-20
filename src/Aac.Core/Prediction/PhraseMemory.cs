using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Aac.Core.Prediction;

/// <summary>What the app has learned from how the user communicates. Saved encrypted on this PC only.</summary>
public sealed class PersonalData
{
    public Dictionary<string, double> Words { get; set; } = new();
    /// <summary>Key is "previous next". Only pairs said at least twice are saved.</summary>
    public Dictionary<string, double> WordPairs { get; set; } = new();
    /// <summary>
    /// Messages, keyed by a one-way fingerprint. A message said only once is kept as its fingerprint alone (no
    /// text); the text is kept once it has been said <see cref="PhraseMemory.MinUses"/> times.
    /// </summary>
    public Dictionary<string, PhraseUse> Phrases { get; set; } = new();
    /// <summary>Random, private to this PC: makes the fingerprints impossible to guess from outside.</summary>
    public string? Salt { get; set; }
}

public sealed class PhraseUse
{
    /// <summary>The message as last said; empty until it has been said <see cref="PhraseMemory.MinUses"/> times.</summary>
    public string Text { get; set; } = "";
    public int Count { get; set; }
    /// <summary>The day it was last said (the time of day is not kept).</summary>
    public DateTimeOffset LastUsed { get; set; }
}

/// <summary>
/// Remembers the messages the user actually says so that their own frequent sentences become
/// one-selection phrases. Never reorders the fixed quick responses (their positions stay predictable).
/// It is not a history: a message said once is kept only as a one-way fingerprint, so nothing the user said
/// once can be read back out of the file; messages that look private are not kept at all; nothing is kept
/// longer than a year unused; and only the day is stored, never the time.
/// </summary>
public sealed partial class PhraseMemory
{
    public const int MaxPhrases = 500;
    public const int MaxLength = 200;
    /// <summary>A message must be said this many times before it is offered as a frequent phrase (and its text is kept).</summary>
    public const int MinUses = 2;
    /// <summary>Anything not said again for this long is forgotten.</summary>
    public const int MaxAgeDays = 365;

    private readonly Dictionary<string, PhraseUse> _phrases = new(StringComparer.Ordinal);
    private string _salt = NewSalt();

    private static string NewSalt() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));

    /// <summary>
    /// True for text that looks like a password, a card or phone number, an address or the like. Such messages are
    /// not learned from at all. (A rough guard: it cannot know what is private, only what usually is.)
    /// </summary>
    public static bool LooksPrivate(string text) => PrivateLook().IsMatch(text);

    [GeneratedRegex(@"\d{4,}|@|\b(password|passcode|pin|ssn|cvv|social security|credit card|card number|account number)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PrivateLook();

    public void Record(string text, DateTimeOffset now)
    {
        var display = text.Trim();
        if (display.Length == 0 || display.Length > MaxLength || LooksPrivate(display)) return;
        var key = Key(display);
        if (!_phrases.TryGetValue(key, out var use)) _phrases[key] = use = new PhraseUse();
        use.Count++;
        use.Text = use.Count >= MinUses ? display : "";
        use.LastUsed = Day(now);
        Forget(now);
        if (_phrases.Count > MaxPhrases)
        {
            var weakest = _phrases.OrderBy(kv => kv.Value.Count).ThenBy(kv => kv.Value.LastUsed).First().Key;
            _phrases.Remove(weakest);
        }
    }

    /// <summary>Drops what has not been said again for a year.</summary>
    private void Forget(DateTimeOffset now)
    {
        var oldest = Day(now).AddDays(-MaxAgeDays);
        foreach (var key in _phrases.Where(kv => kv.Value.LastUsed < oldest).Select(kv => kv.Key).ToList()) _phrases.Remove(key);
    }

    private static DateTimeOffset Day(DateTimeOffset t) => new(t.UtcDateTime.Date, TimeSpan.Zero);

    /// <summary>Most-used first; ties go to the most recent.</summary>
    public IReadOnlyList<string> Frequent(int max) => _phrases.Values
        .Where(p => p.Count >= MinUses && p.Text.Length > 0)
        .OrderByDescending(p => p.Count)
        .ThenByDescending(p => p.LastUsed)
        .Take(max)
        .Select(p => p.Text)
        .ToList();

    public int Count => _phrases.Count;

    public void Clear() { _phrases.Clear(); _salt = NewSalt(); }

    public void Export(PersonalData data)
    {
        data.Salt = _salt;
        data.Phrases = _phrases.ToDictionary(kv => kv.Key, kv => kv.Value);
    }

    public void Import(PersonalData data)
    {
        if (!string.IsNullOrEmpty(data.Salt)) _salt = data.Salt;
        foreach (var (k, v) in data.Phrases)
        {
            // Files from older versions kept every message as readable text under a readable key: keep only what
            // has been said repeatedly (and does not look private), and turn the rest into fingerprints.
            if (v.Text.Length == 0) { _phrases[k] = v; continue; }
            if (LooksPrivate(v.Text)) continue;
            _phrases[Key(v.Text)] = new PhraseUse { Text = v.Count >= MinUses ? v.Text : "", Count = v.Count, LastUsed = Day(v.LastUsed) };
        }
    }

    /// <summary>A one-way fingerprint of the message (ignoring case and punctuation), salted with this PC's secret.</summary>
    private string Key(string text)
    {
        var plain = new string(text.ToLowerInvariant().Where(c => char.IsLetterOrDigit(c) || c == ' ' || c == '\'').ToArray()).Trim();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(_salt + "\n" + plain)))[..32];
    }
}
