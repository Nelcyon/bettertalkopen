namespace Aac.Core.Speech;

/// <summary>An utterance waiting for, or being said by, an engine's voice thread, with its own stop signal.</summary>
public sealed class SpeechJob
{
    private readonly CancellationTokenSource _stop = new();

    internal SpeechJob(Utterance utterance) => Utterance = utterance;

    public Utterance Utterance { get; }

    /// <summary>Set once this utterance is stopped. Nothing ever clears it, so a later utterance cannot un-stop this one.</summary>
    public CancellationToken Stopped => _stop.Token;

    public bool IsStopped => _stop.IsCancellationRequested;

    /// <summary>Its end has been reported (by the voice thread when it finished, or by Stop).</summary>
    internal bool Ended { get; set; }

    internal void Stop() => _stop.Cancel();
}

/// <summary>
/// The utterances an engine says one at a time on its own voice thread. Each one has its own stop signal, so
/// Stop (or a new word or quick phrase cutting in) stops exactly what was waiting or being said at that
/// moment, and starting the next utterance can never cancel the stop of the one before. Each utterance's end
/// is reported once: by Stop if it was stopped, otherwise when the voice thread finishes it.
/// </summary>
public sealed class UtteranceQueue
{
    private readonly Queue<SpeechJob> _waiting = new();
    private readonly object _gate = new();
    private SpeechJob? _current;
    private bool _closed;

    /// <summary>Queues an utterance to be said after the ones already waiting; ignored once closed.</summary>
    public void Add(Utterance utterance)
    {
        lock (_gate)
        {
            if (_closed) return;
            _waiting.Enqueue(new SpeechJob(utterance));
            Monitor.PulseAll(_gate);
        }
    }

    /// <summary>For the voice thread: waits for the next utterance and makes it the current one; null once closed.</summary>
    public SpeechJob? Next()
    {
        lock (_gate)
        {
            _current = null;
            while (_waiting.Count == 0 && !_closed) Monitor.Wait(_gate);
            if (_closed) return null;
            return _current = _waiting.Dequeue();
        }
    }

    /// <summary>For the voice thread, when it is done with a job: true if its end still has to be reported (Stop has not already).</summary>
    public bool Finish(SpeechJob job)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_current, job)) _current = null;
            if (job.Ended) return false;
            job.Ended = true;
            return true;
        }
    }

    /// <summary>Stops the utterance being said and drops the waiting ones; returns each of them, to report as ended (cancelled).</summary>
    public IReadOnlyList<Utterance> StopAll()
    {
        var stopped = new List<Utterance>();
        lock (_gate)
        {
            if (_current is { Ended: false } current)
            {
                current.Ended = true;
                current.Stop();
                stopped.Add(current.Utterance);
            }
            while (_waiting.TryDequeue(out var job))
            {
                job.Ended = true;
                job.Stop();
                stopped.Add(job.Utterance);
            }
        }
        return stopped;
    }

    /// <summary>Stops everything and lets the voice thread leave (Next returns null).</summary>
    public void Close()
    {
        lock (_gate)
        {
            _closed = true;
            _current?.Stop();
            foreach (var job in _waiting) job.Stop();
            _waiting.Clear();
            Monitor.PulseAll(_gate);
        }
    }
}
