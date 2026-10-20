using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Aac.Core.Itch;
using Aac.Core.Session;

namespace Aac.App;

/// <summary>
/// The Itch panel's 3D figure: a realistic body model (Blender Studio's CC0 base mesh). A clicked point is
/// named by the nearest of <see cref="Mannequin.Parts"/>. The session holds the view (turn, tilt, zoom, the
/// point it is centred on) and the marked spot; this view glides smoothly to whatever the session says.
/// With the mouse: a left click marks the spot, a left drag turns and tilts the figure, a right drag moves
/// it, and the wheel zooms in on whatever is under the pointer.
/// </summary>
public sealed class ItchView : Grid
{
    /// <summary>Height to fit in the view at zoom 1: the whole figure plus a margin.</summary>
    private const double WholeBodyHeight = 1.95;
    private const double WholeBodyWidth = 0.8;
    private const double DragThreshold = 5;        // pixels before a left press counts as a drag, not a click
    private const double TurnDegreesPerPixel = 0.5; // at zoom 1; slower the closer the view is
    private const double TiltDegreesPerPixel = 0.25;
    /// <summary>How quickly the view catches up with a change (seconds to cover about two thirds of it).</summary>
    private const double GlideSeconds = 0.07;

    private readonly Viewport3D _viewport = new() { ClipToBounds = true };
    private readonly PerspectiveCamera _camera = new() { FieldOfView = 34, NearPlaneDistance = 0.02, FarPlaneDistance = 20 };
    private readonly HashSet<Model3D> _bodyModels = new();
    private readonly GeometryModel3D _marker;
    private readonly ModelVisual3D _markerVisual = new();
    /// <summary>
    /// The controller icon, discreetly in the figure's upper corner on the side the controls panel is: it shows or
    /// hides the controls (a button for every movement), and is lit while they are shown.
    /// </summary>
    private readonly Button _controls = new()
    {
        Content = new TextBlock { Text = "🎮", FontFamily = new FontFamily("Segoe UI Emoji, Segoe UI Symbol"), FontSize = 26 },
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Top,
        Margin = new Thickness(8),
        Padding = new Thickness(8, 4, 8, 4),
        Foreground = Brushes.White,
        Background = new SolidColorBrush(Color.FromArgb(0x70, 0x20, 0x20, 0x20)),
        BorderBrush = new SolidColorBrush(Color.FromArgb(0x90, 0x8D, 0xB8, 0xEA)),
        BorderThickness = new Thickness(1),
        Opacity = 0.75,
        Focusable = false,
        Cursor = Cursors.Hand,
        // No tooltip: little pop-ups get in the way when the pointer rests on a button.
    };
    private CommunicationSession? _session;

    // The view as drawn right now; it glides toward the session's view.
    private double _yaw, _pitch, _zoom = 1;
    private Vector3D _target = new(CommunicationSession.ItchHome.X, CommunicationSession.ItchHome.Y, CommunicationSession.ItchHome.Z);
    private bool _gliding;
    private TimeSpan _lastFrame;
    private readonly System.Diagnostics.Stopwatch _frameClock = new();
    private int _frameNo;

    private Point? _pressedAt;
    private MouseButton _pressedButton;
    private double _dragged;

