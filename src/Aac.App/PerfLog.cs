using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;

namespace Aac.App;

/// <summary>
/// Optional timing log for finding lag. Off unless the environment variable BETTERTALK_PERF=1 is set
/// when the app starts; then a summary per measurement is rewritten to perf.log every two seconds.
/// Only timings are recorded, never message text.
/// </summary>
public static class PerfLog
{
    public static readonly bool Enabled = Environment.GetEnvironmentVariable("BETTERTALK_PERF") == "1";
    private static readonly ConcurrentDictionary<string, ConcurrentQueue<double>> Samples = new();
    private static Timer? _flush;

    public static void Record(string name, double ms)
    {
        if (!Enabled) return;
        Samples.GetOrAdd(name, _ => new ConcurrentQueue<double>()).Enqueue(ms);
        _flush ??= new Timer(_ => Write(), null, 2000, 2000);
    }

    public static void Record(string name, Stopwatch sw) => Record(name, sw.Elapsed.TotalMilliseconds);

    /// <summary>Keeps only the latest value (e.g. the message length, to count deletions in a test).</summary>
    public static void Set(string name, double value)
    {
        if (!Enabled) return;
        var q = new ConcurrentQueue<double>();
        q.Enqueue(value);
        Samples[name] = q;
        _flush ??= new Timer(_ => Write(), null, 2000, 2000);
    }

    private static void Write()
    {
        try
        {
            var lines = Samples.OrderBy(kv => kv.Key).Select(kv =>
            {
                var v = kv.Value.ToArray().OrderBy(x => x).ToArray();
                if (v.Length == 0) return $"{kv.Key}: none";
                double P(double q) => v[Math.Min(v.Length - 1, (int)(q * v.Length))];
                return $"{kv.Key}: n={v.Length} avg={v.Average():F1}ms p50={P(0.5):F1} p95={P(0.95):F1} max={v[^1]:F1}";
            });
            Directory.CreateDirectory(AppPaths.Root);
            File.WriteAllLines(Path.Combine(AppPaths.Root, "perf.log"), lines);
        }
        catch (IOException) { }
    }

    public static void Reset() => Samples.Clear();
}
