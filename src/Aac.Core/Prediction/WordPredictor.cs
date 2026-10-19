using System.Text.RegularExpressions;

namespace Aac.Core.Prediction;

/// <summary>
/// Fast, offline word prediction: prefix completion over an English dictionary of ~59,000 words
/// ranked by how common they are, boosted by word pairs from a small conversational corpus and by
/// a personal layer learned from messages the user actually spoke (only when enabled).
/// Runs in well under a millisecond, so it never needs a language model.
/// </summary>
public sealed partial class WordPredictor
{
    /// <summary>Zipf frequency (log10 of occurrences per billion words) per known word.</summary>
    private readonly Dictionary<string, double> _zipf = new(StringComparer.Ordinal);
    private string[] _sortedWords = Array.Empty<string>();
    private string[] _mostCommon = Array.Empty<string>();
    private readonly Dictionary<(string, string), double> _bigrams = new();
    private readonly Dictionary<string, HashSet<string>> _followers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _personalUnigrams = new();
    private readonly Dictionary<(string, string), double> _personalBigrams = new();

    private const double UnknownZipf = 3.0;
    private const double BigramWeight = 2.0;
    private const double PersonalWeight = 3.0;
    private const double PersonalBigramWeight = 8.0;
    private const int PrefixPoolSize = 200;
    private readonly Dictionary<string, string[]> _commonByPrefix = new(StringComparer.Ordinal);

    public static WordPredictor CreateDefault()
    {
        var p = new WordPredictor();
        using (var stream = typeof(WordPredictor).Assembly.GetManifestResourceStream("Aac.Core.Prediction.words-en.txt"))
        {
            if (stream is not null) p.LoadDictionary(new StreamReader(stream));
        }
        foreach (var w in SeedCorpus.CommonWords) p.AddWord(w, UnknownZipf);
        foreach (var line in SeedCorpus.Sentences) p.Train(line, personal: false);
        p.Reindex();
        return p;
    }