    public ItchView()
    {
        var made = System.Diagnostics.Stopwatch.StartNew();
        Loaded += (_, _) =>
        {
            if (!PerfLog.Enabled) return;
            PerfLog.Set("render.tier (2 = graphics card)", RenderCapability.Tier >> 16);
            PerfLog.Record("itch.toLoaded", made);
            EventHandler? first = null;
            first = (_, _) => { CompositionTarget.Rendering -= first; PerfLog.Record("itch.toFirstFrame", made); };
            CompositionTarget.Rendering += first;
        };
        Background = new SolidColorBrush(Color.FromRgb(0x14, 0x1A, 0x22));
        Margin = new Thickness(5);
        _viewport.Camera = _camera;

        var group = new Model3DGroup();
        group.Children.Add(new AmbientLight(Color.FromRgb(0x58, 0x58, 0x58)));
        group.Children.Add(new DirectionalLight(Color.FromRgb(0xA0, 0xA0, 0xA0), new Vector3D(-0.4, -0.5, -1)));
        group.Children.Add(new DirectionalLight(Color.FromRgb(0x48, 0x48, 0x50), new Vector3D(0.6, -0.1, 0.8)));
        group.Children.Add(new DirectionalLight(Color.FromRgb(0x30, 0x30, 0x30), new Vector3D(0, 0.3, 1)));
        if (Environment.GetEnvironmentVariable("BETTERTALK_ITCH_REGIONS") == "1") AddRegionModels(group);
        else
        {
            AddModel(group, BodyMesh.Value, Color.FromRgb(0xE0, 0xB8, 0x9A), 25);
            // Shorts: dark green with a gold waistband and gold side stripes.
            var (green, gold) = Shorts.Value;
            AddModel(group, green, Color.FromRgb(0x20, 0x37, 0x31), 12);
            AddModel(group, gold, Color.FromRgb(0xFF, 0xB6, 0x12), 12);
        }
        _viewport.Children.Add(new ModelVisual3D { Content = group });
        PerfLog.Record("itch.build", made);

        _marker = new GeometryModel3D(Ellipsoid(new Vec(0, 0, 0), new Vec(1, 1, 1)), new DiffuseMaterial(Brushes.Red));
        _viewport.Children.Add(_markerVisual);

        Children.Add(_viewport);

        _controls.Click += (_, _) => _session?.ToggleItchControls();
        Children.Add(_controls);

        MouseLeftButtonDown += (_, e) => Press(e, MouseButton.Left);
        MouseRightButtonDown += (_, e) => Press(e, MouseButton.Right);
        MouseMove += OnDrag;
        MouseLeftButtonUp += OnRelease;
        MouseRightButtonUp += OnRelease;
        MouseWheel += OnWheel;
        LostMouseCapture += (_, _) => _pressedAt = null;
        SizeChanged += (_, _) => Refresh();
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
    }

