namespace Aac.Core.Layout;

/// <summary>The parts of the Itch screen that can be pulled out of the main window and put anywhere ("Arrange the panels").</summary>
public enum PanelKind
{
    /// <summary>The 3D figure on its own.</summary>
    Figure,
    /// <summary>The controls: a button for every way of moving the figure ("Move the figure"), beside it.</summary>
    Move,
}

/// <summary>Where one panel is and how big it is. Everything false or null means "as it comes".</summary>
public sealed class PanelPlacement
{
    /// <summary>True when the panel floats in a window of its own instead of sitting beside the keyboard.</summary>
    public bool Detached { get; set; }
    /// <summary>Its width beside the keyboard, in board units (the keyboard is 1400 wide); null is the usual width.</summary>
    public double? Width { get; set; }
    /// <summary>The floating window's place and size in screen pixels; null until it has floated.</summary>
    public int? FloatLeft { get; set; }
    public int? FloatTop { get; set; }
    public int? FloatWidth { get; set; }
    public int? FloatHeight { get; set; }
}

/// <summary>
/// How the Itch panels are arranged: beside the keyboard (the usual way, with the window growing for them) or
/// pulled out into windows of their own. Saved in layout.json the moment it changes.
/// </summary>
public sealed class PanelLayout
{
    /// <summary>Beside the keyboard a panel can be this narrow or this wide, in board units.</summary>
    public const double MinWidth = 240, MaxWidth = 1400;
    /// <summary>One press of Narrower or Wider changes a panel's width by this many units.</summary>
    public const double WidthStep = 60;

    public Dictionary<PanelKind, PanelPlacement> Panels { get; set; } = new();

    public PanelPlacement Of(PanelKind kind) => Panels.TryGetValue(kind, out var p) ? p : Panels[kind] = new PanelPlacement();

    public bool IsDetached(PanelKind kind) => Panels.TryGetValue(kind, out var p) && p.Detached;

    /// <summary>The panel's width beside the keyboard, or <paramref name="usual"/> when it has never been changed.</summary>
    public double WidthOf(PanelKind kind, double usual) =>
        Panels.TryGetValue(kind, out var p) && p.Width is { } w ? Math.Clamp(w, MinWidth, MaxWidth) : usual;

    public void SetWidth(PanelKind kind, double units) => Of(kind).Width = Math.Clamp(units, MinWidth, MaxWidth);

    public void SetDetached(PanelKind kind, bool detached) => Of(kind).Detached = detached;

    /// <summary>The floating window's place, once it has been floated (or moved).</summary>
    public PxRect? FloatOf(PanelKind kind) =>
        Panels.TryGetValue(kind, out var p) && p is { FloatLeft: { } l, FloatTop: { } t, FloatWidth: { } w, FloatHeight: { } h } && w > 0 && h > 0
            ? new PxRect(l, t, l + w, t + h) : null;

    public void SetFloat(PanelKind kind, PxRect rect)
    {
        var p = Of(kind);
        (p.FloatLeft, p.FloatTop, p.FloatWidth, p.FloatHeight) = (rect.Left, rect.Top, rect.Width, rect.Height);
    }

    /// <summary>Everything back beside the keyboard at its usual width; the floating places are forgotten too.</summary>
    public void PutEverythingBack() => Panels.Clear();

    public bool IsDefault => Panels.Values.All(p => !p.Detached && p.Width is null);
}
