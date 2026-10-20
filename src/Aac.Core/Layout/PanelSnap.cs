namespace Aac.Core.Layout;

/// <summary>
/// Snapping for a panel being dragged in move mode: within <c>threshold</c> pixels an edge of the panel jumps to
/// an edge of another panel (beside it, with <c>gap</c> between them, or lined up with it) or of the screen. Each
/// axis snaps on its own, so a panel held against another one slides along that edge as the mouse moves.
/// </summary>
public static class PanelSnap
{
    public static PxRect Snap(PxRect moving, IEnumerable<PxRect> others, PxRect work, int threshold, int gap)
    {
        var list = others.ToList();
        var w = moving.Width;
        var h = moving.Height;

        // Left/right: against panels that are level with this one (overlapping it top to bottom, give or take the threshold).
        var dx = Best(
            list.Where(r => moving.Top < r.Bottom + threshold && moving.Bottom > r.Top - threshold)
                .SelectMany(r => new[]
                {
                    r.Right + gap - moving.Left,   // just right of it
                    r.Left - gap - moving.Right,   // just left of it
                    r.Left - moving.Left,          // lined up on the left
                    r.Right - moving.Right,        // lined up on the right
                })
                .Append(work.Left - moving.Left)
                .Append(work.Right - moving.Right),
            threshold);
        // Up/down: against panels above or below it (overlapping it side to side).
        var left = moving.Left + dx;
        var dy = Best(
            list.Where(r => left < r.Right + threshold && left + w > r.Left - threshold)
                .SelectMany(r => new[]
                {
                    r.Bottom + gap - moving.Top,
                    r.Top - gap - moving.Bottom,
                    r.Top - moving.Top,
                    r.Bottom - moving.Bottom,
                })
                .Append(work.Top - moving.Top)
                .Append(work.Bottom - moving.Bottom),
            threshold);

        // Never off the screen.
        var x = Math.Clamp(left, work.Left, Math.Max(work.Left, work.Right - w));
        var y = Math.Clamp(moving.Top + dy, work.Top, Math.Max(work.Top, work.Bottom - h));
        return new PxRect(x, y, x + w, y + h);
    }

    /// <summary>The smallest move within the threshold, or no move.</summary>
    private static int Best(IEnumerable<int> deltas, int threshold)
    {
        var best = 0;
        var found = false;
        foreach (var d in deltas)
        {
            if (Math.Abs(d) > threshold) continue;
            if (!found || Math.Abs(d) < Math.Abs(best)) { best = d; found = true; }
        }
        return best;
    }
}
