using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace Aac.App;

/// <summary>
/// The icon in the notification area (taskbar tray). The app lives here while hidden; its right-click menu
/// shows or hides the window, opens Settings, switches on button editing, opens the help page, or exits.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Drawing.Icon _image;

    private readonly Forms.ToolStripMenuItem _editItem;
    private readonly Forms.ToolStripMenuItem _arrangeItem;

    public TrayIcon(Action show, Action hide, Action openSettings, Action openSettingsFile, Action exit, Action<bool> setEditMode,
        Action<bool> setArranging, Action putPanelsBack, Action setupGuide)
    {
        _image = DrawIcon();
        var menu = new Forms.ContextMenuStrip { ShowItemToolTips = true };
        menu.Items.Add(new Forms.ToolStripMenuItem("Show BetterTalk", null, (_, _) => show())
            { ToolTipText = "Opens the BetterTalk window (you can also left-click this icon)" });
        menu.Items.Add(new Forms.ToolStripMenuItem("Hide BetterTalk", null, (_, _) => hide())
            { ToolTipText = "Tucks the window away. BetterTalk keeps running and waits here" });
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(new Forms.ToolStripMenuItem("Settings…", null, (_, _) => openSettings())
            { Font = new Drawing.Font(menu.Font, Drawing.FontStyle.Bold), ToolTipText = "Voice, speed, window and button options" });
        _editItem = new Forms.ToolStripMenuItem("Change the words on the buttons") { CheckOnClick = true,
            ToolTipText = "Tick this, then tap any button on the screen to rename, add or delete it. Untick it when you are done" };
        _editItem.CheckedChanged += (_, _) => setEditMode(_editItem.Checked);
        menu.Items.Add(_editItem);
        _arrangeItem = new Forms.ToolStripMenuItem("Unlock UI to move the panels") { CheckOnClick = true,
            ToolTipText = "Tick this, then drag the outlined Itch panels anywhere; they snap to each other's edges. Press Lock UI (or untick this) when you are done" };
        _arrangeItem.CheckedChanged += (_, _) => setArranging(_arrangeItem.Checked);
        menu.Items.Add(_arrangeItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(new Forms.ToolStripMenuItem("Help and downloads (opens the BetterTalk web page)", null, (_, _) => OpenHelpPage()));
        var advanced = new Forms.ToolStripMenuItem("Set-up and tools") { ToolTipText = "Options for the person setting BetterTalk up" };
        advanced.DropDownItems.Add(new Forms.ToolStripMenuItem("Run the set-up guide", null, (_, _) => setupGuide())
            { ToolTipText = "A few questions that set the main choices at once: pointing, opening, screen and corner, hiding, voice" });
        advanced.DropDownItems.Add(new Forms.ToolStripMenuItem("Put the panels back beside the keyboard", null, (_, _) => putPanelsBack())
            { ToolTipText = "Every Itch panel back beside the keyboard at its usual width" });
        advanced.DropDownItems.Add(new Forms.ToolStripMenuItem("Open the settings file in Notepad", null, (_, _) => openSettingsFile())
            { ToolTipText = "Every setting as plain text. Close BetterTalk's Settings window before editing it" });
        menu.Items.Add(advanced);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(new Forms.ToolStripMenuItem("Exit BetterTalk", null, (_, _) => exit())
            { ToolTipText = "Closes BetterTalk completely. It will not open from the corner until you start it again" });
        _icon = new Forms.NotifyIcon
        {
            Icon = _image,
            Text = "BetterTalk: click to open, right-click for the menu",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) show(); };
    }

    private const string HelpPage = "https://github.com/Nelcyon/bettertalkopen";

    private static void OpenHelpPage()
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(HelpPage) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
    }

    /// <summary>Keeps the menu's tick in step when edit mode is switched on or off elsewhere.</summary>
    public bool EditModeChecked { set { if (_editItem.Checked != value) _editItem.Checked = value; } }

    /// <summary>Keeps the menu's tick in step when move mode is switched on or off elsewhere.</summary>
    public bool ArrangingChecked { set { if (_arrangeItem.Checked != value) _arrangeItem.Checked = value; } }

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
/// Opens the app from the corner of the screen it sits in (see <see cref="CornerSpot"/>). In click mode a
/// left click released with the pointer in the corner fires (a low-level mouse hook, so even very fast clicks count; the click
/// still reaches whatever is under it). The hook runs on its own background thread with its own message
/// loop: Windows waits for a low-level hook before moving the cursor, so it must never wait on the
/// app's UI thread. In rest mode the pointer has to stay in the corner for the dwell time.
/// </summary>
public sealed class HotCorner : IDisposable
{
    private const int WhMouseLl = 14, WmLButtonUp = 0x0202, WmQuit = 0x0012;
    private readonly Func<int, int, bool> _inCorner;
    private readonly Action _onTrigger;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly int _dwellMs;
    private readonly DispatcherTimer? _timer;
    private readonly Thread? _hookThread;
    private LowLevelMouseProc? _proc; // kept alive while the hook is installed
    private uint _hookThreadId;
    private DateTime? _enteredAt;
    private bool _fired;

    public HotCorner(Aac.Core.Storage.CornerTrigger trigger, int dwellMs, Func<int, int, bool> inCorner, Action onTrigger)
    {
        _inCorner = inCorner;
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
                if (wParam == WmLButtonUp && _inCorner(info.X, info.Y)) _dispatcher.BeginInvoke(_onTrigger);
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
        if (!_inCorner(p.X, p.Y))
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
