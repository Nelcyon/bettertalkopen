using Aac.Core.Speech;

namespace Aac.Core.Tests;

public class UtteranceQueueTests
{
    private static Utterance U(long id, string text = "hello") => new(id, text, text, UtteranceKind.Message);

    [Fact]
    public void A_new_utterance_after_stop_does_not_un_stop_the_one_that_was_playing()
    {
        // The Reddit report: one shared cancel flag was set by Stop and cleared again by the next Speak, so the
        // voice thread could miss the stop and keep saying the old message instead of the new one.
        var q = new UtteranceQueue();
        q.Add(U(1, "a long message"));
        var playing = q.Next()!;

        var stopped = q.StopAll();
        q.Add(U(2, "Yes."));

        Assert.True(playing.IsStopped);
        Assert.True(playing.Stopped.IsCancellationRequested);
        Assert.Equal(1, Assert.Single(stopped).Id);
        Assert.False(q.Finish(playing)); // its end was already reported by the stop
        var next = q.Next()!;
        Assert.Equal(2, next.Utterance.Id);
        Assert.False(next.IsStopped);
    }

    [Fact]
    public void Stop_also_drops_what_was_waiting_and_reports_each_end_once()
    {
        var q = new UtteranceQueue();
        q.Add(U(1));
        q.Add(U(2));
        q.Add(U(3));
        var playing = q.Next()!;

        Assert.Equal(new long[] { 1, 2, 3 }, q.StopAll().Select(u => u.Id));
        Assert.Empty(q.StopAll()); // a second Stop reports nothing again
        Assert.False(q.Finish(playing));
    }

    [Fact]
    public void An_utterance_that_finishes_on_its_own_reports_its_end_once_and_a_later_stop_leaves_it_alone()
    {
        var q = new UtteranceQueue();
        q.Add(U(1));
        var job = q.Next()!;
        Assert.True(q.Finish(job));
        Assert.Empty(q.StopAll());
        Assert.False(job.IsStopped);
    }

    [Fact]
    public void Stopping_one_utterance_never_touches_the_signal_of_the_next()
    {
        var q = new UtteranceQueue();
        q.Add(U(1));
        var first = q.Next()!;
        q.StopAll();
        q.Finish(first);
        q.Add(U(2));
        var second = q.Next()!;
        Assert.NotEqual(first.Stopped, second.Stopped);
        Assert.False(second.Stopped.IsCancellationRequested);
        Assert.True(q.Finish(second));
    }

    [Fact]
    public void The_voice_thread_waits_for_work_and_leaves_when_closed()
    {
        var q = new UtteranceQueue();
        var said = new List<long>();
        var thread = new Thread(() =>
        {
            while (q.Next() is { } job)
            {
                lock (said) said.Add(job.Utterance.Id);
                q.Finish(job);
            }
        });
        thread.Start();
        q.Add(U(1));
        q.Add(U(2));
        SpinWait.SpinUntil(() => { lock (said) return said.Count == 2; }, TimeSpan.FromSeconds(5));
        q.Close();
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
        Assert.Equal(new long[] { 1, 2 }, said);
        q.Add(U(3)); // ignored once closed
        Assert.Null(q.Next());
    }
}
