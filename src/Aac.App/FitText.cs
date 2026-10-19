using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Aac.App;

/// <summary>
/// Draws a button's label as large as fits its space, centred both ways. Multi-word labels wrap; a single
/// word stays on one line. Short labels are capped at about 58% of the height so letters do not look
/// bloated; long phrases shrink down to a readable minimum and are cut with "…" only if they still do not
/// fit in four lines. An optional hint line is drawn smaller underneath. The size is found in the layout
/// pass from the space actually given (never from a previous layout), and results are cached per text and
/// size, so reused buttons and unchanged keys are not measured again.
/// </summary>
public sealed class FitText : FrameworkElement
{
    private const double MinFont = 12;
    private const double CapOfHeight = 0.58;
    private const int MaxLines = 4;
    private static readonly Dictionary<(string, string?, int, int, bool), double> Cache = new();

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string), typeof(FitText),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure));
    public static readonly DependencyProperty HintProperty = DependencyProperty.Register(nameof(Hint), typeof(string), typeof(FitText),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure));
    public static readonly DependencyProperty ForegroundProperty = System.Windows.Documents.TextElement.ForegroundProperty.AddOwner(typeof(FitText),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty FontWeightProperty = System.Windows.Documents.TextElement.FontWeightProperty.AddOwner(typeof(FitText),
        new FrameworkPropertyMetadata(FontWeights.Normal, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty FontFamilyProperty = System.Windows.Documents.TextElement.FontFamilyProperty.AddOwner(typeof(FitText),
        new FrameworkPropertyMetadata(SystemFonts.MessageFontFamily, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public string? Hint { get => (string?)GetValue(HintProperty); set => SetValue(HintProperty, value); }
    public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    public FontWeight FontWeight { get => (FontWeight)GetValue(FontWeightProperty); set => SetValue(FontWeightProperty, value); }
    public FontFamily FontFamily { get => (FontFamily)GetValue(FontFamilyProperty); set => SetValue(FontFamilyProperty, value); }

    public FitText() => ClipToBounds = true; // never draw into a neighbouring button

    private static readonly Brush HintBrush = new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xC8));

    // Measuring wants all the space it is offered: the text is drawn into the final arranged size.
    protected override Size MeasureOverride(Size available) => new(
        double.IsInfinity(available.Width) ? 0 : available.Width,
        double.IsInfinity(available.Height) ? 0 : available.Height);

    private Typeface Face => new(FontFamily, FontStyles.Normal, FontWeight, FontStretches.Normal);
    private double Dip => VisualTreeHelper.GetDpi(this).PixelsPerDip;

    private FormattedText Format(string text, double size, double maxWidth, Brush brush, bool wrap)
    {
        // Wrapped text is centred line by line inside the wrap width; a single line is left-aligned here and
        // centred when it is drawn (centre alignment without a wrap width would shift it left by half its width).
        var ft = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Face, size, brush, Dip)
        {
            TextAlignment = wrap ? TextAlignment.Center : TextAlignment.Left,
        };
        if (wrap) ft.MaxTextWidth = Math.Max(1, maxWidth);
        return ft;
    }

    /// <summary>Largest font size (binary search) at which the text fits the box; 0 if even the minimum does not.</summary>
    private double FitSize(string text, double width, double height, bool wrap)
    {
        var key = (text, FontWeight.ToString(), (int)width, (int)height, wrap);
        if (Cache.TryGetValue(key, out var cached)) return cached;
        var cap = height * CapOfHeight;
        double lo = MinFont, hi = Math.Max(MinFont, cap), best = 0;
        for (var i = 0; i < 12 && hi - lo > 0.5; i++)
        {
            var mid = (lo + hi) / 2;
            var ft = Format(text, mid, width, Foreground, wrap);
            var fits = ft.Height <= height && ft.WidthIncludingTrailingWhitespace <= width + 0.5 && (!wrap || ft.Height <= mid * 1.4 * MaxLines)
                && (!wrap || LongestWordFits(text, mid, width)); // never split or cut a word to make it fit
            if (fits) { best = mid; lo = mid; } else hi = mid;
        }
        if (best == 0)
        {
            var ft = Format(text, MinFont, width, Foreground, wrap);
            if (ft.Height <= height && ft.WidthIncludingTrailingWhitespace <= width + 0.5) best = MinFont;
        }
        if (Cache.Count > 4000) Cache.Clear();
        Cache[key] = best;
        return best;
    }

    private bool LongestWordFits(string text, double size, double width) =>
        text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .All(word => Format(word, size, width, Foreground, wrap: false).WidthIncludingTrailingWhitespace <= width + 0.5);

    protected override void OnRender(DrawingContext dc)
    {
        var full = ActualWidth;
        var w = full * 0.92; // a little room at the sides
        var h = ActualHeight;
        var text = Text ?? "";
        if (w < 4 || h < 4 || text.Length == 0) return;

        // Short one-word labels never wrap; anything with spaces may.
        var wrap = text.Contains(' ');
        var hasHint = !string.IsNullOrEmpty(Hint);
        var labelHeight = hasHint ? h * 0.72 : h;
        var size = FitSize(text, w, labelHeight, wrap);

        FormattedText label;
        if (size > 0) label = Format(text, size, w, Foreground, wrap);
        else
        {
            // Does not fit even at the minimum: show what fits, ending in "…" (the full text is in the tooltip).
            label = Format(text, MinFont, w, Foreground, wrap: true);
            label.MaxTextHeight = Math.Max(MinFont * 1.3, labelHeight);
            label.Trimming = TextTrimming.CharacterEllipsis;
            size = MinFont;
        }

        FormattedText? hint = null;
        if (hasHint)
        {
            hint = Format(Hint!, Math.Max(MinFont * 0.85, size * 0.5), w, HintBrush, wrap: true);
            hint.MaxTextHeight = h - Math.Min(label.Height, labelHeight);
            hint.Trimming = TextTrimming.CharacterEllipsis;
        }

        // Centre the visible ink, not the line box (which has room for descenders and so sits text high).
        var y = hint is null
            ? h / 2 - (label.Height + label.OverhangAfter - label.Extent / 2) // ink bottom = Height + OverhangAfter
            : Math.Max(0, (h - label.Height - hint.Height) / 2);
        // With a wrap width, FormattedText centres each line inside it; a single unwrapped line is centred here.
        var left = (full - w) / 2;
        dc.DrawText(label, new Point(label.MaxTextWidth > 0 ? left : (full - label.Width) / 2, y));
        if (hint is not null) dc.DrawText(hint, new Point(left, y + label.Height));
    }
}
