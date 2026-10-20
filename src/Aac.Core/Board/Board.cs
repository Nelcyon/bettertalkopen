namespace Aac.Core.Board;

public enum CellKind
{
    Command,
    Letter,
    Prediction,
    Candidate,
    QuickPhrase,
    Category,
    Phrase,
    /// <summary>An urgent phrase from the always-visible strip; behaves like a quick phrase.</summary>
    Pinned,
    /// <summary>A menu button (page switch, sentences, stop, hide); runs its command, drawn apart from the letter keys.</summary>
    Menu,
    /// <summary>A word in the strip that replaces the urgent phrases while Itch is open: said the moment it is pressed.</summary>
    Spoken,
    /// <summary>A button that moves the Itch figure (turn, zoom): a tool, drawn in blue so it never looks like words to say.</summary>
    Tool,
}

public enum CommandId
{
    None,
    GoQuick,
    GoKeyboard,
    GoPhrases,
    MakeSentences,
    Speak,
    StopVoice,
    Space,
    DeleteLetter,
    DeleteWord,
    Clear,
    Undo,
    ForgetLearning,
    HideWindow,
    ClosePanel,
    AddPhrase,
    Itch,
    ItchTurnLeft,
    ItchTurnRight,
    ItchFront,
    ItchBack,
    ItchZoomIn,
    ItchZoomOut,
    ItchCentre,
    /// <summary>Folds the controls panel beside the figure away, or brings it back (the controller icon over the figure).</summary>
    ItchControls,
    /// <summary>Look at the figure from above (down on it) or from below, one step at a time.</summary>
    ItchTiltAbove,
    ItchTiltBelow,
    /// <summary>Slide the figure up, down, left or right across the view, one step at a time.</summary>
    ItchPanUp,
    ItchPanDown,
    ItchPanLeft,
    ItchPanRight,
    OpenSettings,
}

/// <summary>
/// One on-screen target. Immutable; the board is rebuilt when content changes.
/// <see cref="Label"/> is the full name (screen readers, tooltips). When <see cref="Icon"/> is set the
/// button shows the icon large with <see cref="Caption"/> (or the label) small beneath it.
/// <see cref="Span"/> is the cell's width in key units and <see cref="Gap"/> empty units before it
/// (the stagger of a real keyboard's rows). <see cref="IsOn"/> marks a toggle that is currently on
/// (the open drawer's button, the selected phrase category). <see cref="Tone"/> names the colour family of a
/// button that belongs to a group (Turn, Tilt, Slide, Zoom), so each kind of movement looks different.
/// </summary>
public sealed record Cell(string Id, string Label, CellKind Kind, CommandId Command = CommandId.None, string? Payload = null, string? Hint = null, bool IsEnabled = true,
    string? Icon = null, string? Caption = null, double Span = 1, KeyPart Part = KeyPart.Whole, double Gap = 0, bool IsOn = false, string? Tone = null)
{
    public string? ShownCaption => Icon is null ? null : Caption ?? Label;
}

/// <summary>Neighbouring cells drawn as one split key (shared outline): each half is still its own target.</summary>
public enum KeyPart
{
    Whole,
    Start,
    End,
}

public sealed record BoardRow(string Name, IReadOnlyList<Cell> Cells);

/// <summary>
/// Moves a single focus cursor over rows of different lengths. Rows are drawn full-width, so moving
/// up/down keeps the same horizontal position proportionally — what you see is where you land.
/// Edges stop rather than wrap unless wrapping is turned on.
/// </summary>
/// <summary>Where a row's cells sit across the board: each cell's start and width, and the row's total width.</summary>
public sealed record RowGeometry(IReadOnlyList<(double Start, double Width)> Cells, double Total)
{
    public static RowGeometry Equal(int count) =>
        new(Enumerable.Range(0, count).Select(i => ((double)i, 1.0)).ToList(), count);

    public static RowGeometry FromSpans(IReadOnlyList<double> spans) => FromCells(spans.Select(w => (0.0, w)).ToList(), null);

    /// <summary>From each cell's (gap before, width); <paramref name="total"/> defaults to where the last cell ends.</summary>
    public static RowGeometry FromCells(IReadOnlyList<(double Gap, double Width)> cells, double? total)
    {
        var list = new List<(double, double)>();
        var x = 0.0;
        foreach (var (gap, width) in cells)
        {
            x += gap;
            list.Add((x, width));
            x += width;
        }
        return new RowGeometry(list, Math.Max(total ?? x, x));
    }
}

/// <summary>
/// Moves a single focus cursor over rows of different lengths. Rows are drawn full-width, so moving
/// up/down lands on the cell drawn under the centre of the current one: what you see is where you land.
/// Edges stop rather than wrap unless wrapping is turned on.
/// </summary>
public static class GridNavigator
{
    public static (int Row, int Col) Move(IReadOnlyList<int> rows, int row, int col, int dRow, int dCol, bool wrap) =>
        Move(rows.Select(RowGeometry.Equal).ToList(), row, col, dRow, dCol, wrap);

    public static (int Row, int Col) Move(IReadOnlyList<IReadOnlyList<double>> rows, int row, int col, int dRow, int dCol, bool wrap) =>
        Move(rows.Select(RowGeometry.FromSpans).ToList(), row, col, dRow, dCol, wrap);

    public static (int Row, int Col) Move(IReadOnlyList<RowGeometry> rows, int row, int col, int dRow, int dCol, bool wrap)
    {
        if (rows.Count == 0) return (0, 0);
        row = Math.Clamp(row, 0, rows.Count - 1);
        col = Math.Clamp(col, 0, Math.Max(0, rows[row].Cells.Count - 1));

        if (dRow != 0)
        {
            var target = row + dRow;
            if (target < 0 || target >= rows.Count)
            {
                if (!wrap) return (row, col);
                target = (target + rows.Count) % rows.Count;
            }
            return (target, MapColumn(col, rows[row], rows[target]));
        }

        if (dCol != 0)
        {
            var n = rows[row].Cells.Count;
            var target = col + dCol;
            if (target < 0 || target >= n)
            {
                if (!wrap) return (row, col);
                target = (target + n) % n;
            }
            return (row, target);
        }
        return (row, col);
    }

    public static int MapColumn(int col, int fromCount, int toCount) =>
        MapColumn(col, RowGeometry.Equal(Math.Max(0, fromCount)), RowGeometry.Equal(Math.Max(0, toCount)));

    public static int MapColumn(int col, IReadOnlyList<double> from, IReadOnlyList<double> to) =>
        MapColumn(col, RowGeometry.FromSpans(from), RowGeometry.FromSpans(to));

    /// <summary>The cell in the target row under the horizontal centre of the current cell (the nearest one if that spot is a gap).</summary>
    public static int MapColumn(int col, RowGeometry from, RowGeometry to)
    {
        if (to.Cells.Count == 0 || from.Cells.Count == 0) return 0;
        col = Math.Clamp(col, 0, from.Cells.Count - 1);
        var (start, width) = from.Cells[col];
        var x = (start + width / 2) / from.Total * to.Total;
        var best = 0;
        var bestDistance = double.MaxValue;
        for (var i = 0; i < to.Cells.Count; i++)
        {
            var (s, w) = to.Cells[i];
            var distance = x < s ? s - x : x >= s + w ? x - (s + w) + 1e-9 : 0; // a cell's right edge belongs to the next cell
            if (distance < bestDistance)
            {
                best = i;
                bestDistance = distance;
            }
        }
        return best;
    }
}