    private void AddModel(Model3DGroup group, MeshGeometry3D mesh, Color colour, double shine)
    {
        var material = new MaterialGroup();
        material.Children.Add(new DiffuseMaterial(new SolidColorBrush(colour)));
        material.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromRgb(0x2A, 0x26, 0x22)), shine));
        material.Freeze();
        var model = new GeometryModel3D(mesh, material) { BackMaterial = material };
        group.Children.Add(model);
        _bodyModels.Add(model);
    }

    /// <summary>
    /// A checking aid (BETTERTALK_ITCH_REGIONS=1): colours the body by the words a click there would give,
    /// so the borders between regions (inner and outer thigh, each finger) can be seen at a glance.
    /// </summary>
    private void AddRegionModels(Model3DGroup group)
    {
        var body = BodyMesh.Value;
        var meshes = new Dictionary<string, MeshGeometry3D>();
        var idx = body.TriangleIndices;
        for (var t = 0; t < idx.Count; t += 3)
        {
            var (a, b, c) = (body.Positions[idx[t]], body.Positions[idx[t + 1]], body.Positions[idx[t + 2]]);
            var p = new Vec((a.X + b.X + c.X) / 3, (a.Y + b.Y + c.Y) / 3, (a.Z + b.Z + c.Z) / 3);
            var words = ItchDescriber.Where(Mannequin.Nearest(p), p);
            if (!meshes.TryGetValue(words, out var mesh)) meshes[words] = mesh = new MeshGeometry3D();
            foreach (var i in new[] { idx[t], idx[t + 1], idx[t + 2] })
            {
                mesh.Positions.Add(body.Positions[i]);
                mesh.Normals.Add(body.Normals[i]);
                mesh.TriangleIndices.Add(mesh.Positions.Count - 1);
            }
        }
        foreach (var (words, mesh) in meshes)
        {
            var hash = (uint)StringComparer.Ordinal.GetHashCode(words);
            var colour = Color.FromRgb((byte)(60 + hash % 196), (byte)(60 + (hash >> 8) % 196), (byte)(60 + (hash >> 16) % 196));
            AddModel(group, mesh, colour, 40);
        }
    }

    private void Attach()
    {
        _session = Window.GetWindow(this)?.DataContext as CommunicationSession;
        if (_session is null) return;
        _session.PropertyChanged += OnSessionChanged;
        _session.ItchViewChanged += OnViewChanged;
        // Start where the session is, without gliding in from somewhere else.
        (_yaw, _pitch, _zoom) = (_session.ItchYaw, _session.ItchPitch, _session.ItchZoom);
        _target = ToVector(_session.ItchTarget);
        Refresh();
    }

    private void Detach()
    {
        StopGliding();
        if (_session is null) return;
        _session.PropertyChanged -= OnSessionChanged;
        _session.ItchViewChanged -= OnViewChanged;
        _session = null;
    }

    private void OnSessionChanged(object? sender, PropertyChangedEventArgs e) => Refresh();

    private void OnViewChanged(object? sender, EventArgs e)
    {
        if (_session is not null && _pressedAt is not null && _dragged >= DragThreshold)
        {
            // While dragging, the figure follows the mouse exactly and at once: easing it would make it trail
            // behind the pointer and hitch as the drag starts. (A zoom still in progress keeps gliding.)
            (_yaw, _pitch) = (_session.ItchYaw, _session.ItchPitch);
            _target = ToVector(_session.ItchTarget);
            PlaceCamera();
            if (Math.Abs(_session.ItchZoom / _zoom - 1) < 0.001) return;
        }
        if (_gliding) return;
        _gliding = true;
        _lastFrame = TimeSpan.Zero;
        _frameClock.Restart();
        _frameNo = 0;
        CompositionTarget.Rendering += OnFrame;
    }

    private void StopGliding()
    {
        if (!_gliding) return;
        _gliding = false;
        CompositionTarget.Rendering -= OnFrame;
    }

    /// <summary>Each frame, moves the drawn view part of the way to the session's view, then stops once it is there.</summary>
    private void OnFrame(object? sender, EventArgs e)
    {
        if (_session is null) { StopGliding(); return; }
        var now = e is RenderingEventArgs r ? r.RenderingTime : TimeSpan.Zero;
        if (now == _lastFrame) return; // the same frame reported twice
        _frameNo++;
        if (PerfLog.Enabled) PerfLog.Record(_frameNo <= 3 ? $"itch.frame{_frameNo}" : "itch.frame", _frameClock.Elapsed.TotalMilliseconds);
        _frameClock.Restart();
        var dt = _lastFrame == TimeSpan.Zero ? 1 / 60.0 : Math.Min(0.1, (now - _lastFrame).TotalSeconds);
        _lastFrame = now;
        var k = 1 - Math.Exp(-dt / GlideSeconds);

        var yawGap = ((_session.ItchYaw - _yaw) % 360 + 540) % 360 - 180; // the short way round
        _yaw += yawGap * k;
        _pitch += (_session.ItchPitch - _pitch) * k;
        _zoom = Math.Exp(Math.Log(_zoom) + (Math.Log(_session.ItchZoom) - Math.Log(_zoom)) * k);
        var targetGap = ToVector(_session.ItchTarget) - _target;
        _target += targetGap * k;

        if (Math.Abs(yawGap) < 0.05 && Math.Abs(_session.ItchPitch - _pitch) < 0.05
            && Math.Abs(_session.ItchZoom / _zoom - 1) < 0.001 && targetGap.Length < 0.0005)
        {
            (_yaw, _pitch, _zoom) = (_session.ItchYaw, _session.ItchPitch, _session.ItchZoom);
            _target = ToVector(_session.ItchTarget);
            StopGliding();
        }
        PlaceCamera();
    }

    private void Refresh()
    {
        if (_session is null || ActualWidth <= 0) return;

        // The controller icon sits at the figure's upper corner beside the controls panel; it is lit while the controls show.
        var open = _session.ItchControlsOpen;
        _controls.HorizontalAlignment = ItchViewHost.PanelsOnLeft ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        _controls.Opacity = open ? 1 : 0.7;
        _controls.BorderBrush = open ? new SolidColorBrush(Color.FromRgb(0x8D, 0xB8, 0xEA)) : new SolidColorBrush(Color.FromArgb(0x90, 0x8D, 0xB8, 0xEA));
        _controls.BorderThickness = new Thickness(open ? 3 : 1);
        PlaceCamera();
    }

    private void PlaceCamera()
    {
        if (ActualWidth <= 0) return;
        var look = LookDirection();
        var eye = (Point3D)(_target - look * Distance());
        _camera.Position = eye;
        _camera.LookDirection = look;
        _camera.UpDirection = new Vector3D(0, 1, 0);

        if (_session?.ItchSpot is { } spot)
        {
            // The marker shrinks as the view closes in, so it never hides what is around the spot.
            var size = 0.016 / Math.Sqrt(_zoom);
            var marker = new Transform3DGroup();
            marker.Children.Add(new ScaleTransform3D(size, size, size));
            marker.Children.Add(new TranslateTransform3D(spot.Point.X, spot.Point.Y, spot.Point.Z));
            _marker.Transform = marker;
            _markerVisual.Content = _marker;
        }
        else _markerVisual.Content = null;
    }

    private Vector3D LookDirection()
    {
        var yaw = _yaw * Math.PI / 180;
        var pitch = _pitch * Math.PI / 180;
        return new Vector3D(-Math.Sin(yaw) * Math.Cos(pitch), -Math.Sin(pitch), -Math.Cos(yaw) * Math.Cos(pitch));
    }

    private double Distance() => FitDistance() / _zoom;

    private static Vector3D ToVector(Vec v) => new(v.X, v.Y, v.Z);

    /// <summary>
    /// How far away the camera must be for the whole figure to fit. WPF's field of view is horizontal,
    /// so the vertical angle depends on the view's shape (this view is usually wide and short).
    /// </summary>
    private double FitDistance()
    {
        var halfH = _camera.FieldOfView / 2 * Math.PI / 180;
        var aspect = Math.Max(0.1, ActualWidth / Math.Max(1, ActualHeight));
        var halfV = Math.Atan(Math.Tan(halfH) / aspect);
        return Math.Max(WholeBodyHeight / 2 / Math.Tan(halfV), WholeBodyWidth / 2 / Math.Tan(halfH));
    }

    /// <summary>How many metres one pixel covers at the depth of the view's centre (at the zoom the session wants).</summary>
    private double MetresPerPixel()
    {
        var halfH = _camera.FieldOfView / 2 * Math.PI / 180;
        var distance = FitDistance() / (_session?.ItchZoom ?? 1);
        return 2 * distance * Math.Tan(halfH) / Math.Max(1, ActualWidth);
    }

    // ---------- mouse ----------

    private void Press(MouseButtonEventArgs e, MouseButton button)
    {
        if (_pressedAt is not null) return; // the other button is already held
        _pressedAt = e.GetPosition(this);
        _pressedButton = button;
        _dragged = 0;
        CaptureMouse();
        e.Handled = true;
    }

    /// <summary>
    /// A left drag turns the figure (side to side) and tilts it (up and down); a right drag moves it, the
    /// body following the pointer exactly. Both get gentler as the view closes in, so small details are
    /// easy to reach.
    /// </summary>
    private void OnDrag(object sender, MouseEventArgs e)
    {
        if (_session is null || _pressedAt is not { } from) return;
        var now = e.GetPosition(this);
        var (dx, dy) = (now.X - from.X, now.Y - from.Y);
        if (_dragged < DragThreshold)
        {
            // A right press only ever moves the figure, so it starts at once. A left press could still be a
            // click, so it waits for a few pixels and then starts from there, without jumping.
            if (_pressedButton == MouseButton.Right) _dragged = DragThreshold;
            else
            {
                _dragged = Math.Max(_dragged, Math.Abs(dx) + Math.Abs(dy)); // how far from where it was pressed
                if (_dragged >= DragThreshold) _pressedAt = now;
                return;
            }
        }
        _pressedAt = now;
        if (_pressedButton == MouseButton.Right)
        {
            var metres = MetresPerPixel();
            _session.PanItch(-dx * metres, dy * metres);
        }
        else
        {
            var gentle = 1 / Math.Sqrt(_session.ItchZoom);
            _session.TurnItch(-dx * TurnDegreesPerPixel * gentle);
            _session.TiltItch(dy * TiltDegreesPerPixel * gentle);
        }
    }

    private void OnRelease(object sender, MouseButtonEventArgs e)
    {
        if (_pressedAt is null || e.ChangedButton != _pressedButton) return;
        var wasClick = _dragged < DragThreshold && _pressedButton == MouseButton.Left;
        _pressedAt = null;
        ReleaseMouseCapture();
        e.Handled = true;
        if (_session is null || !wasClick) return;
        if (Pick(e.GetPosition(this)) is { } hit) _session.MarkItch(hit.PartId, hit.Point);
    }

    /// <summary>The wheel zooms in on whatever is under the pointer, which stays put while the rest grows around it.</summary>
    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        if (_session is null || ActualWidth <= 0) return;
        var steps = Math.Sign(e.Delta);
        if (steps == 0) return;
        _session.ZoomItch(steps, PointAtViewDepth(e.GetPosition(this)));
    }

    /// <summary>The point under the pointer, at the depth of the view's centre.</summary>
    private Vec PointAtViewDepth(Point at)
    {
        var look = LookDirection();
        var right = Vector3D.CrossProduct(look, new Vector3D(0, 1, 0));
        right.Normalize();
        var up = Vector3D.CrossProduct(right, look);
        var tanH = Math.Tan(_camera.FieldOfView / 2 * Math.PI / 180);
        var tanV = tanH * ActualHeight / Math.Max(1, ActualWidth);
        var nx = 2 * at.X / Math.Max(1, ActualWidth) - 1;
        var ny = 1 - 2 * at.Y / Math.Max(1, ActualHeight);
        var distance = Distance();
        var p = _target + right * (nx * tanH * distance) + up * (ny * tanV * distance);
        return new Vec(p.X, p.Y, p.Z);
    }

    private (string PartId, Vec Point)? Pick(Point at)
    {
        (string, Vec)? found = null;
        VisualTreeHelper.HitTest(_viewport, null, result =>
        {
            if (result is RayMeshGeometry3DHitTestResult mesh && _bodyModels.Contains(mesh.ModelHit))
            {
                var p = new Vec(mesh.PointHit.X, mesh.PointHit.Y, mesh.PointHit.Z);
                found = (Mannequin.Nearest(p).Id, p);
                return HitTestResultBehavior.Stop;
            }
            return HitTestResultBehavior.Continue;
        }, new PointHitTestParameters(at));
        return found;
    }

    // ---------- meshes ----------

    /// <summary>
    /// Reads the body model and cuts the shorts (a few hundred milliseconds) on a background thread, so the
    /// first time Itch opens it does not wait for them. Safe to call more than once.
    /// </summary>
    public static Task Preload() => Task.Run(() => { _ = BodyMesh.Value; _ = Shorts.Value; });

    /// <summary>The body, read once from the embedded OBJ (positions, normals, triangles; y up, facing +z).</summary>
    private static readonly Lazy<MeshGeometry3D> BodyMesh = new(LoadBody);

    /// <summary>
    /// A pair of shorts cut from the body itself: the skin between the waist and mid-thigh (arms and hands
    /// left out), pushed out a few millimetres so it sits on the body like cloth. Triangles are cut exactly
    /// along the hem, the waistband and the stripes, so the edges are straight. Returns the green cloth and
    /// the gold trim (waistband and a stripe down the outside of each leg) as separate meshes.
    /// </summary>
    private static readonly Lazy<(MeshGeometry3D Green, MeshGeometry3D Gold)> Shorts = new(() =>
    {
        const double Waist = 0.985, Hem = 0.6, Band = 0.957, Thickness = 0.007;
        var body = BodyMesh.Value;
        var green = new MeshGeometry3D();
        var gold = new MeshGeometry3D();
        var idx = body.TriangleIndices;
        for (var t = 0; t < idx.Count; t += 3)
        {
            var tri = new List<(Point3D P, Vector3D N)>(3);
            for (var k = 0; k < 3; k++) tri.Add((body.Positions[idx[t + k]], body.Normals[idx[t + k]]));
            if (tri.All(v => v.P.Y > Waist) || tri.All(v => v.P.Y < Hem) || tri.Average(v => Math.Abs(v.P.X)) > 0.27) continue;
            var inside = Split(Split(tri, v => v.P.Y - Hem).Above, v => Waist - v.P.Y).Above;
            var (band, below) = Split(inside, v => v.P.Y - Band);
            var (stripe, cloth) = Split(below, v => Stripe(v.P));
            Add(gold, band, Thickness);
            Add(gold, stripe, Thickness);
            Add(green, cloth, Thickness);
        }
        green.Freeze();
        gold.Freeze();
        return (green, gold);
    });

    /// <summary>Positive on the gold stripe down the outside of each hip and leg, negative elsewhere.</summary>
    private static double Stripe(Point3D p)
    {
        const double HalfWidth = 0.2; // radians around the leg or hips
        var x = Math.Abs(p.X);
        double cx, cz, sx, sz;
        if (p.Y < 0.84)
        {
            // Around the thigh's centre line (it leans out and back a little going down).
            var k = Math.Clamp((0.86 - p.Y) / 0.37, 0, 1);
            (cx, cz, sx, sz) = (0.11 + 0.03 * k, -0.02 - 0.025 * k, 1, 1);
        }
        else (cx, cz, sx, sz) = (0, -0.025, 0.18, 0.12); // around the hips
        var angle = Math.Atan2((p.Z - cz) / sz, (x - cx) / sx);
        return Math.Cos(angle) - Math.Cos(HalfWidth);
    }

    /// <summary>Splits a convex polygon where f changes sign, interpolating positions and normals.</summary>
    private static (List<(Point3D P, Vector3D N)> Above, List<(Point3D P, Vector3D N)> Below) Split(
        List<(Point3D P, Vector3D N)> poly, Func<(Point3D P, Vector3D N), double> f)
    {
        var above = new List<(Point3D, Vector3D)>();
        var below = new List<(Point3D, Vector3D)>();
        for (var i = 0; i < poly.Count; i++)
        {
            var (a, b) = (poly[i], poly[(i + 1) % poly.Count]);
            var (fa, fb) = (f(a), f(b));
            (fa >= 0 ? above : below).Add(a);
            if (fa >= 0 != fb >= 0)
            {
                var k = fa / (fa - fb);
                var cut = (a.P + (b.P - a.P) * k, a.N + (b.N - a.N) * k);
                above.Add(cut);
                below.Add(cut);
            }
        }
        return (above, below);
    }

    private static void Add(MeshGeometry3D mesh, List<(Point3D P, Vector3D N)> poly, double offset)
    {
        if (poly.Count < 3) return;
        var first = mesh.Positions.Count;
        foreach (var (p, n) in poly)
        {
            var unit = n;
            unit.Normalize();
            mesh.Positions.Add(p + unit * offset);
            mesh.Normals.Add(unit);
        }
        for (var i = 1; i < poly.Count - 1; i++)
        {
            mesh.TriangleIndices.Add(first);
            mesh.TriangleIndices.Add(first + i);
            mesh.TriangleIndices.Add(first + i + 1);
        }
    }

    private static MeshGeometry3D LoadBody()
    {
        using var stream = typeof(ItchView).Assembly.GetManifestResourceStream("body.obj.gz")
            ?? throw new InvalidOperationException("The body model is missing from the app.");
        using var reader = new System.IO.StreamReader(new System.IO.Compression.GZipStream(stream, System.IO.Compression.CompressionMode.Decompress));
        var positions = new Point3DCollection(43000);
        var normals = new Vector3DCollection(43000);
        var indices = new Int32Collection(260000);
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        static double D(ReadOnlySpan<char> s) => double.Parse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture);
        Span<Range> f = stackalloc Range[5];
        while (reader.ReadLine() is { } line)
        {
            var span = line.AsSpan();
            if (span.Length < 3) continue;
            if (span.StartsWith("v "))
            {
                span.Split(f, ' ', StringSplitOptions.RemoveEmptyEntries);
                positions.Add(new Point3D(D(span[f[1]]), D(span[f[2]]), D(span[f[3]])));
            }
            else if (span.StartsWith("vn "))
            {
                span.Split(f, ' ', StringSplitOptions.RemoveEmptyEntries);
                normals.Add(new Vector3D(D(span[f[1]]), D(span[f[2]]), D(span[f[3]])));
            }
            else if (span.StartsWith("f "))
            {
                span.Split(f, ' ', StringSplitOptions.RemoveEmptyEntries);
                for (var k = 1; k <= 3; k++)
                {
                    var v = span[f[k]];
                    var slash = v.IndexOf('/');
                    indices.Add(int.Parse(slash < 0 ? v : v[..slash], inv) - 1);
                }
            }
        }
        var mesh = new MeshGeometry3D { Positions = positions, Normals = normals, TriangleIndices = indices };
        mesh.Freeze();
        return mesh;
    }

    private static MeshGeometry3D Ellipsoid(Vec c, Vec r, int slices = 28, int stacks = 18)
    {
        var mesh = new MeshGeometry3D();
        for (var i = 0; i <= stacks; i++)
        {
            var phi = Math.PI * i / stacks;
            for (var j = 0; j <= slices; j++)
            {
                var theta = 2 * Math.PI * j / slices;
                var n = new Vector3D(Math.Sin(phi) * Math.Sin(theta), Math.Cos(phi), Math.Sin(phi) * Math.Cos(theta));
                mesh.Positions.Add(new Point3D(c.X + r.X * n.X, c.Y + r.Y * n.Y, c.Z + r.Z * n.Z));
                mesh.Normals.Add(new Vector3D(n.X / r.X, n.Y / r.Y, n.Z / r.Z));
            }
        }
        Stitch(mesh, stacks, slices);
        return mesh;
    }

    private static void Stitch(MeshGeometry3D mesh, int rows, int slices)
    {
        for (var i = 0; i < rows; i++)
        {
            for (var j = 0; j < slices; j++)
            {
                var a = i * (slices + 1) + j;
                var b = a + slices + 1;
                mesh.TriangleIndices.Add(a); mesh.TriangleIndices.Add(b); mesh.TriangleIndices.Add(a + 1);
                mesh.TriangleIndices.Add(a + 1); mesh.TriangleIndices.Add(b); mesh.TriangleIndices.Add(b + 1);
            }
        }
        mesh.Freeze();
    }
}

/// <summary>
/// Holds the one Itch figure view. The view (and its 3D model, already on the graphics card) is made once and
/// kept, so opening Itch again only puts it back on screen instead of building it all over.
/// </summary>
public sealed class ItchViewHost : ContentControl
{
    private static ItchView? _shared;

    /// <summary>True when the controls panel sits to the left of the figure (a window anchored at the right of the screen).</summary>
    public static bool PanelsOnLeft { get; set; }

    /// <summary>The figure view, made the first time it is needed (or ahead of time by <see cref="Prepare"/>).</summary>
    public static ItchView Shared => _shared ??= new ItchView();

    /// <summary>Makes the figure view ahead of time (call on the UI thread once the model is preloaded).</summary>
    public static void Prepare() => _ = Shared;

    public ItchViewHost()
    {
        Focusable = false;
        Loaded += (_, _) =>
        {
            // A view can only be in one place: take it from wherever it was last shown.
            if (Shared.Parent is ContentControl previous && previous != this) previous.Content = null;
            Content = Shared;
        };
        Unloaded += (_, _) => { if (Content == Shared) Content = null; };
    }
}
