using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Aac.Core.Input;
using Aac.Core.Session;
using Aac.Core.Storage;

namespace Aac.App;

public partial class MainWindow : Window
{
    private readonly CommunicationSession _session;
    private readonly InputGate _gate;
    private readonly AppSettings _settings;
    private GlobalHotkey? _hotkey;
    private WindowState _restoreState = WindowState.Normal;
    private bool _fullscreen;

    public MainWindow(CommunicationSession session, InputGate gate, AppSettings settings)
    {
        InitializeComponent();
        _session = session;
        _gate = gate;
        _settings = settings;
        DataContext = session;
        // One list of rows (one focus, one navigation), shown in two places: the board, and the Itch panel.
        Board.ItemsSource = RowsWhere(session, r => !r.StartsWith(CommunicationSession.ItchRowPrefix, StringComparison.Ordinal)
            && !r.StartsWith(CommunicationSession.ItchControlsPrefix, StringComparison.Ordinal));
        ItchBoard.ItemsSource = RowsWhere(session, r => r.StartsWith(CommunicationSession.ItchRowPrefix, StringComparison.Ordinal));
        ItchControlsBoard.ItemsSource = RowsWhere(session, r => r.StartsWith(CommunicationSession.ItchControlsPrefix, StringComparison.Ordinal));

        FontSize = settings.FontSize;
        DraftText.FontSize = settings.FontSize * 1.3;
        // Room for three lines of message, always, so nothing above it moves as the message grows.
        MessageScroll.Height = DraftText.FontSize * 1.34 * 3;
        // Problems are shown for five seconds over the message box, then the box is back.
        var noticeTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        noticeTimer.Tick += (_, _) => { noticeTimer.Stop(); NoticeBanner.Visibility = Visibility.Collapsed; };
        string? shownNotice = null;
        void ShowNoticeIfNew()
        {
            if (_session.Notice is not { Length: > 0 } notice || ReferenceEquals(notice, shownNotice)) return;
            shownNotice = notice;
            NoticeText.Text = notice;
            NoticeBanner.Visibility = Visibility.Visible;
            noticeTimer.Stop();
            noticeTimer.Start();
        }
        _session.PropertyChanged += (_, _) => ShowNoticeIfNew();
        Loaded += (_, _) => ShowNoticeIfNew();
        // Opening a drawer makes the window taller by exactly its height (the keyboard stays put); closing shrinks it back.
        _session.PropertyChanged += (_, _) => { UpdateDrawerSpace(); UpdateItchSpace(); };

        // Keep the newest words in view once the message is longer than three lines.
        _session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is not ("" or nameof(CommunicationSession.Draft))) return;
            ShowMessage();
            if (_session.ReadAloudText is null)
                Dispatcher.InvokeAsync(MessageScroll.ScrollToEnd, System.Windows.Threading.DispatcherPriority.Loaded);
        };
        _session.SpeechProgress += (_, _) => ShowMessage();
        ShowMessage();
        Topmost = settings.AlwaysOnTop;
        UpdateFocusFrame();
        if (settings.LockWindowSize)
        {
            ResizeMode = ResizeMode.CanMinimize; // no edge dragging, no maximize
            Chrome.ResizeBorderThickness = new Thickness(0);
        }
        // Stay docked to the top-left of the work area when screens, resolution or scaling change.
        EventHandler displayChanged = (_, _) => Dispatcher.BeginInvoke(DockTopLeft);
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += displayChanged;
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(DockTopLeft);
        Closed += (_, _) => Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= displayChanged;
        // Any mouse or touch press (on a key or not) means the user is pointing, not stepping: drop the frame.
        PreviewMouseDown += (_, _) => { _lastInputWasNavigationKey = false; UpdateFocusFrame(); };
        PreviewTouchDown += (_, _) => { _lastInputWasNavigationKey = false; UpdateFocusFrame(); };

        // Lives in the top-left corner; the size is whatever the user last dragged it to.
        var work = SystemParameters.WorkArea;
        if (WindowSizeStore.Load() is { } size)
        {
            Width = Math.Min(size.Width, work.Width);
            Height = Math.Min(size.Height, work.Height);
        }
        else
        {
            Width = Math.Min(Width, work.Width);
            Height = Math.Min(Height, work.Height);
        }
        Left = work.Left;
        Top = work.Top;
        SizeChanged += (_, e) => { MeasureToLayout("resize→layout"); OnSizeChanged(e); };
        if (settings.StartFullscreen) SetFullscreen(true);

        PreviewKeyDown += OnPreviewKeyDown;
        PreviewKeyUp += OnPreviewKeyUp;
        PreviewTextInput += OnPreviewTextInput;
        Activated += (_, _) => _gate.FocusGained();
        Deactivated += (_, _) => _gate.FocusLost();
        StateChanged += (_, _) => { if (WindowState != WindowState.Minimized) _restoreState = WindowState; };
        _session.WindowToggleRequested += (_, _) => ToggleWindow();
        if (settings.AutoHideWhenMouseLeaves) StartAutoHide(settings.AutoHideAfterMs);
        SourceInitialized += OnSourceInitialized;
        Closing += OnClosing;
        Closed += (_, _) => _hotkey?.Dispose();
    }

    /// <summary>Set by the app when it is really exiting; otherwise closing only hides to the tray.</summary>
    public bool AllowClose { get; set; }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // Saved without the drawer extension, so a restart never starts extended.
        if (WindowState == WindowState.Normal && !_fullscreen) WindowSizeStore.Save(Width - _itchExtra, Height - _drawerExtra);
        if (AllowClose) return;
        e.Cancel = true;
        HideToTray();
    }

    /// <summary>
    /// Everything is designed at 1400×900 and scaled to the window, so making the window smaller
    /// or larger shrinks or grows the whole board evenly while the rows still fill it.
    /// </summary>
    /// <summary>
    /// The window height the user chose, without any drawer extension. The keys are scaled to this (and the width)
    /// only, so growing or shrinking the window for a drawer can never change their size.
    /// </summary>
    private double _baseHeight = double.NaN;
    /// <summary>The height the window was just set to for a drawer; that resize must not change the base.</summary>
    private double _drawerResizeTarget = double.NaN;

    private static System.ComponentModel.ICollectionView RowsWhere(CommunicationSession session, Func<string, bool> name)
    {
        var view = new System.Windows.Data.ListCollectionView((System.Collections.IList)session.Rows);
        view.Filter = o => o is RowView r && name(r.Name);
        return view;
    }

    /// <summary>The window width the user chose, without the Itch panel; the keys are scaled to this.</summary>
    private double _baseWidth = double.NaN;
    private double _widthTarget = double.NaN;
    /// <summary>How much wider the window is because the Itch panel is open.</summary>
    private double _itchExtra;
    /// <summary>The Itch panels' widths in the board's own units (the keyboard is 1400 wide).</summary>
    private const double ItchPanelUnits = 560, ItchControlsUnits = 600;
    /// <summary>The Itch width (in board units) the window currently has room for: 0, figure, or figure + controls.</summary>
    private double _itchUnitsShown;

    /// <summary>
    /// When the user resizes the window by hand, everything scales together: the keyboard and any open Itch panels
    /// (or drawer) keep the share of the window they had. So folding a panel away later removes exactly that
    /// panel's width, and the keyboard and figure keep their size.
    /// </summary>
    private void OnSizeChanged(SizeChangedEventArgs e)
    {
        if (e.WidthChanged)
        {
            if (!double.IsNaN(_widthTarget) && Math.Abs(ActualWidth - _widthTarget) < 1.5) _widthTarget = double.NaN;
            else if (double.IsNaN(_widthTarget))
            {
                var share = double.IsNaN(_baseWidth) || _baseWidth + _itchExtra <= 0 ? 0 : _itchExtra / (_baseWidth + _itchExtra);
                _itchExtra = Math.Round(ActualWidth * share);
                _baseWidth = ActualWidth - _itchExtra;
            }
        }
        if (!e.HeightChanged && !double.IsNaN(_baseHeight)) { ScaleToWindow(); return; }
        var resizedByHand = false;
        if (!double.IsNaN(_drawerResizeTarget) && Math.Abs(ActualHeight - _drawerResizeTarget) < 1.5)
            _drawerResizeTarget = double.NaN;                 // our own drawer resize arriving: base unchanged
        else if (double.IsNaN(_drawerResizeTarget))
        {
            // The user resized the window (or this is the first layout).
            var share = double.IsNaN(_baseHeight) || _baseHeight + _drawerExtra <= 0 ? 0 : _drawerExtra / (_baseHeight + _drawerExtra);
            _drawerExtra = Math.Round(ActualHeight * share);
            _baseHeight = ActualHeight - _drawerExtra;
            resizedByHand = true;
        }
        ScaleToWindow();
        // The open drawer keeps its share too, measured in the board's own (scaled) units.
        if (resizedByHand && _drawerExtra > 0 && RowsPanel() is { } panel) panel.DrawerHeight = _drawerExtra / (_scale?.ScaleY ?? 1.0);
    }

    private static readonly Brush ReadWordBack = new SolidColorBrush(Color.FromRgb(0xFF, 0xD5, 0x4F));
    private static readonly Brush ReadWordFore = Brushes.Black;
    private static readonly Brush NotYetRead = new SolidColorBrush(Color.FromRgb(0xA8, 0xA8, 0xA8));
    private (string? Text, int? Word) _shownMessage;

    /// <summary>
    /// The message box: what the user is typing, or, while a message is being said, that message with the word
    /// being said highlighted (words already said in white, the rest grey), until it finishes.
    /// </summary>
    private void ShowMessage()
    {
        var reading = _session.ReadAloudText;
        var shown = (reading ?? _session.Draft, reading is null ? null : _session.ReadAloudWord);
        if (reading is not null) shown.Item2 ??= -1;
        if (shown == _shownMessage) return;
        _shownMessage = shown;
        DraftText.Inlines.Clear();
        if (reading is null)
        {
            DraftText.Text = _session.Draft;
            return;
        }
        var current = _session.ReadAloudWord ?? -1;
        var words = Aac.Core.Speech.SpeechController.Words(reading);
        var at = 0;
        for (var i = 0; i < words.Count; i++)
        {
            var (start, length) = words[i];
            if (start > at) DraftText.Inlines.Add(new System.Windows.Documents.Run(reading[at..start]));
            var run = new System.Windows.Documents.Run(reading.Substring(start, length));
            if (i == current) { run.Background = ReadWordBack; run.Foreground = ReadWordFore; }
            else if (i > current) run.Foreground = NotYetRead;
            DraftText.Inlines.Add(run);
            at = start + length;
            if (i == current)
            {
                var r = run;
                Dispatcher.InvokeAsync(() => r.BringIntoView(), System.Windows.Threading.DispatcherPriority.Loaded);
            }
        }
        if (at < reading.Length) DraftText.Inlines.Add(new System.Windows.Documents.Run(reading[at..]));
    }

    private ScaleTransform? _scale;

    private void ScaleToWindow()
    {
        if (ActualWidth <= 0 || double.IsNaN(_baseHeight) || _baseHeight <= 0) return;
        var width = double.IsNaN(_baseWidth) ? ActualWidth : _baseWidth;
        var fit = Math.Min(width / 1400.0, _baseHeight / 900.0);
        var scale = Math.Clamp(fit, 0.3, 3.0) * _settings.UiScale;
        if (_scale is not null && Math.Abs(_scale.ScaleX - scale) < 1e-6) return; // unchanged: no relayout
        Root.LayoutTransform = _scale = new ScaleTransform(scale, scale);
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        // Note when the window is being dragged or resized, so auto-hide never fires mid-drag.
        HwndSource.FromHwnd(hwnd)?.AddHook((IntPtr _, int msg, IntPtr _, IntPtr _, ref bool _) =>
        {
            if (msg == 0x0231) _movingOrSizing = true;       // WM_ENTERSIZEMOVE
            else if (msg == 0x0232) _movingOrSizing = false; // WM_EXITSIZEMOVE
            return IntPtr.Zero;
        });
        _hotkey = GlobalHotkey.TryRegister(hwnd, _settings.GlobalHotkey, ToggleWindow, out var problem);
        if (problem is not null) _session.ShowNotice(problem);
    }

    private static string KeyName(KeyEventArgs e) => (e.Key == Key.System ? e.SystemKey : e.Key).ToString();

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F11 && !e.IsRepeat && !_settings.LockWindowSize)
        {
            SetFullscreen(!_fullscreen);
            e.Handled = true;
            return;
        }
        MeasureToLayout("key→layout");
        var result = _gate.KeyDown(KeyName(e), e.IsRepeat);
        if (result.Handled) e.Handled = true;
        if (result.Action is { } action)
        {
            if (action is InputAction.MoveUp or InputAction.MoveDown or InputAction.MoveLeft or InputAction.MoveRight)
            {
                _lastInputWasNavigationKey = true;
                UpdateFocusFrame();
            }
            var pressed = action == InputAction.Select ? _session.FocusedCell?.Id : null;
            _session.HandleAction(action);
            if (pressed is not null && FindCellButton(pressed) is { } button) Flash(button);
        }
    }

    private void OnPreviewKeyUp(object sender, KeyEventArgs e)
    {
        var result = _gate.KeyUp(KeyName(e));
        if (result.Handled) e.Handled = true;
        if (result.Action is { } action)
        {
            if (action is InputAction.MoveUp or InputAction.MoveDown or InputAction.MoveLeft or InputAction.MoveRight)
            {
                _lastInputWasNavigationKey = true;
                UpdateFocusFrame();
            }
            var pressed = action == InputAction.Select ? _session.FocusedCell?.Id : null;
            _session.HandleAction(action);
            if (pressed is not null && FindCellButton(pressed) is { } button) Flash(button);
        }
        PerfLog.Set("message length", _session.Draft.Length);
    }

    /// <summary>Plain typing (physical keys, Windows on-screen keyboard, QuadStick text macros) goes into the message.</summary>
    private void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Text)) return;
        MeasureToLayout("type→layout");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        _session.TypeText(e.Text);
        PerfLog.Record("type: session", sw);
        e.Handled = true;
    }

    /// <summary>With PerfLog on: time from now until WPF has finished layout for the resulting change.</summary>
    private void MeasureToLayout(string name)
    {
        if (!PerfLog.Enabled) return;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Dispatcher.InvokeAsync(() => PerfLog.Record(name, sw), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    // Holding an on-screen delete key repeats it, like holding Backspace: after 400 ms, then every 60 ms.
    private System.Windows.Threading.DispatcherTimer? _repeat;
    private string? _repeatId;
    private bool _repeated;

    private void Cell_PressStart(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: "del-letter" or "del-word" } el) return;
        StopRepeat();
        _repeatId = (string)el.Tag;
        _repeated = false;
        _repeat = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _repeat.Tick += (_, _) =>
        {
            _repeat!.Interval = TimeSpan.FromMilliseconds(60);
            _repeated = true;
            _session.ActivateCell(_repeatId!);
        };
        _repeat.Start();
    }

    private void Cell_PressEnd(object sender, MouseEventArgs e) => StopRepeat();

    private void StopRepeat()
    {
        _repeat?.Stop();
        _repeat = null;
    }

    /// <summary>How much taller (in window units) the window currently is because a drawer is open.</summary>
    private double _drawerExtra;
    private BoardRowsPanel? _rowsPanel;

    private BoardRowsPanel? RowsPanel()
    {
        if (_rowsPanel is not null) return _rowsPanel;
        // The keyboard's own panel (inside Board), never the Itch panels' ones: if Itch was opened before the
        // first drawer, a search from the window would find the figure's panel first and give it the drawer's room.
        var queue = new Queue<DependencyObject>();
        queue.Enqueue(Board);
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            if (node is BoardRowsPanel panel) return _rowsPanel = panel;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) queue.Enqueue(VisualTreeHelper.GetChild(node, i));
        }
        return null;
    }

    /// <summary>Extra height in key rows the window currently has: 0, a drawer's, or the Itch figure's.</summary>
    private double _extraRowsShown;

    private void UpdateDrawerSpace()
    {
        var wantedRows = _session.Drawer != Drawer.None ? CommunicationSession.DrawerRows * BoardRowsPanel.DrawerRowWeight : 0;
        if (Math.Abs(wantedRows - _extraRowsShown) < 1e-6 || RowsPanel() is not { } panel || double.IsNaN(_baseHeight)) return;
        _extraRowsShown = wantedRows;
        var open = wantedRows > 0;
        var scale = _scale?.ScaleY ?? 1.0;
        if (open)
        {
            // The extra rows at the keyboard's row height, in window units. The unit comes from the last
            // layout and does not depend on whether a drawer is open, so fast toggling cannot skew it.
            var wanted = wantedRows * panel.LastKeyboardUnit * scale;
            var room = WindowState == WindowState.Normal && !_fullscreen ? SystemParameters.WorkArea.Bottom - (Top + _baseHeight) : 0;
            _drawerExtra = Math.Round(Math.Max(0, Math.Min(wanted, room)));
            // Where the screen has no room to grow, the drawer still opens (smaller keys) rather than not at all.
            panel.DrawerHeight = (_drawerExtra > 0 ? _drawerExtra : wanted) / scale;
            if (_drawerExtra > 0) SetDrawerHeight(_baseHeight + _drawerExtra);
        }
        else ShrinkDrawerSpace(panel);
    }

    /// <summary>
    /// Shows the Itch panels on the right (the figure, and its controls when expanded) and makes the window
    /// wider or narrower by exactly their width, in one step, so nothing on the left moves.
    /// </summary>
    private void UpdateItchSpace()
    {
        var figure = _session.ItchOpen;
        var controls = figure && _session.ItchControlsOpen;
        // Each panel's width plus the gap beside it, so the keyboard keeps exactly its width.
        var units = (figure ? ItchPanelUnits + Gap(ItchPanel) : 0) + (controls ? ItchControlsUnits + Gap(ItchControlsPanel) : 0);
        if (Math.Abs(units - _itchUnitsShown) < 1e-6 || double.IsNaN(_baseWidth)) return;
        _itchUnitsShown = units;
        var scale = _scale?.ScaleX ?? 1.0;
        var wanted = units * scale;
        var room = WindowState == WindowState.Normal && !_fullscreen ? SystemParameters.WorkArea.Right - (Left + _baseWidth) : 0;
        var extra = Math.Round(Math.Max(0, Math.Min(wanted, room)));
        // Where the screen has no room to widen, the panels still open, taking width from the keyboard.
        var shrink = wanted > 0 && extra < wanted ? Math.Max(0.1, extra / wanted) : 1;
        if (extra == 0 && wanted > 0) shrink = 1;
        ItchPanel.Width = ItchPanelUnits * shrink;
        ItchControlsPanel.Width = ItchControlsUnits * shrink;
        ItchPanel.Visibility = figure ? Visibility.Visible : Visibility.Collapsed;
        ItchControlsPanel.Visibility = controls ? Visibility.Visible : Visibility.Collapsed;
        if (Math.Abs(extra - _itchExtra) >= 1)
        {
            _itchExtra = extra;
            _widthTarget = _baseWidth + extra;
            Width = _widthTarget;
        }
    }

    private static double Gap(FrameworkElement panel) => panel.Margin.Left + panel.Margin.Right;

    private void ShrinkDrawerSpace(BoardRowsPanel panel)
    {
        panel.DrawerHeight = 0;
        var had = _drawerExtra;
        _drawerExtra = 0;                                       // before resizing, so nothing subtracts it twice
        if (had > 0) SetDrawerHeight(_baseHeight);
    }

    private void SetDrawerHeight(double height)
    {
        _drawerResizeTarget = height;
        Height = height;
    }

    public static readonly DependencyProperty ShowFocusFrameProperty =
        DependencyProperty.Register(nameof(ShowFocusFrame), typeof(bool), typeof(MainWindow), new PropertyMetadata(false));

    /// <summary>The focus frame shows only while moving with arrow keys or switches; a click hides it again.</summary>
    public bool ShowFocusFrame
    {
        get => (bool)GetValue(ShowFocusFrameProperty);
        set => SetValue(ShowFocusFrameProperty, value);
    }

    private bool _lastInputWasNavigationKey;

    private void UpdateFocusFrame() =>
        ShowFocusFrame = FocusHighlightPolicy.ShowFrame(_settings.SelectionHighlight, _lastInputWasNavigationKey);

    /// <summary>The on-screen button for a cell, to flash it when it is pressed with the Select key.</summary>
    private System.Windows.Controls.Button? FindCellButton(string id)
    {
        // The keyboard's own panel (inside Board), never the Itch panels' ones: if Itch was opened before the
        // first drawer, a search from the window would find the figure's panel first and give it the drawer's room.
        var queue = new Queue<DependencyObject>();
        queue.Enqueue(Board);
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            if (node is System.Windows.Controls.Button { Tag: string tag } b && tag == id) return b;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) queue.Enqueue(VisualTreeHelper.GetChild(node, i));
        }
        return null;
    }

    private static readonly System.Windows.Media.Animation.DoubleAnimation FlashAnimation =
        new(0.55, 0, TimeSpan.FromMilliseconds(220)) { FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop };

    /// <summary>Briefly flashes a pressed button green, so the user sees the press without anything staying lit.</summary>
    private static void Flash(System.Windows.Controls.Button button)
    {
        if (button.Template?.FindName("FlashLayer", button) is UIElement layer)
            layer.BeginAnimation(OpacityProperty, FlashAnimation);
    }

    private void Cell_Click(object sender, RoutedEventArgs e)
    {
        _lastInputWasNavigationKey = false;
        UpdateFocusFrame();
        if (sender is System.Windows.Controls.Button pressed) Flash(pressed);
        // The release that ends a held, repeating delete key is not one more delete.
        if (_repeated && sender is FrameworkElement { Tag: string held } && held == _repeatId)
        {
            _repeated = false;
            return;
        }
        MeasureToLayout("click→layout");
        if (sender is FrameworkElement { Tag: string id }) _session.ActivateCell(id);
        PerfLog.Set("message length", _session.Draft.Length);
    }

    /// <summary>Hide to let the user get back to their game or browser; show again from the corner, the tray or the global shortcut.</summary>
    private void ToggleWindow()
    {
        if (IsVisible && IsActive && WindowState != WindowState.Minimized) HideToTray();
        else ShowFromTray();
    }

    // Auto-hide: once the pointer has been off the window, and nothing was pressed, for the set time,
    // hide to the tray like the Hide button. Checked four times a second with GetCursorPos (no mouse hook).
    private DateTime? _pointerAwaySince;
    private DateTimeOffset _lastInputAt = DateTimeOffset.UtcNow;
    private bool _movingOrSizing;

    private void StartAutoHide(int afterMs)
    {
        PreviewKeyDown += (_, _) => _lastInputAt = DateTimeOffset.UtcNow;
        PreviewMouseDown += (_, _) => _lastInputAt = DateTimeOffset.UtcNow;
        PreviewTouchDown += (_, _) => _lastInputAt = DateTimeOffset.UtcNow;
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += (_, _) =>
        {
            if (!IsVisible || WindowState == WindowState.Minimized || _fullscreen)
            {
                _pointerAwaySince = null;
                return;
            }
            if (PointerOverAppWindow()) _pointerAwaySince = null;
            else _pointerAwaySince ??= DateTime.UtcNow;
            var busy = _session.IsSpeaking || OwnedWindowsOpen() || _movingOrSizing;
            if (Aac.Core.Input.AutoHidePolicy.ShouldHide(DateTimeOffset.UtcNow, _pointerAwaySince, _lastInputAt, busy, Topmost, afterMs))
            {
                _pointerAwaySince = null;
                HideToTray();
            }
        };
        timer.Start();
    }

    /// <summary>True while the pointer is over this window or another of the app's windows (Settings, a message box).</summary>
    private static bool PointerOverAppWindow() =>
        Application.Current.Windows.Cast<Window>().Where(w => w.IsVisible).Any(PointerOver);

    private static bool PointerOver(Window w)
    {
        var hwnd = new WindowInteropHelper(w).Handle;
        return hwnd != IntPtr.Zero && NativeMouse.GetCursorPos(out var p) && NativeMouse.GetWindowRect(hwnd, out var r)
            && p.X >= r.Left && p.X < r.Right && p.Y >= r.Top && p.Y < r.Bottom;
    }

    /// <summary>The settings window (or a message box) is open: don't hide out from under it.</summary>
    private static bool OwnedWindowsOpen() =>
        Application.Current.Windows.Cast<Window>().Any(w => w is not MainWindow && w.IsVisible);

    /// <summary>Puts the window's top-left corner exactly on the work area's (above or beside any taskbar).</summary>
    private void DockTopLeft()
    {
        if (_fullscreen || WindowState != WindowState.Normal) return;
        var work = SystemParameters.WorkArea;
        Left = work.Left;
        Top = work.Top;
    }

    public void HideToTray()
    {
        _gate.FocusLost();
        Hide();
    }

    public void ShowFromTray()
    {
        DockTopLeft();
        Show();
        if (WindowState == WindowState.Minimized) WindowState = _restoreState;
        Activate();
        // Windows may refuse to bring a window forward from the background; a brief Topmost toggle is the usual remedy.
        var wasTopmost = Topmost;
        Topmost = true;
        Topmost = wasTopmost;
    }

    /// <summary>The pointer rested in the top-left corner: open, unless already open and in front.</summary>
    public void OnHotCorner()
    {
        if (IsVisible && IsActive && WindowState != WindowState.Minimized) return;
        ShowFromTray();
    }

    private void SetFullscreen(bool on)
    {
        _fullscreen = on;
        WindowState = on ? WindowState.Maximized : WindowState.Normal;
        if (!on) DockTopLeft();
    }
}

internal static class NativeMouse
{
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct Point { public int X; public int Y; }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct Rect { public int Left; public int Top; public int Right; public int Bottom; }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    public static extern bool GetCursorPos(out Point point);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
}
