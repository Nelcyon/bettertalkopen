using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Aac.Core.Input;
using Aac.Core.Layout;
using Aac.Core.Session;
using Aac.Core.Storage;

namespace Aac.App;

public partial class MainWindow : Window
{
    private readonly CommunicationSession _session;
    private readonly InputGate _gate;
    private readonly AppSettings _settings;
    private GlobalHotkeys? _hotkeys;
    private WindowState _restoreState = WindowState.Normal;
    private bool _fullscreen;

    public MainWindow(CommunicationSession session, InputGate gate, AppSettings settings)
    {
        InitializeComponent();
        _session = session;
        _gate = gate;
        _settings = settings;
        DataContext = session;
        // One list of rows (one focus, one navigation), shown in several places: the board, the Itch figure panel
        // (the figure), the controls panel (the buttons that move it), and any panel pulled out into a
        // window of its own (see SyncPanels). Each place shows only its own rows.
        Board.ItemsSource = RowsWhere(session, r => !IsPanelRow(r), arrangeable: false);
        Board.SizeChanged += (_, _) => FitMovePanel();
        ItchBoard.ItemsSource = RowsWhere(session, r => HostShows(PanelKind.Figure, floating: false, r));
        MoveBoard.ItemsSource = RowsWhere(session, r => HostShows(PanelKind.Move, floating: false, r));
        // The covers laid over the panels while the UI is unlocked: press anywhere on one to drag it.
        ItchCoverHost.Content = Cover(PanelKind.Figure);
        MoveCoverHost.Content = Cover(PanelKind.Move);

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
        _session.PropertyChanged += (_, _) => { UpdateDrawerSpace(); SyncPanels(); };

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
        // Stay docked in the corner of the chosen screen when screens, resolution or scaling change.
        EventHandler displayChanged = (_, _) => Dispatcher.BeginInvoke(Dock);
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += displayChanged;
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(Dock);
        Closed += (_, _) => Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= displayChanged;
        // Any mouse or touch press (on a key or not) means the user is pointing, not stepping: drop the frame.
        PreviewMouseDown += (_, _) => { _lastInputWasNavigationKey = false; UpdateFocusFrame(); };
        PreviewTouchDown += (_, _) => { _lastInputWasNavigationKey = false; UpdateFocusFrame(); };

        // Lives in its corner of the chosen screen; the size is whatever the user last dragged it to. The exact
        // pixels are set once the window has a Windows handle (see Dock); this is a first guess in WPF units.
        var screen = Screens.Choose(settings.Screen);
        var scale = WindowPlacement.Scale(this);
        var maxWidth = screen.WorkArea.Width / scale;
        var maxHeight = screen.WorkArea.Height / scale;
        if (WindowSizeStore.Load() is { } size)
        {
            Width = Math.Min(size.Width, maxWidth);
            Height = Math.Min(size.Height, maxHeight);
        }
        else
        {
            Width = Math.Min(Width, maxWidth);
            Height = Math.Min(Height, maxHeight);
        }
        var (x, y) = Anchoring.Place(settings.Anchor, screen.WorkArea, (int)Math.Round(Width * scale), (int)Math.Round(Height * scale));
        Left = x / scale;
        Top = y / scale;
        MirrorLayout();
        SizeChanged += (_, e) => { MeasureToLayout("resize→layout"); OnSizeChanged(e); };
        if (settings.StartFullscreen) SetFullscreen(true);

        PreviewKeyDown += OnPreviewKeyDown;
        PreviewKeyUp += OnPreviewKeyUp;
        // A mouse button (not the left one) can be an in-app key too, e.g. right click for Type a space.
        PreviewMouseDown += (_, e) => { if (MouseKeyName(e.ChangedButton) is { } name && RunKey(name, false)) e.Handled = true; };
        PreviewMouseUp += (_, e) => { if (MouseKeyName(e.ChangedButton) is { } name && ReleaseKey(name)) e.Handled = true; };
        PreviewTextInput += OnPreviewTextInput;
        Activated += (_, _) => _gate.FocusGained();
        Deactivated += (_, _) => _gate.FocusLost();
        StateChanged += (_, _) => { if (WindowState != WindowState.Minimized) _restoreState = WindowState; SyncPanels(); };
        _session.WindowToggleRequested += (_, _) => ToggleWindow();
        if (settings.AutoHideWhenMouseLeaves) StartAutoHide(settings.AutoHideAfterMs);
        SourceInitialized += OnSourceInitialized;
        Closing += OnClosing;
        Closed += (_, _) => _hotkeys?.Dispose();
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

    /// <summary>A view of the session's rows for one host; an arrangeable one is refreshed when the panels are rearranged.</summary>
    private System.Windows.Data.ListCollectionView RowsWhere(CommunicationSession session, Func<string, bool> name, bool arrangeable = true)
    {
        var view = new System.Windows.Data.ListCollectionView((System.Collections.IList)session.Rows);
        view.Filter = o => o is RowView r && name(r.Name);
        if (arrangeable) _rowViews.Add(view);
        return view;
    }

    private static bool IsFigureRow(string r) => r == CommunicationSession.ItchViewRow;
    private static bool IsMoveRow(string r) => r == CommunicationSession.ItchMoveTitleRow || r.StartsWith(CommunicationSession.ItchMoveRowPrefix, StringComparison.Ordinal);
    private static bool IsPanelRow(string r) => r.StartsWith(CommunicationSession.ItchRowPrefix, StringComparison.Ordinal);

    /// <summary>
    /// Which rows a panel's host shows, beside the keyboard or floating: the figure alone, or the controls.
    /// </summary>
    private bool HostShows(PanelKind kind, bool floating, string row) => floating == _layout.IsDetached(kind) && kind switch
    {
        PanelKind.Figure => IsFigureRow(row),
        _ => IsMoveRow(row),
    };

    /// <summary>The window width the user chose, without the Itch panel; the keys are scaled to this.</summary>
    private double _baseWidth = double.NaN;
    private double _widthTarget = double.NaN;
    /// <summary>How much wider the window is because the Itch panel is open.</summary>
    private double _itchExtra;
    /// <summary>The Itch panels' widths in the board's own units (the keyboard is 1400 wide).</summary>
    private const double ItchPanelUnits = 640, MovePanelUnits = 520;
    /// <summary>The Itch width (in board units) the window currently has room for: 0 or the figure panel.</summary>
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
        foreach (var w in _floating.Values) w.SetScale(scale);
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        // Note when the window is being dragged or resized, so auto-hide never fires mid-drag.
        HwndSource.FromHwnd(hwnd)?.AddHook((IntPtr _, int msg, IntPtr _, IntPtr _, ref bool _) =>
        {
            if (msg == 0x0231) _movingOrSizing = true;       // WM_ENTERSIZEMOVE
            else if (msg == 0x0232)                          // WM_EXITSIZEMOVE: back flush in the corner after a resize by hand
            {
                _movingOrSizing = false;
                Dispatcher.BeginInvoke(Dock);
            }
            return IntPtr.Zero;
        });
        Dock();
        var problems = new List<string>();
        _hotkeys = GlobalHotkeys.Register(hwnd, _settings.Hotkeys.Select(h => (h.Key, HotkeyRun(h.Value))), problems);
        if (problems.Count > 0) _session.ShowNotice(string.Join(" ", problems));
    }

