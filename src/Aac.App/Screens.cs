using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Aac.Core.Layout;
using Aac.Core.Storage;
using Forms = System.Windows.Forms;

namespace Aac.App;

/// <summary>One screen (monitor) as Windows reports it, in screen pixels.</summary>
public sealed record ScreenInfo(string DeviceName, PxRect Bounds, PxRect WorkArea, bool IsMain)
{
    /// <summary>Plain words for a list: "Main screen (1920 × 1080)" or "Screen 2 (2560 × 1440, to the right of the main screen)".</summary>
    public string Describe(ScreenInfo main)
    {
        var size = $"{Bounds.Width} × {Bounds.Height}";
        if (IsMain) return $"Main screen ({size})";
        var where = Bounds.Left >= main.Bounds.Right ? ", to the right of the main screen"
            : Bounds.Right <= main.Bounds.Left ? ", to the left of the main screen"
            : Bounds.Bottom <= main.Bounds.Top ? ", above the main screen"
            : Bounds.Top >= main.Bounds.Bottom ? ", below the main screen"
            : "";
        return $"Screen {Number} ({size}{where})";
    }

    /// <summary>The number in the device name (\\.\DISPLAY2 → 2), as a label; 0 when the name has none.</summary>
    public int Number => int.TryParse(new string(DeviceName.Reverse().TakeWhile(char.IsDigit).Reverse().ToArray()), out var n) ? n : 0;
}

/// <summary>The screens, in screen pixels (the process is per-monitor DPI aware, so Windows reports real pixels).</summary>
public static class Screens
{
    public static IReadOnlyList<ScreenInfo> All() =>
        Forms.Screen.AllScreens.Select(ToInfo).OrderByDescending(s => s.IsMain).ThenBy(s => s.Bounds.Left).ThenBy(s => s.Bounds.Top).ToList();

    public static ScreenInfo MainScreen() => ToInfo(Forms.Screen.PrimaryScreen ?? Forms.Screen.AllScreens[0]);

    /// <summary>The screen with this device name, or the main screen when it is unplugged or not set.</summary>
    public static ScreenInfo Choose(string? deviceName) =>
        (deviceName is { Length: > 0 } ? Forms.Screen.AllScreens.FirstOrDefault(s => string.Equals(s.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase)) : null) is { } s
            ? ToInfo(s) : MainScreen();

    /// <summary>The screen under this point (screen pixels), or the nearest one.</summary>
    public static ScreenInfo At(int x, int y) => ToInfo(Forms.Screen.FromPoint(new System.Drawing.Point(x, y)));

    private static ScreenInfo ToInfo(Forms.Screen s) => new(s.DeviceName, Rect(s.Bounds), Rect(s.WorkingArea), s.Primary);

    private static PxRect Rect(System.Drawing.Rectangle r) => new(r.Left, r.Top, r.Right, r.Bottom);
}

/// <summary>
/// The corner of the chosen screen that opens the app. The mouse hook asks on every mouse event, so the
/// screen's rectangle is kept ready and only looked up again when the screens change.
/// </summary>
public sealed class CornerSpot : IDisposable
{
    private readonly WindowCorner _corner;
    private readonly int _size;
    private readonly string? _screenName;
    private ScreenInfo _screen;
    private readonly EventHandler _changed;

    public CornerSpot(AppSettings settings)
    {
        _corner = settings.Anchor;
        _size = Math.Clamp(settings.CornerPixels, 0, 200);
        _screenName = settings.Screen;
        _screen = Screens.Choose(_screenName);
        _changed = (_, _) => _screen = Screens.Choose(_screenName);
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += _changed;
    }

    public bool Contains(int x, int y) => Anchoring.InCorner(_corner, _screen.Bounds, _size, x, y);

    public void Dispose() => Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= _changed;
}

/// <summary>Moves and sizes a window in screen pixels in one step (so an anchored window never flickers across the screen).</summary>
public static class WindowPlacement
{
    private const uint NoSize = 0x0001, NoMove = 0x0002, NoZOrder = 0x0004, NoActivate = 0x0010;

    public static IntPtr Handle(Window w) => new WindowInteropHelper(w).Handle;

    /// <summary>The window's rectangle in screen pixels, or null before it has a Windows handle.</summary>
    public static PxRect? Rect(Window w)
    {
        var hwnd = Handle(w);
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var r)) return null;
        return new PxRect(r.Left, r.Top, r.Right, r.Bottom);
    }

    /// <summary>Screen pixels per WPF unit for the screen the window is on (1.0 at 100 %).</summary>
    public static double Scale(Window w) => VisualTreeHelper.GetDpi(w).DpiScaleX;

    public static void Move(Window w, int x, int y) =>
        SetWindowPos(Handle(w), IntPtr.Zero, x, y, 0, 0, NoSize | NoZOrder | NoActivate);

    public static void MoveAndSize(Window w, int x, int y, int width, int height) =>
        SetWindowPos(Handle(w), IntPtr.Zero, x, y, width, height, NoZOrder | NoActivate);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left; public int Top; public int Right; public int Bottom; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
}

/// <summary>Starts BetterTalk when this Windows account signs in: a Run entry in the registry, kept in step with the setting and the install folder.</summary>
public static class StartupEntry
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Name = "BetterTalk";

    /// <summary>Adds or removes the entry; a problem is logged and never stops the app.</summary>
    public static void Apply(bool on)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (key is null) return;
            if (on && Environment.ProcessPath is { } exe) key.SetValue(Name, $"\"{exe}\"");
            else if (!on && key.GetValue(Name) is not null) key.DeleteValue(Name, throwOnMissingValue: false);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or IOException or UnauthorizedAccessException)
        {
            App.LogError(ex);
        }
    }
}