    /// <summary>Reads lines of "word&lt;TAB&gt;zipf*100".</summary>
    public void LoadDictionary(TextReader reader)
    {
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var tab = line.IndexOf('\t');
            if (tab <= 0 || !int.TryParse(line.AsSpan(tab + 1), out var z100)) continue;
            AddWord(line[..tab], z100 / 100.0);
        }
        Reindex();
    }

    public int VocabularySize => _zipf.Count;

    private void AddWord(string word, double zipf)
    {
        if (!_zipf.TryGetValue(word, out var existing) || existing < zipf) _zipf[word] = zipf;
    }

    private void Reindex()
    {
        lock (_commonByPrefix) _commonByPrefix.Clear();
        _sortedWords = _zipf.Keys.Concat(_personalUnigrams.Keys).Distinct().OrderBy(w => w, StringComparer.Ordinal).ToArray();
        _mostCommon = _zipf.OrderByDescending(kv => kv.Value).Take(200).Select(kv => kv.Key).ToArray();
    }

    public static IReadOnlyList<string> Tokenize(string text) =>
        WordRegex().Matches(text.ToLowerInvariant()).Select(m => m.Value).ToList();

    /// <summary>Adds a message the user spoke to the personal model.</summary>
    public void Learn(string text)
    {
        var newWord = Tokenize(text).Any(t => !_zipf.ContainsKey(t) && !_personalUnigrams.ContainsKey(t));
        Train(text, personal: true);
        if (newWord) Reindex();
    }

    private void Train(string text, bool personal)
    {
        var bi = personal ? _personalBigrams : _bigrams;
        var prev = "<s>";
        foreach (var t in Tokenize(text))
        {
            if (personal) _personalUnigrams[t] = _personalUnigrams.GetValueOrDefault(t) + 1;
            else AddWord(t, UnknownZipf);
            bi[(prev, t)] = bi.GetValueOrDefault((prev, t)) + 1;
            if (!_followers.TryGetValue(prev, out var set)) _followers[prev] = set = new HashSet<string>(StringComparer.Ordinal);
            set.Add(t);
            prev = t;
        }
    }

    /// <summary>Copies the learned words and word pairs into <paramref name="data"/> for saving.</summary>
    public void ExportPersonal(PersonalData data)
    {
        data.Words = new Dictionary<string, double>(_personalUnigrams);
        data.WordPairs = _personalBigrams.ToDictionary(kv => $"{kv.Key.Item1} {kv.Key.Item2}", kv => kv.Value);
    }

    public void ImportPersonal(PersonalData data)
    {
        foreach (var (w, c) in data.Words) _personalUnigrams[w] = c;
        foreach (var (pair, c) in data.WordPairs)
        {
            var space = pair.IndexOf(' ');
            if (space <= 0) continue;
            var (a, b) = (pair[..space], pair[(space + 1)..]);
            _personalBigrams[(a, b)] = c;
            if (!_followers.TryGetValue(a, out var set)) _followers[a] = set = new HashSet<string>(StringComparer.Ordinal);
            set.Add(b);
        }
        Reindex();
    }

    public bool HasLearned => _personalUnigrams.Count > 0;

    public void ClearPersonal()
    {
        _personalUnigrams.Clear();
        _personalBigrams.Clear();
        Reindex();
    }

    /// <summary>
    /// Predicts words for the text typed so far. If the text ends mid-word, completes that word
    /// (including the word itself when it is already complete, so "four" offers four, fourth, fourteen);
    /// if it ends with a space (or is empty), predicts the next word.
    /// </summary>
    public IReadOnlyList<string> Predict(string textSoFar, int max = 6)
    {
        var (previous, partial) = SplitForPrediction(textSoFar);
        IEnumerable<string> pool = partial.Length > 0
            ? CandidatesWithPrefix(previous, partial)
            : _mostCommon.Concat(_followers.GetValueOrDefault(previous) ?? Enumerable.Empty<string>()).Concat(_personalUnigrams.Keys);

        // Keep only the best few in a small heap: a one-letter prefix can match thousands of words.
        var best = new PriorityQueue<string, (double Score, int NegLength, string Word)>(Comparer<(double Score, int NegLength, string Word)>.Create(
            (a, b) => a.Score != b.Score ? a.Score.CompareTo(b.Score)
                : a.NegLength != b.NegLength ? a.NegLength.CompareTo(b.NegLength)
                : string.CompareOrdinal(b.Word, a.Word)));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var w in pool)
        {
            if (!seen.Add(w)) continue;
            var key = (Score(previous, w), -w.Length, w);
            if (best.Count < max) best.Enqueue(w, key);
            else if (best.TryPeek(out _, out var worst) && best.Comparer.Compare(key, worst) > 0) best.EnqueueDequeue(w, key);
        }
        var result = new List<string>(best.Count);
        while (best.TryDequeue(out var w, out _)) result.Add(w);
        result.Reverse();
        return result;
    }

    private double Score(string previous, string word) =>
        _zipf.GetValueOrDefault(word, UnknownZipf)
        + BigramWeight * Math.Log(1 + _bigrams.GetValueOrDefault((previous, word)))
        + PersonalWeight * Math.Log(1 + _personalUnigrams.GetValueOrDefault(word))
        + PersonalBigramWeight * Math.Log(1 + _personalBigrams.GetValueOrDefault((previous, word)));

    /// <summary>
    /// Words that could rank for this prefix. A short prefix matches thousands of words, but only the
    /// most common ones, or ones boosted by a word pair or by the user's own use, can make the top few.
    /// </summary>
    private IEnumerable<string> CandidatesWithPrefix(string previous, string prefix)
    {
        string[]? common;
        lock (_commonByPrefix) _commonByPrefix.TryGetValue(prefix, out common);
        if (common is null)
        {
            var all = WithPrefix(prefix).ToList();
            common = all.Count <= PrefixPoolSize
                ? all.ToArray()
                : all.OrderByDescending(w => _zipf.GetValueOrDefault(w, UnknownZipf)).Take(PrefixPoolSize).ToArray();
            if (all.Count <= PrefixPoolSize) return common;
            lock (_commonByPrefix) _commonByPrefix[prefix] = common;
        }
        var boosted = (_followers.GetValueOrDefault(previous) ?? Enumerable.Empty<string>())
            .Concat(_personalUnigrams.Keys)
            .Where(w => w.StartsWith(prefix, StringComparison.Ordinal));
        return common.Concat(boosted);
    }

    private IEnumerable<string> WithPrefix(string prefix)
    {
        var i = Array.BinarySearch(_sortedWords, prefix, StringComparer.Ordinal);
        if (i < 0) i = ~i;
        for (; i < _sortedWords.Length && _sortedWords[i].StartsWith(prefix, StringComparison.Ordinal); i++)
            yield return _sortedWords[i];
    }

    internal static (string Previous, string Partial) SplitForPrediction(string text)
    {
        var lower = text.ToLowerInvariant();
        var endsInWord = lower.Length > 0 && (char.IsLetterOrDigit(lower[^1]) || lower[^1] == '\'');
        var tokens = Tokenize(lower);
        if (endsInWord && tokens.Count > 0)
            return (tokens.Count > 1 ? tokens[^2] : "<s>", tokens[^1]);
        if (tokens.Count == 0 || Regex.IsMatch(lower, @"[.!?]\s*$"))
            return ("<s>", "");
        return (tokens[^1], "");
    }

    [GeneratedRegex(@"[a-z0-9]+(?:'[a-z]+)?")]
    private static partial Regex WordRegex();
}