    /// <summary>What a system-wide hotkey does; an action name from an older or hand-edited settings file that is not known shows or hides.</summary>
    private Action HotkeyRun(string action) => Enum.TryParse<HotkeyAction>(action, true, out var a) ? a switch
    {
        HotkeyAction.Show => ShowFromTray,
        HotkeyAction.Hide => HideToTray,
        HotkeyAction.ShowWithItch => () => { ShowFromTray(); _session.OpenItch(true); },
        _ => ToggleWindow,
    } : ToggleWindow;

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
        if (Keyboard.Modifiers == ModifierKeys.Control && ClipboardKey(e.Key)) { e.Handled = true; return; }
        if (RunKey(KeyName(e), e.IsRepeat)) e.Handled = true;
    }

    /// <summary>Ctrl-C, Ctrl-X and Ctrl-V (a QuadStick's soft sips) copy, cut and paste the whole message.</summary>
    private bool ClipboardKey(Key key)
    {
        try
        {
            switch (key)
            {
                case Key.C when _session.Draft.Length > 0: Clipboard.SetText(_session.Draft); _session.ShowNotice("Message copied."); return true;
                case Key.X when _session.Draft.Length > 0: Clipboard.SetText(_session.Draft); _session.HandleAction(InputAction.Clear); _session.ShowNotice("Message cut. Undo brings it back."); return true;
                case Key.V when Clipboard.ContainsText(): _session.TypeText(Clipboard.GetText()); return true;
                case Key.C or Key.X or Key.V: return true;
                default: return false;
            }
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return true; // another program has the clipboard open for a moment
        }
    }

    internal static string? MouseKeyName(MouseButton button) => button switch
    {
        MouseButton.Right => "RightClick",
        MouseButton.Middle => "MiddleClick",
        MouseButton.XButton1 => "SideButton1",
        MouseButton.XButton2 => "SideButton2",
        _ => null, // the left button presses BetterTalk's buttons and is never a key
    };

    /// <summary>A key (or mouse button) pressed in BetterTalk: its bound action, if any. True when it was taken.</summary>
    private bool RunKey(string name, bool isRepeat)
    {
        var result = _gate.KeyDown(name, isRepeat);
        Apply(result.Action);
        return result.Handled;
    }

    /// <summary>A key (or mouse button) let go: some actions happen on release. True when it was taken.</summary>
    private bool ReleaseKey(string name)
    {
        var result = _gate.KeyUp(name);
        Apply(result.Action);
        PerfLog.Set("message length", _session.Draft.Length);
        return result.Handled;
    }

    private void Apply(InputAction? action)
    {
        if (action is not { } found) return;
        var a = FocusHighlightPolicy.Resolve(found, ShowFocusFrame);
        if (a is InputAction.MoveUp or InputAction.MoveDown or InputAction.MoveLeft or InputAction.MoveRight)
        {
            _lastInputWasNavigationKey = true;
            UpdateFocusFrame();
        }
        var pressed = a == InputAction.Select ? _session.FocusedCell?.Id : null;
        _session.HandleAction(a);
        if (pressed is not null && FindCellButton(pressed) is { } button) Flash(button);
    }

    private void OnPreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (ReleaseKey(KeyName(e))) e.Handled = true;
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
            var room = RoomToGrow(vertical: true);
            _drawerExtra = Math.Round(Math.Max(0, Math.Min(wanted, room)));
            // Where the screen has no room to grow, the drawer still opens (smaller keys) rather than not at all.
            panel.DrawerHeight = (_drawerExtra > 0 ? _drawerExtra : wanted) / scale;
            if (_drawerExtra > 0) SetDrawerHeight(_baseHeight + _drawerExtra);
        }
        else ShrinkDrawerSpace(panel);
    }

    // ---------- the Itch panels: beside the keyboard, or pulled out into windows of their own ----------

    /// <summary>How the panels are arranged (layout.json): beside the keyboard or floating, and how wide.</summary>
    private readonly PanelLayout _layout = LayoutStore.Load(AppPaths.Layout);
    /// <summary>The UI is unlocked (move mode): covers over the panels to drag them, and floating windows can be resized.</summary>
    private bool _arranging;
    private readonly Dictionary<PanelKind, PanelWindow> _floating = new();
    /// <summary>The panels' views of the rows, refreshed when the arrangement changes which host shows what.</summary>
    private readonly List<System.Windows.Data.ListCollectionView> _rowViews = new();
    private readonly Dictionary<PanelKind, PanelGrabCover> _covers = new();

    private PanelGrabCover Cover(PanelKind kind)
    {
        var cover = new PanelGrabCover(TitleOf(kind));
        cover.MouseLeftButtonDown += (_, e) => BeginDrag(kind, e);
        _covers[kind] = cover;
        return cover;
    }

    /// <summary>
    /// Shows the Itch panels where the arrangement puts them. Beside the keyboard, the window gets wider or
    /// narrower by exactly their width, in one step, so nothing else moves; a panel pulled out gets a floating
    /// window of its own instead.
    /// </summary>
    private void SyncPanels()
    {
        var figure = _session.ItchOpen && !_layout.IsDetached(PanelKind.Figure);
        var move = _session.ItchOpen && _session.ItchControlsOpen && !_layout.IsDetached(PanelKind.Move);
        var figureUnits = _layout.WidthOf(PanelKind.Figure, ItchPanelUnits);
        var moveUnits = _layout.WidthOf(PanelKind.Move, MovePanelUnits);
        // Each panel's width plus the gap beside it, so the keyboard keeps exactly its width.
        var units = (figure ? figureUnits + Gap(ItchPanel) : 0) + (move ? moveUnits + Gap(MovePanel) : 0);
        if (Math.Abs(units - _itchUnitsShown) >= 1e-6 && !double.IsNaN(_baseWidth))
        {
            _itchUnitsShown = units;
            var scale = _scale?.ScaleX ?? 1.0;
            var wanted = units * scale;
            var room = RoomToGrow(vertical: false);
            var extra = Math.Round(Math.Max(0, Math.Min(wanted, room)));
            // Where the screen has no room to widen, the panels still open, taking width from the keyboard.
            var shrink = wanted > 0 && extra < wanted ? Math.Max(0.1, extra / wanted) : 1;
            if (extra == 0 && wanted > 0) shrink = 1;
            ItchPanel.Width = figureUnits * shrink;
            MovePanel.Width = moveUnits * shrink;
            ItchPanel.Visibility = figure ? Visibility.Visible : Visibility.Collapsed;
            MovePanel.Visibility = move ? Visibility.Visible : Visibility.Collapsed;
            if (Math.Abs(extra - _itchExtra) >= 1)
            {
                _itchExtra = extra;
                _widthTarget = _baseWidth + extra;
                SetAnchoredSize(_widthTarget, null);
            }
        }
        var docked = ItchPanel.Visibility == Visibility.Visible || MovePanel.Visibility == Visibility.Visible;
        var left = Anchoring.IsLeft(_settings.Anchor);
        KeyboardBack.Margin = new Thickness(docked && !left ? 0 : -14, -14, docked && left ? 0 : -14, -14);
        SyncFloating();
        FitMovePanel();
    }

    /// <summary>
    /// The controls panel is only as tall as its rows need, at the keyboard's row height, so its buttons are
    /// about the size of keys instead of stretching over the whole height of the window.
    /// </summary>
    private void FitMovePanel()
    {
        var unit = RowsPanel()?.LastKeyboardUnit ?? 0;
        if (unit <= 0) { MovePanel.ClearValue(HeightProperty); return; }
        var chrome = 2 * (MovePanel.BorderThickness.Top + MovePanel.Padding.Top);
        MovePanel.Height = (BoardRowsPanel.TitleWeight + 4 * BoardRowsPanel.MoveRowWeight) * unit + chrome;
        if (_floating.TryGetValue(PanelKind.Move, out var floating)) FitFloatingMove(floating);
    }

    /// <summary>A pulled-out Move panel is only as tall as its buttons.</summary>
    private void FitFloatingMove(PanelWindow w)
    {
        var unit = RowsPanel()?.LastKeyboardUnit ?? 0;
        if (unit <= 0) return;
        w.FitHeight((BoardRowsPanel.TitleWeight + 4 * BoardRowsPanel.MoveRowWeight) * unit, _scale?.ScaleX ?? 1.0);
    }

    /// <summary>Floating panels show while Itch is open (the controls: while they are shown) and this window is on screen.</summary>
    private void SyncFloating()
    {
        foreach (var kind in new[] { PanelKind.Figure, PanelKind.Move })
        {
            var wanted = IsVisible && WindowState != WindowState.Minimized && _session.ItchOpen && _layout.IsDetached(kind)
                && (kind != PanelKind.Move || _session.ItchControlsOpen);
            if (wanted) ShowFloating(kind);
            else if (_floating.TryGetValue(kind, out var w) && w.IsVisible) w.Hide();
        }
    }

    private static string TitleOf(PanelKind kind) => kind switch
    {
        PanelKind.Figure => "Itch figure",
        _ => "Move the figure",
    };

    private static double UsualUnits(PanelKind kind) => kind == PanelKind.Move ? MovePanelUnits : ItchPanelUnits;

    private void ShowFloating(PanelKind kind)
    {
        if (!_floating.TryGetValue(kind, out var w))
        {
            w = new PanelWindow(this, kind, TitleOf(kind), RowsWhere(_session, r => HostShows(kind, floating: true, r)),
                (System.Windows.Controls.DataTemplateSelector)FindResource("RowTemplates"));
            w.Cover.MouseLeftButtonDown += (_, e) => BeginDrag(kind, e);
            w.Cover.MouseMove += (_, _) => DragTo();
            w.Cover.MouseLeftButtonUp += (_, _) => EndDrag();
            w.Cover.LostMouseCapture += (_, _) => EndDrag();
            w.Moved += rect => { _layout.SetFloat(kind, rect); SaveLayout(); };
            // A mouse button bound as a key works over a pulled-out panel too.
            w.PreviewMouseDown += (_, e) => { if (MouseKeyName(e.ChangedButton) is { } name && RunKey(name, false)) e.Handled = true; };
            w.PreviewMouseUp += (_, e) => { if (MouseKeyName(e.ChangedButton) is { } name && ReleaseKey(name)) e.Handled = true; };
            w.SetScale(_scale?.ScaleX ?? 1.0);
            w.SetArranging(_arranging);
            w.ShowFocusFrame = ShowFocusFrame;
            _floating[kind] = w;
        }
        if (w.IsVisible) return;
        var rect = _layout.FloatOf(kind) ?? DefaultFloatRect(kind);
        // A first guess in WPF units, then the exact pixels once the window exists.
        var dpi = WindowPlacement.Scale(this);
        w.Left = rect.Left / dpi;
        w.Top = rect.Top / dpi;
        w.Width = rect.Width / dpi;
        w.Height = rect.Height / dpi;
        w.Show();
        WindowPlacement.MoveAndSize(w, rect.Left, rect.Top, rect.Width, rect.Height);
        if (kind == PanelKind.Move) FitFloatingMove(w);
    }

    /// <summary>
    /// Where a panel floats the first time it is pulled out: beside the window on the side it grows toward, as
    /// tall as the window (Move the figure: as tall as its rows, at the bottom), kept on the screen.
    /// </summary>
    private PxRect DefaultFloatRect(PanelKind kind)
    {
        var work = Screens.Choose(_settings.Screen).WorkArea;
        var dpi = WindowPlacement.Scale(this);
        var scale = (_scale?.ScaleX ?? 1.0) * dpi;
        var main = WindowPlacement.Rect(this) ?? new PxRect(work.Left, work.Top, work.Left + (int)(1400 * scale), work.Top + (int)(900 * scale));
        var width = Math.Min(work.Width, (int)Math.Round(UsualUnits(kind) * scale));
        var rows = (BoardRowsPanel.TitleWeight + 4 * BoardRowsPanel.MoveRowWeight) * (RowsPanel()?.LastKeyboardUnit ?? 100);
        var height = Math.Min(work.Height, kind == PanelKind.Move ? (int)Math.Round(rows * scale + 24 * dpi) : main.Height);
        var gap = (int)Math.Round(8 * dpi);
        var left = Anchoring.IsLeft(_settings.Anchor) ? main.Right + gap : main.Left - gap - width;
        var top = kind == PanelKind.Move ? main.Bottom - height : main.Top;
        left = Math.Clamp(left, work.Left, work.Right - width);
        top = Math.Clamp(top, work.Top, work.Bottom - height);
        return new PxRect(left, top, left + width, top + height);
    }

    /// <summary>
    /// Move mode ("Unlock UI"): every panel gets a cover that takes the presses, so its buttons and the figure cannot
    /// be used, and pressing anywhere on it drags it; floating panels can also be resized by their edges. Lock UI ends it.
    /// </summary>
    public void SetArranging(bool on)
    {
        if (_drag is not null) EndDrag();
        _arranging = on;
        foreach (var cover in _covers.Values) cover.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        MoveBadge.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        foreach (var w in _floating.Values) w.SetArranging(on);
        if (on && !_session.ItchOpen) _session.OpenItch(true);
    }

    public bool IsArranging => _arranging;

    /// <summary>The Lock UI button on the move-mode label.</summary>
    public event Action? LockUiRequested;

    private void LockUi_Click(object sender, RoutedEventArgs e) => LockUiRequested?.Invoke();

    private void PutBack_Click(object sender, RoutedEventArgs e) => PutAllPanelsBack();

    // ---------- dragging a panel in move mode ----------

    /// <summary>The panel being dragged: its floating window and where in it the mouse took hold (screen pixels).</summary>
    private (PanelKind Kind, PanelWindow Window, int Dx, int Dy)? _drag;

    /// <summary>
    /// Takes hold of a panel. One beside the keyboard comes out into a window of its own at exactly the same place
    /// first, so it simply follows the mouse from where it was.
    /// </summary>
    private void BeginDrag(PanelKind kind, System.Windows.Input.MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (!_arranging || !NativeMouse.GetCursorPos(out var p)) return;
        if (!_layout.IsDetached(kind))
        {
            FrameworkElement host = kind == PanelKind.Figure ? ItchPanel : MovePanel;
            if (!host.IsVisible) return;
            var topLeft = host.PointToScreen(new Point(0, 0));
            var bottomRight = host.PointToScreen(new Point(host.ActualWidth, host.ActualHeight));
            _layout.SetDetached(kind, true);
            _layout.SetFloat(kind, new PxRect((int)Math.Round(topLeft.X), (int)Math.Round(topLeft.Y), (int)Math.Round(bottomRight.X), (int)Math.Round(bottomRight.Y)));
            ApplyLayout();
        }
        if (!_floating.TryGetValue(kind, out var w) || !w.IsVisible || WindowPlacement.Rect(w) is not { } at) return;
        _drag = (kind, w, p.X - at.Left, p.Y - at.Top);
        w.Cover.CaptureMouse();
    }

    /// <summary>Follows the mouse, snapping to (and sliding along) the edges of the keyboard, the other panels and the screen.</summary>
    private void DragTo()
    {
        if (_drag is not { } d || !NativeMouse.GetCursorPos(out var p) || WindowPlacement.Rect(d.Window) is not { } at) return;
        var wanted = new PxRect(p.X - d.Dx, p.Y - d.Dy, p.X - d.Dx + at.Width, p.Y - d.Dy + at.Height);
        var dpi = WindowPlacement.Scale(this);
        var snapped = PanelSnap.Snap(wanted, SnapTargets(d.Kind), Screens.At(p.X, p.Y).WorkArea, (int)Math.Round(24 * dpi), (int)Math.Round(8 * dpi));
        if (snapped.Left != at.Left || snapped.Top != at.Top) WindowPlacement.Move(d.Window, snapped.Left, snapped.Top);
    }

    private void EndDrag()
    {
        if (_drag is not { } d) return;
        _drag = null;
        if (d.Window.Cover.IsMouseCaptured) d.Window.Cover.ReleaseMouseCapture();
        if (WindowPlacement.Rect(d.Window) is { } at) _layout.SetFloat(d.Kind, at);
        SaveLayout();
    }

    /// <summary>What a dragged panel snaps to: the keyboard, the panels still beside it, and the other floating panels.</summary>
    private IEnumerable<PxRect> SnapTargets(PanelKind moving)
    {
        foreach (var element in new FrameworkElement[] { KeyboardBack, ItchPanel, MovePanel })
            if (element.IsVisible && element.ActualWidth > 0)
            {
                var a = element.PointToScreen(new Point(0, 0));
                var b = element.PointToScreen(new Point(element.ActualWidth, element.ActualHeight));
                yield return new PxRect((int)Math.Round(a.X), (int)Math.Round(a.Y), (int)Math.Round(b.X), (int)Math.Round(b.Y));
            }
        foreach (var (kind, w) in _floating)
            if (kind != moving && w.IsVisible && WindowPlacement.Rect(w) is { } r) yield return r;
    }

    /// <summary>Everything back beside the keyboard at the usual widths (from the tray menu or Settings).</summary>
    public void PutAllPanelsBack()
    {
        _layout.PutEverythingBack();
        foreach (var w in _floating.Values) w.Hide();
        ApplyLayout();
    }

    private void ApplyLayout()
    {
        foreach (var v in _rowViews) v.Refresh();
        SyncPanels();
        SaveLayout();
    }

    private void SaveLayout()
    {
        try { LayoutStore.Save(AppPaths.Layout, _layout); }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            _session.ShowNotice($"The panel arrangement could not be saved ({ex.Message}).");
        }
    }

    /// <summary>How far (in WPF units) the window can grow away from its anchored corner before it hits the edge of the work area.</summary>
    private double RoomToGrow(bool vertical)
    {
        if (WindowState != WindowState.Normal || _fullscreen) return 0;
        var work = Screens.Choose(_settings.Screen).WorkArea;
        var scale = WindowPlacement.Scale(this);
        return vertical ? work.Height / scale - _baseHeight : work.Width / scale - _baseWidth;
    }

    /// <summary>
    /// Resizes the window (WPF units; null keeps that dimension) with its anchored corner staying put, in one
    /// step: a window anchored at the right or bottom grows leftward or upward, and nothing on screen moves.
    /// </summary>
    private void SetAnchoredSize(double? width, double? height)
    {
        if (WindowPlacement.Rect(this) is not { } rect)
        {
            if (width is { } w0) Width = w0;
            if (height is { } h0) Height = h0;
            return;
        }
        var scale = WindowPlacement.Scale(this);
        var w = width is { } wd ? (int)Math.Round(wd * scale) : rect.Width;
        var h = height is { } hd ? (int)Math.Round(hd * scale) : rect.Height;
        var (x, y) = WindowState != WindowState.Normal || _fullscreen
            ? (rect.Left, rect.Top)
            : Anchoring.Place(_settings.Anchor, Screens.Choose(_settings.Screen).WorkArea, w, h);
        WindowPlacement.MoveAndSize(this, x, y, w, h);
    }

    /// <summary>
    /// Puts the Itch panels and the message box on the side the window grows toward, away from the anchored
    /// corner: panels to the left for a window at the right of the screen; for one at the bottom, the message
    /// box at the top with the drawer under it, so the window grows upward and the keyboard stays still.
    /// </summary>
    private void MirrorLayout()
    {
        var left = Anchoring.IsLeft(_settings.Anchor);
        var top = Anchoring.IsTop(_settings.Anchor);
        System.Windows.Controls.Grid.SetColumn(ItchPanel, left ? 3 : 1);
        System.Windows.Controls.Grid.SetColumn(MovePanel, left ? 4 : 0);
        ItchPanel.Margin = left ? new Thickness(8, 0, 0, 0) : new Thickness(0, 0, 8, 0);
        MovePanel.Margin = ItchPanel.Margin;
        ItchViewHost.PanelsOnLeft = !left;
        Root.RowDefinitions[0].Height = top ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
        Root.RowDefinitions[1].Height = top ? GridLength.Auto : new GridLength(1, GridUnitType.Star);
        System.Windows.Controls.Grid.SetRow(MessagePanel, top ? 1 : 0);
        System.Windows.Controls.Grid.SetRow(NoticeBanner, top ? 1 : 0);
        System.Windows.Controls.Grid.SetRow(BoardHost, top ? 0 : 1);
        BoardHost.Margin = top ? new Thickness(0, 0, 0, 8) : new Thickness(0, 8, 0, 0);
    }

    /// <summary>
    /// The set-up guide's preview: the window moves to this screen and corner right away (the settings object
    /// is shared, so Finish keeps it and Skip puts the old place back). The drawer order follows after a restart.
    /// </summary>
    public void PreviewPlacement(string? screen, WindowCorner corner)
    {
        _settings.Screen = screen;
        _settings.Anchor = corner;
        MirrorLayout();
        Dock();
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
        SetAnchoredSize(null, height);
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

    private void UpdateFocusFrame()
    {
        ShowFocusFrame = FocusHighlightPolicy.ShowFrame(_settings.SelectionHighlight, _lastInputWasNavigationKey);
        foreach (var w in _floating.Values) w.ShowFocusFrame = ShowFocusFrame;
    }

    /// <summary>The on-screen button for a cell, to flash it when it is pressed with the Select key (in whichever panel it is).</summary>
    private System.Windows.Controls.Button? FindCellButton(string id)
    {
        var queue = new Queue<DependencyObject>();
        queue.Enqueue(Board);
        queue.Enqueue(ItchBoard);
        queue.Enqueue(MoveBoard);
        foreach (var w in _floating.Values) queue.Enqueue(w.Board);
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

    /// <summary>The Done button on the red button-edit-mode label: the host leaves edit mode.</summary>
    public event Action? EditDoneRequested;

    private void EditDone_Click(object sender, RoutedEventArgs e) => EditDoneRequested?.Invoke();

    public event Action? FeedbackCancelRequested;

    private void FeedbackCancel_Click(object sender, RoutedEventArgs e) => FeedbackCancelRequested?.Invoke();

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
            var busy = _session.IsSpeaking || OwnedWindowsOpen() || _movingOrSizing || _arranging; // never hide while panels are being moved
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

    /// <summary>The settings window (or a message box) is open: don't hide out from under it. Floating panels are part of this window.</summary>
    private static bool OwnedWindowsOpen() =>
        Application.Current.Windows.Cast<Window>().Any(w => w is not (MainWindow or PanelWindow) && w.IsVisible);

    /// <summary>Puts the window's anchored corner exactly on that corner of the chosen screen's work area (above or beside any taskbar), at its current size.</summary>
    private void Dock()
    {
        if (_fullscreen || WindowState != WindowState.Normal || WindowPlacement.Rect(this) is not { } rect) return;
        var (x, y) = Anchoring.Place(_settings.Anchor, Screens.Choose(_settings.Screen).WorkArea, rect.Width, rect.Height);
        if (x != rect.Left || y != rect.Top) WindowPlacement.Move(this, x, y);
    }

    public void HideToTray()
    {
        _gate.FocusLost();
        foreach (var w in _floating.Values) w.Hide(); // the floating panels go with the window
        Hide();
    }

    public void ShowFromTray()
    {
        Dock();
        Show();
        if (WindowState == WindowState.Minimized) WindowState = _restoreState;
        Activate();
        // Windows may refuse to bring a window forward from the background; a brief Topmost toggle is the usual remedy.
        var wasTopmost = Topmost;
        Topmost = true;
        Topmost = wasTopmost;
        SyncPanels(); // and the floating panels come back with it
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
        if (!on) Dock();
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
