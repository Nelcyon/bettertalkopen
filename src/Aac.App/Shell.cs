using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace Aac.App;

/// <summary>
/// The icon in the notification area (taskbar tray). The app lives here while hidden; its menu
/// shows the window, opens settings, or exits.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Drawing.Icon _image;

    private readonly Forms.ToolStripMenuItem _editItem;

    public TrayIcon(Action show, Action hide, Action openSettings, Action openSettingsFile, Action exit, Action<bool> setEditMode)
    {
        _image = DrawIcon();
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Show talk window", null, (_, _) => show());
        menu.Items.Add("Hide talk window", null, (_, _) => hide());
        menu.Items.Add(new Forms.ToolStripSeparator());
        _editItem = new Forms.ToolStripMenuItem("Edit buttons (change, add or delete words)") { CheckOnClick = true };
        _editItem.CheckedChanged += (_, _) => setEditMode(_editItem.Checked);
        menu.Items.Add(_editItem);
        menu.Items.Add("Settings…", null, (_, _) => openSettings());
        menu.Items.Add("Open settings file", null, (_, _) => openSettingsFile());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit BetterTalk", null, (_, _) => exit());
        _icon = new Forms.NotifyIcon
        {
            Icon = _image,
            Text = "BetterTalk",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) show(); };
    }

    /// <summary>Keeps the menu's tick in step when edit mode is switched on or off elsewhere.</summary>
    public bool EditModeChecked { set { if (_editItem.Checked != value) _editItem.Checked = value; } }

    /// <summary>A speech bubble drawn at runtime, so no image file has to ship with the app.</summary>
    private static Drawing.Icon DrawIcon()
    {
        using var bmp = new Drawing.Bitmap(32, 32);
        using (var g = Drawing.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Drawing.Color.Transparent);
            using var fill = new Drawing.SolidBrush(Drawing.Color.FromArgb(255, 212, 0));
            g.FillEllipse(fill, 1, 2, 30, 22);
            g.FillPolygon(fill, new[] { new Drawing.Point(7, 19), new Drawing.Point(4, 30), new Drawing.Point(15, 22) });
            using var dot = new Drawing.SolidBrush(Drawing.Color.Black);
            g.FillEllipse(dot, 8, 11, 4, 4);
            g.FillEllipse(dot, 14, 11, 4, 4);
            g.FillEllipse(dot, 20, 11, 4, 4);
        }
        var handle = bmp.GetHicon();
        try { return (Drawing.Icon)Drawing.Icon.FromHandle(handle).Clone(); }
        finally { DestroyIcon(handle); }
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _image.Dispose();
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);
}

/// <summary>
/// Opens the app from the top-left corner of the main screen. In click mode a left click released with
/// the pointer in the corner fires (a low-level mouse hook, so even very fast clicks count; the click
/// still reaches whatever is under it). The hook runs on its own background thread with its own message
/// loop: Windows waits for a low-level hook before moving the cursor, so it must never wait on the
/// app's UI thread. In rest mode the pointer has to stay in the corner for the dwell time.
/// </summary>
public sealed class HotCorner : IDisposable
{
    private const int CornerPixels = 3;
    private const int WhMouseLl = 14, WmLButtonUp = 0x0202, WmQuit = 0x0012;
    private readonly Action _onTrigger;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly int _dwellMs;
    private readonly DispatcherTimer? _timer;
    private readonly Thread? _hookThread;
    private LowLevelMouseProc? _proc; // kept alive while the hook is installed
    private uint _hookThreadId;
    private DateTime? _enteredAt;
    private bool _fired;

    public HotCorner(Aac.Core.Storage.CornerTrigger trigger, int dwellMs, Action onTrigger)
    {
        _onTrigger = onTrigger;
        _dwellMs = Math.Max(0, dwellMs);
        if (trigger == Aac.Core.Storage.CornerTrigger.Click)
        {
            using var ready = new ManualResetEventSlim();
            _hookThread = new Thread(() => HookThread(ready)) { IsBackground = true, Name = "Corner mouse hook" };
            _hookThread.Start();
            ready.Wait(TimeSpan.FromSeconds(2));
        }
        else
        {
            _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Input, (_, _) => Tick(), _dispatcher);
            _timer.Start();
        }
    }

    private void HookThread(ManualResetEventSlim ready)
    {
        _hookThreadId = GetCurrentThreadId();
        _proc = HookProc;
        var hook = SetWindowsHookEx(WhMouseLl, _proc, GetModuleHandle(null), 0);
        ready.Set();
        if (hook == IntPtr.Zero) return;
        try
        {
            while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
        }
        finally { UnhookWindowsHookEx(hook); }
    }

    private static bool InCorner(int x, int y) => x <= CornerPixels && y <= CornerPixels;

    private IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam)
    {
        // Runs for every mouse event on the PC: only a cheap check, never blocking work. An error here must
        // never take the app (or the mouse) down, so it is logged and the event passed on.
        try
        {
            if (code >= 0)
            {
                var info = Marshal.PtrToStructure<MouseHookInfo>(lParam);
                if (PerfLog.Enabled) PerfLog.Record("mouse hook delay", unchecked((uint)Environment.TickCount - info.Time));
                if (wParam == WmLButtonUp && InCorner(info.X, info.Y)) _dispatcher.BeginInvoke(_onTrigger);
            }
        }
        catch (Exception ex)
        {
            App.LogError(ex);
            App.LogLifecycle($"corner hook error: {ex.GetType().Name}");
        }
        return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
    }

    private void Tick()
    {
        if (!GetCursorPos(out var p)) return;
        if (!InCorner(p.X, p.Y))
        {
            _enteredAt = null;
            _fired = false;
            return;
        }
        _enteredAt ??= DateTime.UtcNow;
        if (!_fired && (DateTime.UtcNow - _enteredAt.Value).TotalMilliseconds >= _dwellMs)
        {
            _fired = true; // once per visit to the corner
            _onTrigger();
        }
    }

    public void Dispose()
    {
        _timer?.Stop();
        if (_hookThread is not null && _hookThreadId != 0) PostThreadMessage(_hookThreadId, WmQuit, IntPtr.Zero, IntPtr.Zero);
    }

    private delegate IntPtr LowLevelMouseProc(int code, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseHookInfo { public int X; public int Y; public uint MouseData; public uint Flags; public uint Time; public IntPtr ExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg { public IntPtr Hwnd; public uint Message; public IntPtr WParam; public IntPtr LParam; public uint Time; public Point Pt; }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc proc, IntPtr module, uint threadId);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out Msg msg, IntPtr hwnd, uint min, uint max);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref Msg msg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref Msg msg);

    [DllImport("user32.dll")]
    private static extern bool PostThreadMessage(uint threadId, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? name);
}

/// <summary>Remembers the window size between runs (position is always the top-left corner).</summary>
public static class WindowSizeStore
{
    private sealed record Size(double Width, double Height);

    public static (double Width, double Height)? Load()
    {
        try
        {
            if (!File.Exists(AppPaths.WindowSize)) return null;
            var s = JsonSerializer.Deserialize<Size>(File.ReadAllText(AppPaths.WindowSize));
            return s is null || s.Width < 200 || s.Height < 150 ? null : (s.Width, s.Height);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }

    public static void Save(double width, double height)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.Root);
            File.WriteAllText(AppPaths.WindowSize, JsonSerializer.Serialize(new Size(width, height)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
