namespace Aac.Core.Layout;

/// <summary>The corner of the screen the window sits in. Everything that opens grows away from it, so the keyboard never moves.</summary>
public enum WindowCorner
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
}

/// <summary>A rectangle in screen pixels; <see cref="Right"/> and <see cref="Bottom"/> are one past the last pixel.</summary>
public readonly record struct PxRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
    public bool Contains(int x, int y) => x >= Left && x < Right && y >= Top && y < Bottom;
}

/// <summary>Where an anchored window goes, and when the pointer is in its corner. Pure arithmetic, so it is tested.</summary>
public static class Anchoring
{
    public static bool IsLeft(WindowCorner corner) => corner is WindowCorner.TopLeft or WindowCorner.BottomLeft;
    public static bool IsTop(WindowCorner corner) => corner is WindowCorner.TopLeft or WindowCorner.TopRight;

    /// <summary>The top-left pixel of a window of this size sitting flush in the corner of the work area.</summary>
    public static (int X, int Y) Place(WindowCorner corner, PxRect work, int width, int height) =>
        (IsLeft(corner) ? work.Left : work.Right - width, IsTop(corner) ? work.Top : work.Bottom - height);

    /// <summary>
    /// True when the pointer is within <paramref name="size"/> pixels of the screen's corner (so a corner size
    /// of 3 is a 4-pixel square, as the top-left corner always was). Clamped: a pointer pushed into the corner
    /// of a screen that is the edge of the desktop stops exactly on it.
    /// </summary>
    public static bool InCorner(WindowCorner corner, PxRect screen, int size, int x, int y)
    {
        if (!screen.Contains(x, y)) return false;
        var dx = IsLeft(corner) ? x - screen.Left : screen.Right - 1 - x;
        var dy = IsTop(corner) ? y - screen.Top : screen.Bottom - 1 - y;
        return dx <= size && dy <= size;
    }
}
