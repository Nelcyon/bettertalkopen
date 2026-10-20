using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using Aac.Core.Layout;

namespace Aac.App;

/// <summary>
/// Laid over a panel while the UI is unlocked (move mode): a dashed outline and "Drag to move" that catch every
/// press, so the buttons and the figure underneath cannot be used, and pressing anywhere on the panel grabs it.
/// </summary>
public sealed class PanelGrabCover : Grid
{
    public PanelGrabCover(string title)
    {
        Visibility = Visibility.Collapsed;
        Focusable = false;
        Cursor = System.Windows.Input.Cursors.SizeAll;
        Panel.SetZIndex(this, 40);
        Children.Add(new Border { Background = new SolidColorBrush(Color.FromArgb(0x70, 0x0D, 0x2A, 0x4A)), CornerRadius = new CornerRadius(12) });
        Children.Add(new System.Windows.Shapes.Rectangle
        {
            Stroke = new SolidColorBrush(Color.FromRgb(0x7C, 0xC4, 0xFF)), StrokeThickness = 4, StrokeDashArray = new DoubleCollection { 3, 2 },
            RadiusX = 12, RadiusY = 12, IsHitTestVisible = false,
        });
        var label = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        label.Children.Add(new TextBlock { Text = "✥", FontSize = 44, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center });
        label.Children.Add(new TextBlock { Text = title, FontSize = 22, FontWeight = FontWeights.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center });
        label.Children.Add(new TextBlock { Text = "drag to move", FontSize = 16, Foreground = new SolidColorBrush(Color.FromRgb(0xD6, 0xE6, 0xF8)), HorizontalAlignment = HorizontalAlignment.Center });
        Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xD0, 0x0D, 0x16, 0x24)), CornerRadius = new CornerRadius(10), Padding = new Thickness(18, 8, 18, 10),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false, Child = label,
        });
    }
}

/// <summary>
/// A panel of the Itch screen floating in a window of its own after being dragged away from the keyboard in move mode.
/// It shows the same rows as the panel did beside the keyboard (one focus, one navigation), never takes the
/// keyboard focus from the main window, and hides and shows with it.
/// </summary>
public sealed class PanelWindow : Window
{
    /// <summary>Same name as the main window's: the cell style binds to it through the nearest Window.</summary>
    public static readonly DependencyProperty ShowFocusFrameProperty =
        DependencyProperty.Register(nameof(ShowFocusFrame), typeof(bool), typeof(PanelWindow), new PropertyMetadata(false));

    public bool ShowFocusFrame
    {
        get => (bool)GetValue(ShowFocusFrameProperty);
        set => SetValue(ShowFocusFrameProperty, value);
    }

    public PanelKind Kind { get; }
    public PanelGrabCover Cover { get; }
    public ItemsControl Board { get; }

    /// <summary>The window was dragged or resized by hand (its new place in screen pixels).</summary>
    public event Action<PxRect>? Moved;

    private readonly WindowChrome _chrome = new()
    {
        CaptionHeight = 0, ResizeBorderThickness = new Thickness(0), GlassFrameThickness = new Thickness(0),
        UseAeroCaptionButtons = false, CornerRadius = new CornerRadius(0),
    };

    public PanelWindow(Window owner, PanelKind kind, string title, System.ComponentModel.ICollectionView rows,
        DataTemplateSelector rowTemplates)
    {
        Kind = kind;
        Owner = owner;
        DataContext = owner.DataContext; // the session: the figure view finds it through its window
        Title = title + " – BetterTalk";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.CanResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        Topmost = owner.Topmost;
        WindowStartupLocation = WindowStartupLocation.Manual;
        // Only the panel itself is drawn: no window background around it.
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Foreground = (Brush)Application.Current.FindResource("Fg");
        WindowChrome.SetWindowChrome(this, _chrome);

        Cover = new PanelGrabCover(title);
        Board = new ItemsControl
        {
            Focusable = false,
            ItemTemplateSelector = rowTemplates,
            ItemsSource = rows,
            ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(BoardRowsPanel))),
        };
        var frame = new Border
        {
            Background = (Brush)Application.Current.FindResource("Bg"),
            BorderBrush = (Brush)Application.Current.FindResource("MenuBorder"), BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(12), Padding = new Thickness(2), Child = Board,
        };
        var root = new Grid();
        root.Children.Add(frame);
        root.Children.Add(Cover);
        Content = root;

        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            // Never take the keyboard from the main window (QuadStick keys must keep going there), and stay out of Alt+Tab.
            var style = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
            SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(style | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW));
            HwndSource.FromHwnd(hwnd)?.AddHook((IntPtr _, int msg, IntPtr _, IntPtr _, ref bool _) =>
            {
                if (msg == 0x0232 && WindowPlacement.Rect(this) is { } rect) Moved?.Invoke(rect); // WM_EXITSIZEMOVE
                return IntPtr.Zero;
            });
        };
    }

    /// <summary>Move mode: the cover catches every press (to drag the panel), and the edges can be dragged to resize it.</summary>
    public void SetArranging(bool on)
    {
        Cover.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        _chrome.ResizeBorderThickness = on ? new Thickness(10) : new Thickness(0);
    }

    /// <summary>The window is exactly as tall as its buttons (no empty space below them); only its width can be dragged.</summary>
    public void FitHeight(double boardHeight, double scale)
    {
        // 2+2 frame border, 2+2 frame padding
        var height = boardHeight * scale + 8;
        MinHeight = MaxHeight = Height = height;
    }

    /// <summary>The panel's buttons are drawn at the main window's scale, so they match the keyboard's.</summary>
    public void SetScale(double scale) => Board.LayoutTransform = new ScaleTransform(scale, scale);

    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TOOLWINDOW = 0x00000080, WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
}
