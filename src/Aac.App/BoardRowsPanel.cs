using System.Windows;
using System.Windows.Controls;
using Aac.Core.Session;

namespace Aac.App;

/// <summary>
/// Stacks the board rows: the urgent strip gets a slimmer share and every other keyboard row an equal one
/// of the height that is left after <see cref="DrawerHeight"/>. The drawer (Sentences, Quick or Phrases)
/// gets exactly <see cref="DrawerHeight"/> below the keyboard. The window grows by that much when a drawer
/// opens, so the keyboard keeps its size and place; with no drawer open there is no gap at all.
/// </summary>
public sealed class BoardRowsPanel : Panel
{
    public const double PinnedWeight = 0.7;
    /// <summary>A drawer row is this fraction of a keyboard row's height.</summary>
    public const double DrawerRowWeight = 0.9;

    public static readonly DependencyProperty DrawerHeightProperty = DependencyProperty.Register(
        nameof(DrawerHeight), typeof(double), typeof(BoardRowsPanel),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

    /// <summary>Height given to the drawer rows (0 while no drawer is open), in layout units.</summary>
    public double DrawerHeight
    {
        get => (double)GetValue(DrawerHeightProperty);
        set => SetValue(DrawerHeightProperty, value);
    }

    private static string? NameOf(UIElement child) => (child as FrameworkElement)?.DataContext is RowView r ? r.Name : null;
    private static bool IsDrawer(UIElement child) => NameOf(child)?.StartsWith(CommunicationSession.DrawerRowPrefix, StringComparison.Ordinal) == true;
    private static double Weight(UIElement child) => NameOf(child) switch
    {
        CommunicationSession.PinnedRow => PinnedWeight,
        CommunicationSession.ItchViewRow => ItchViewWeight,
        CommunicationSession.ItchBarRow => 0.55,
        _ => 1.0,
    };

    /// <summary>The Itch figure's share in key rows (it also gets the extra height the window grows by).</summary>
    public const double ItchViewWeight = 5.0;

    private static bool IsItchView(UIElement child) => NameOf(child) == CommunicationSession.ItchViewRow;

    private double KeyboardWeight() => InternalChildren.Cast<UIElement>().Where(c => !IsDrawer(c)).Sum(Weight);

    /// <summary>The height of one keyboard-row unit for a given panel height and drawer height.</summary>
    public double UnitFor(double panelHeight, double drawerHeight) => Math.Max(0, panelHeight - drawerHeight) / Math.Max(0.1, KeyboardWeight());

    /// <summary>
    /// The keyboard row unit from the last layout pass. It does not change when a drawer opens or closes
    /// (the window grows or shrinks by exactly the drawer), so it is safe to read even mid-change.
    /// </summary>
    public double LastKeyboardUnit { get; private set; }

    private (double Unit, double DrawerRow, double ItchExtra) Plan(double height)
    {
        var drawer = Math.Clamp(DrawerHeight, 0, height);
        var drawerRows = InternalChildren.Cast<UIElement>().Count(IsDrawer);
        var itch = InternalChildren.Cast<UIElement>().Any(IsItchView);
        // With Itch open the extra height goes to the figure; with a drawer open, to the drawer rows.
        return (UnitFor(height, drawer), drawerRows == 0 ? 0 : drawer / CommunicationSession.DrawerRows, itch ? drawer : 0);
    }

    protected override Size MeasureOverride(Size available)
    {
        var (unit, drawerRow, itchExtra) = double.IsInfinity(available.Height) ? (double.PositiveInfinity, double.PositiveInfinity, 0) : Plan(available.Height);
        foreach (UIElement child in InternalChildren)
            child.Measure(new Size(available.Width, IsDrawer(child) ? drawerRow : Weight(child) * unit + (IsItchView(child) ? itchExtra : 0)));
        return new Size(double.IsInfinity(available.Width) ? 0 : available.Width, double.IsInfinity(available.Height) ? 0 : available.Height);
    }

    protected override Size ArrangeOverride(Size final)
    {
        var (unit, drawerRow, itchExtra) = Plan(final.Height);
        LastKeyboardUnit = unit;
        var y = 0.0;
        foreach (UIElement child in InternalChildren.Cast<UIElement>().Where(c => !IsDrawer(c)))
        {
            var h = Weight(child) * unit + (IsItchView(child) ? itchExtra : 0);
            child.Arrange(new Rect(0, y, final.Width, h));
            y += h;
        }
        foreach (UIElement child in InternalChildren.Cast<UIElement>().Where(IsDrawer))
        {
            child.Arrange(new Rect(0, y, final.Width, drawerRow));
            y += drawerRow;
        }
        return final;
    }
}

/// <summary>
/// Lays out a row's cells side by side by their key units (<see cref="Aac.Core.Board.Cell.Span"/> wide,
/// <see cref="Aac.Core.Board.Cell.Gap"/> empty units before). A row with fixed
/// <see cref="RowView.Units"/> (the keyboard) keeps one key size across rows; others fill the width.
/// </summary>
public sealed class BoardCellsPanel : Panel
{
    private static Aac.Core.Board.Cell? CellOf(UIElement child) => child is FrameworkElement { DataContext: CellView v } ? v.Cell : null;

    private (double Unit, List<(double X, double W)> Slots) Plan(double width)
    {
        var cells = InternalChildren.Cast<UIElement>().Select(CellOf).ToList();
        var slots = new List<(double, double)>();
        var x = 0.0;
        foreach (var c in cells)
        {
            x += c?.Gap ?? 0;
            var w = Math.Max(0.1, c?.Span ?? 1);
            slots.Add((x, w));
            x += w;
        }
        var total = Math.Max(x, (DataContext as RowView)?.Units ?? x);
        return (total <= 0 || double.IsInfinity(width) ? 0 : width / total, slots);
    }

    protected override Size MeasureOverride(Size available)
    {
        var (unit, slots) = Plan(available.Width);
        var height = 0.0;
        var i = 0;
        foreach (UIElement child in InternalChildren)
        {
            var w = unit == 0 ? double.PositiveInfinity : slots[i].W * unit;
            child.Measure(new Size(w, available.Height));
            height = Math.Max(height, child.DesiredSize.Height);
            i++;
        }
        return new Size(double.IsInfinity(available.Width) ? 0 : available.Width,
                        double.IsInfinity(available.Height) ? height : available.Height);
    }

    protected override Size ArrangeOverride(Size final)
    {
        var (unit, slots) = Plan(final.Width);
        var i = 0;
        foreach (UIElement child in InternalChildren)
        {
            var (x, w) = slots[i++];
            child.Arrange(new Rect(x * unit, 0, w * unit, final.Height));
        }
        return final;
    }
}

/// <summary>True when the bound value is a non-empty string (used to switch a cell to its icon layout).</summary>
public sealed class IsSetConverter : System.Windows.Data.IValueConverter
{
    public static readonly IsSetConverter Instance = new();
    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => value is string { Length: > 0 };
    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Visible when the bound value is true, collapsed otherwise.</summary>
public sealed class VisibleIfTrue : System.Windows.Data.IValueConverter
{
    public static readonly VisibleIfTrue Instance = new();
    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Draws the Itch screen's view row as the 3D figure and every other row as its buttons.</summary>
public sealed class RowTemplateSelector : System.Windows.Controls.DataTemplateSelector
{
    public DataTemplate? Buttons { get; set; }
    public DataTemplate? ItchFigure { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container) =>
        item is RowView { Name: CommunicationSession.ItchViewRow } ? ItchFigure : Buttons;
}
