using System.ComponentModel;
using Aac.Core.Abstractions;
using Aac.Core.Board;
using Aac.Core.Input;
using Aac.Core.Itch;
using Aac.Core.Prediction;
using Aac.Core.Speech;
using Aac.Core.Storage;

namespace Aac.Core.Session;

/// <summary>
/// What is shown in the drawer between the keyboard and the message box. There is only one screen:
/// the keyboard is always there, and these open (one at a time) in a fixed slot below it.
/// </summary>
public enum Drawer
{
    None,
    Quick,
    Phrases,
}

/// <summary>Which editable list a button's text lives in.</summary>
public enum PhraseList
{
    Urgent,
    Quick,
    Category,
    /// <summary>The words of the "Speak out loud" panel beside the Itch figure.</summary>
    Spoken,
}

/// <summary>A request to change a button: <see cref="OldText"/> is null when adding a new one.</summary>
public sealed record PhraseEdit(PhraseList List, string? Category, string? OldText);

public sealed class SessionOptions
{
    public bool QuickPhrasesSpeakImmediately { get; set; } = true;
    public bool ClearMessageAfterSpeaking { get; set; } = true;
    public bool LearnFromSpokenMessages { get; set; } = true;
    /// <summary>The likeliest word goes in the middle of the green row and the rest alternate outward; otherwise left to right.</summary>
    public bool SuggestionsFromMiddle { get; set; } = true;
    public bool WrapNavigation { get; set; }
    /// <summary>After choosing a sentence or phrase, put the cursor on Speak (choosing never speaks).</summary>
    public bool JumpToSpeakAfterChoosing { get; set; } = true;
    /// <summary>Also say each word aloud as soon as it is finished; Speak still reads the whole message.</summary>
    public bool SpeakEachWord { get; set; }
    /// <summary>Repeated clicks on the same on-screen target within this window are ignored.</summary>
    public int PointerDebounceMs { get; set; } = 400;
    /// <summary>Draw the Hide key. On by default, whether or not the window also hides by itself as the mouse leaves.</summary>
    public bool ShowHideKey { get; set; } = true;
    /// <summary>Shorter repeat guard for the on-screen delete keys.</summary>
    public int DeletePointerDebounceMs { get; set; } = 40;
    /// <summary>
    /// The drawer (Quick or Phrases) opens above the board instead of below it: for a window anchored at the
    /// bottom of the screen, so the window grows upward and the keyboard stays where it is. The rows come first
    /// in <see cref="CommunicationSession.Rows"/> then, so moving up from the urgent strip reaches the drawer.
    /// </summary>
    public bool DrawerAbove { get; set; }
    /// <summary>The Itch figure opens on the right of the keyboard (a window anchored at the left), so the Itch button's arrow points right.</summary>
    public bool ItchOpensRight { get; set; } = true;
    /// <summary>Whether the controls panel shows beside the figure; the 🎮 icon changes it and it is remembered.</summary>
    public bool ItchControlsOpen { get; set; } = true;
    /// <summary>The same guard for panel buttons (Itch, Quick, Phrases, Controls): short enough to close and reopen at once, long enough to stop a bounce.</summary>
    public int TogglePointerDebounceMs { get; set; } = 120;

    public static SessionOptions From(AppSettings s) => new()
    {
        QuickPhrasesSpeakImmediately = s.Speech.QuickPhrasesSpeakImmediately,
        ClearMessageAfterSpeaking = s.Speech.ClearMessageAfterSpeaking,
        SpeakEachWord = s.Speech.SpeakEachWord,
        LearnFromSpokenMessages = s.Privacy.LearnFromSpokenMessages,
        SuggestionsFromMiddle = s.SuggestionsFromMiddle,
        WrapNavigation = s.WrapNavigation,
        DrawerAbove = !Layout.Anchoring.IsTop(s.Anchor),
        ItchOpensRight = Layout.Anchoring.IsLeft(s.Anchor),
        ItchControlsOpen = s.ItchControlsOpen,
    };
}

/// <summary>A cell as displayed, with its focus state.</summary>
public sealed class CellView : INotifyPropertyChanged
{
    private bool _isFocused;
    private Cell _cell;
    public CellView(Cell cell) => _cell = cell;

    /// <summary>Updated in place when the same target changes (label, enabled...), so the UI keeps its button.</summary>
    public Cell Cell
    {
        get => _cell;
        set
        {
            if (Equals(_cell, value)) return;
            _cell = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Cell)));
        }
    }
    public bool IsFocused
    {
        get => _isFocused;
        set
        {
            if (_isFocused == value) return;
            _isFocused = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsFocused)));
        }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// A board row. <see cref="Units"/> fixes its width in key units (keyboard rows share one key size); null means
/// the cells fill it. A row with a <see cref="Title"/> has no cells: it is a heading over the rows below it.
/// </summary>
public sealed record RowView(string Name, IReadOnlyList<CellView> Cells, double? Units = null, string? Title = null, string? Subtitle = null)
{
    /// <summary>This row with the Hide key added at its end, or unchanged when <paramref name="hide"/> is null.</summary>
    public RowView WithHide(Cell? hide) => hide is null ? this : this with { Cells = Cells.Append(new CellView(hide)).ToList() };

    public RowGeometry Geometry => RowGeometry.FromCells(Cells.Select(c => (c.Cell.Gap, c.Cell.Span)).ToList(), Units);
}

public enum MessageKind
{
    Typed,
    Sentence,
    Phrase,
    Quick,
}

/// <summary>Per-message measurements. No message text is kept, only counts.</summary>
public sealed record MessageMetric(int Actions, TimeSpan Duration, int Characters, MessageKind Kind);

/// <summary>
/// The whole communication screen as a UI-independent model: board layout, focus, message editing,
/// suggestions and the approve/speak flow. The WPF window only draws this and forwards input.
/// </summary>
public sealed class CommunicationSession : INotifyPropertyChanged
{
    public const string PinnedRow = "pinned";
    /// <summary>Rows named with this prefix belong to the Itch (figure) panel, shown to the right of the keyboard.</summary>
    public const string ItchRowPrefix = "itch-";
    /// <summary>The row the host fills with the 3D mannequin while Itch is open (it has no cells).</summary>
    public const string ItchViewRow = "itch-view";
    /// <summary>The heading over the buttons that move the figure, under the figure itself.</summary>
    public const string ItchMoveTitleRow = "itch-title-move";
    /// <summary>The rows of buttons that move the figure are named with this prefix (slimmer rows than keys).</summary>
    public const string ItchMoveRowPrefix = "itch-move-";
    /// <summary>Ids of the editable Speak out loud words (the strip while Itch is open) start with this; the arrow words are fixed.</summary>
    private const string SpokenWordPrefix = "say-";
    /// <summary>Drawer rows are named with this prefix and a number; they sit below the keyboard.</summary>
    public const string DrawerRowPrefix = "drawer-";
    /// <summary>The drawer slot always holds this many rows of space, so opening or closing it never moves the keyboard.</summary>
    public const int DrawerRows = 3;
    public const string SuggestionsRow = "suggestions";
    public const string FrequentCategory = "★ Most used";
    private const int ForgetConfirmMs = 10_000;

    private static readonly string[][] KeyboardLayout =
    {
        new[] { "q", "w", "e", "r", "t", "y", "u", "i", "o", "p" },
        new[] { "a", "s", "d", "f", "g", "h", "j", "k", "l" },
        new[] { "z", "x", "c", "v", "b", "n", "m", "'", "?" },
    };

    private readonly SpeechController _speech;
    private readonly WordPredictor _predictor;
    private readonly PhraseLibrary _phrases;
    private readonly SessionOptions _options;
    private readonly IClock _clock;
    private readonly DraftStore? _draftStore;
    private readonly SynchronizationContext? _ui;
    private readonly Stack<string> _undo = new();
    private readonly List<MessageMetric> _metrics = new();

    /// <summary>Updated in place by <see cref="Rebuild"/> so unchanged rows and buttons are kept, not recreated.</summary>
    private readonly System.Collections.ObjectModel.ObservableCollection<RowView> _rows = new();
    private string _focusRowName = "";
    private int _focusCol;
    private string? _openCategory;
    private readonly PhraseMemory _memory;
    private DateTimeOffset? _forgetArmedAt;
    /// <summary>The menu button that opened the drawer, so closing it puts the focus back there.</summary>
    private CommandId _drawerOpener;
    private (string Id, DateTimeOffset At)? _lastPointer;
    private int _actionsThisMessage;
    private DateTimeOffset? _messageStartedAt;
    private MessageKind _messageKind = MessageKind.Typed;

    public CommunicationSession(
        SpeechController speech,
        WordPredictor predictor,
        PhraseLibrary phrases,
        SessionOptions options,
        IClock clock,
        DraftStore? draftStore = null,
        PhraseMemory? phraseMemory = null)
    {
        _speech = speech;
        _predictor = predictor;
        _phrases = phrases;
        _phrases.Upgrade();
        _options = options;
        ItchControlsOpen = options.ItchControlsOpen;
        _clock = clock;
        _draftStore = draftStore;
        _memory = phraseMemory ?? new PhraseMemory();
        _ui = SynchronizationContext.Current;
        _speech.StateChanged += (_, _) => OnUi(() => { Rebuild(); RaiseAll(); });
        _speech.ProgressChanged += (_, _) => OnUi(() => SpeechProgress?.Invoke(this, EventArgs.Empty));

        var recovered = _draftStore?.Load();
        if (!string.IsNullOrWhiteSpace(recovered))
        {
            Draft = recovered;
            StatusText = "Recovered your unfinished message.";
            SayFinishedWords(typed: false); // its words count as already said
        }
        Rebuild();
        FocusFirstPageRow();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    /// <summary>Raised when the user asks to hide/show the window (handled by the host app).</summary>
    public event EventHandler? WindowToggleRequested;
    /// <summary>The Settings key was selected: the host opens its Settings window.</summary>
    public event EventHandler? SettingsRequested;
    /// <summary>Raised when what the app has learned changes, so the host can save it.</summary>
    public event EventHandler? LearningChanged;
    /// <summary>In edit mode, pressing a red, orange or phrase button (or "＋ Add") asks the host to edit it.</summary>
    public event EventHandler<PhraseEdit>? EditRequested;
    /// <summary>The button texts were changed; the host saves them.</summary>
    public event EventHandler? PhrasesChanged;

    // ---------- itch ----------

    /// <summary>The Itch panels: a 3D figure the user clicks on to show exactly where the user itches, and spoken directions.</summary>
    public bool ItchOpen { get; private set; }
    /// <summary>How far the figure is turned, in degrees (0 = facing the user, 180 = its back).</summary>
    public double ItchYaw { get; private set; }
    /// <summary>How far the view looks down on the figure (positive) or up at it, in degrees.</summary>
    public double ItchPitch { get; private set; }
    /// <summary>1 = whole body; larger is closer.</summary>
    public double ItchZoom { get; private set; } = 1;
    /// <summary>The point on the figure the view is centred on and turns around (moved by panning).</summary>
    public Vec ItchTarget { get; private set; } = ItchHome;

    /// <summary>The middle of the body: where the view starts and where Centre puts it back.</summary>
    public static readonly Vec ItchHome = new(0, 0.9, 0);
    public const double ItchMaxZoom = 10, ItchZoomStep = 1.25, ItchMaxPitch = 70;
    /// <summary>The marked spot (part id and point on the figure), or null.</summary>
    public (string PartId, Vec Point)? ItchSpot { get; private set; }

    /// <summary>One press of Turn turns the figure this far: an eighth of a turn, so the sides come up in two presses.</summary>
    public const double ItchTurnStep = 45;
    /// <summary>One press of From above / From below tilts the view this many degrees (the most is <see cref="ItchMaxPitch"/>).</summary>
    public const double ItchTiltStep = 15;

    /// <summary>
    /// Whether the controls panel (Move the figure) beside the figure is shown. It stays the way the 🎮 icon left it,
    /// also the next time Itch opens and after a restart (<see cref="ItchControlsChanged"/>).
    /// </summary>
    public bool ItchControlsOpen { get; private set; }

    /// <summary>The controls panel was shown or folded away, so the choice can be saved.</summary>
    public event EventHandler? ItchControlsChanged;

    public void OpenItch(bool open)
    {
        if (ItchOpen == open) return;
        ItchOpen = open;
        Rebuild();
        if (open) FocusCommand(CommandId.ItchTurnRight);
        else FocusCommand(CommandId.Itch);
        RaiseAll();
    }

    /// <summary>Records the spot the user clicked and types out where it is, for the person scratching to read (Say it speaks it).</summary>
    public void MarkItch(string partId, Vec point)
    {
        if (Mannequin.Find(partId) is not { } part) return;
        ItchSpot = (partId, point);
        SetDraft(ItchDescriber.Sentence(part, point));
        StatusText = "Marked. The sentence is in the message box; press Say it to speak it.";
        RaiseAll();
    }

    /// <summary>
    /// Zooms in (positive steps) or out. With a point (what is under the mouse), that point stays where it
    /// is on screen, so the wheel zooms into whatever the user points at; otherwise it zooms on the view's centre.
    /// </summary>
    public void ZoomItch(int steps, Vec? toward = null)
    {
        var old = ItchZoom;
        ItchZoom = Math.Clamp(ItchZoom * Math.Pow(ItchZoomStep, steps), 1, ItchMaxZoom);
        if (toward is { } point && ItchZoom != old)
        {
            // Only the part of the offset across the screen counts (not the part along the view).
            var look = ItchLookDirection();
            var offset = point - ItchTarget;
            offset -= look * offset.Dot(look);
            ItchTarget += offset * (1 - old / ItchZoom);
        }
        ItchTarget = ClampTarget(ItchTarget); // closer in, the view must stay closer to the body
        RaiseView();
    }

    /// <summary>
    /// Moves the view across the figure by a distance in metres (right and up as seen on screen). The view
    /// always stays on the body, so the figure never leaves the screen completely.
    /// </summary>
    public void PanItch(double right, double up)
    {
        var yaw = ItchYaw * Math.PI / 180;
        var screenRight = new Vec(Math.Cos(yaw), 0, -Math.Sin(yaw));
        ItchTarget = ClampTarget(ItchTarget + screenRight * right + new Vec(0, up, 0));
        RaiseView();
    }

    /// <summary>One press of a move key moves the view a fifth of what is on screen.</summary>
    private void PanItchStep(int right, int up)
    {
        var step = 0.4 / ItchZoom;
        PanItch(right * step, up * step);
    }

    /// <summary>Tilts the view to look down on the figure (positive) or up at it.</summary>
    public void TiltItch(double degrees)
    {
        ItchPitch = Math.Clamp(ItchPitch + degrees, -ItchMaxPitch, ItchMaxPitch);
        RaiseView();
    }

    /// <summary>The direction the camera looks in, from the turn and tilt.</summary>
    public Vec ItchLookDirection()
    {
        var yaw = ItchYaw * Math.PI / 180;
        var pitch = ItchPitch * Math.PI / 180;
        // The camera sits in front of the figure at yaw 0 (on +z), and above it for a positive pitch.
        return new Vec(-Math.Sin(yaw) * Math.Cos(pitch), -Math.Sin(pitch), -Math.Cos(yaw) * Math.Cos(pitch));
    }

    /// <summary>
    /// Keeps the view's centre close to the body, so the figure can never go off screen: within a quarter of a metre
    /// when the whole body shows, and closer the more it is zoomed in (2.5 cm at full zoom).
    /// </summary>
    private Vec ClampTarget(Vec v) => Mannequin.Within(v, ItchLeash);

    /// <summary>How far the view's centre may be from the body at the current zoom, in metres.</summary>
    public double ItchLeash => 0.25 / ItchZoom;

    /// <summary>
    /// Raised when only the view of the figure changed (turn, tilt, zoom, pan), so the 3D view can follow it
    /// without the rest of the window refreshing on every mouse move.
    /// </summary>
    public event EventHandler? ItchViewChanged;

    private void RaiseView() => ItchViewChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>Folds the controls panel away or brings it back (the controller icon over the figure).</summary>
    public void ToggleItchControls() => ItchCommand(CommandId.ItchControls);

    /// <summary>Turns the figure by a number of degrees (dragging on it, or the Turn buttons).</summary>
    public void TurnItch(double degrees)
    {
        ItchYaw = ((ItchYaw + degrees) % 360 + 360) % 360;
        RaiseView();
    }

    private void ItchCommand(CommandId command)
    {
        switch (command)
        {
            case CommandId.ItchTurnLeft: TurnItch(-ItchTurnStep); break;
            case CommandId.ItchTurnRight: TurnItch(ItchTurnStep); break;
            case CommandId.ItchFront: ItchYaw = 0; ItchPitch = 0; RaiseView(); break;
            case CommandId.ItchBack: ItchYaw = 180; ItchPitch = 0; RaiseView(); break;
            case CommandId.ItchZoomIn: ZoomItch(2); break;
            case CommandId.ItchZoomOut: ZoomItch(-2); break;
            case CommandId.ItchControls:
                ItchControlsOpen = !ItchControlsOpen;
                ItchControlsChanged?.Invoke(this, EventArgs.Empty);
                Rebuild(); // focus stays where it was, or moves to the nearest buttons when its rows went away
                RaiseAll(); // the window folds the panel away (or back)
                break;
            case CommandId.ItchTiltAbove: TiltItch(ItchTiltStep); break;
            case CommandId.ItchTiltBelow: TiltItch(-ItchTiltStep); break;
            case CommandId.ItchPanUp: PanItchStep(0, 1); break;
            case CommandId.ItchPanDown: PanItchStep(0, -1); break;
            case CommandId.ItchPanLeft: PanItchStep(-1, 0); break;
            case CommandId.ItchPanRight: PanItchStep(1, 0); break;
            case CommandId.ItchCentre: ItchZoom = 1; ItchPitch = 0; ItchTarget = ItchHome; RaiseView(); break;
        }
    }

    /// <summary>
    /// Rows of the Itch screen. The figure's panel holds just the figure. Beside it, the controls panel (while
    /// shown) is "Move the figure" with a button for every movement. The words said out loud take the urgent
    /// strip's place at the top while Itch is open (see <see cref="Rebuild"/>).
    /// </summary>
    private void AddItchRows(List<RowView> rows)
    {
        // The figure fills its panel.
        rows.Add(new RowView(ItchViewRow, Array.Empty<CellView>()));
        if (!ItchControlsOpen) return;
        rows.Add(new RowView(ItchMoveTitleRow, Array.Empty<CellView>(), Title: "Move the figure"));
        // Every movement has its own buttons, so the figure can be handled with left clicks alone (dragging and
        // the wheel still work). Laid out like a remote control, one colour for each kind of movement:
        //   Turn (blue, wide)  .  Front / Back
        //   From above (teal)  .  Slide: a cross of four arrows around Whole body (purple)  .  Closer / Farther (gold)
        // The cross sits between two columns of wide buttons; every row adds up to eight units.
        const double pad = 1.2, side = 2.0, gapAround = 0.2;
        rows.Add(MoveRow("turn",
            Tool("itch-turn-left", "Turn the figure left", CommandId.ItchTurnLeft, "⟲", "Turn left", 2.6, tone: "turn"),
            Tool("itch-front", "Show the front", CommandId.ItchFront, "☺", "Front", 1.4, tone: "view"),
            Tool("itch-back", "Show the back", CommandId.ItchBack, "◍", "Back", 1.4, tone: "view"),
            Tool("itch-turn-right", "Turn the figure right", CommandId.ItchTurnRight, "⟳", "Turn right", 2.6, tone: "turn")));
        rows.Add(MoveRow("up",
            Tool("itch-tilt-above", "Look at the figure from above", CommandId.ItchTiltAbove, "⇩", "From above", side, tone: "tilt"),
            Tool("itch-pan-up", "Slide the figure up", CommandId.ItchPanUp, "▲", "Up", pad, gap: gapAround + pad, tone: "slide"),
            Tool("itch-zoom-in", "Closer", CommandId.ItchZoomIn, "＋", "Closer", side, gap: gapAround + pad, tone: "zoom")));
        rows.Add(MoveRow("middle",
            Tool("itch-tilt-below", "Look at the figure from below", CommandId.ItchTiltBelow, "⇧", "From below", side, tone: "tilt"),
            Tool("itch-pan-left", "Slide the figure left", CommandId.ItchPanLeft, "◀", "Left", pad, gap: gapAround, tone: "slide"),
            Tool("itch-centre", "Show the whole body again", CommandId.ItchCentre, "⌂", "Whole body", pad, tone: "view"),
            Tool("itch-pan-right", "Slide the figure right", CommandId.ItchPanRight, "▶", "Right", pad, tone: "slide"),
            Tool("itch-zoom-out", "Farther", CommandId.ItchZoomOut, "－", "Farther", side, gap: gapAround, tone: "zoom")));
        rows.Add(MoveRow("down",
            Tool("itch-pan-down", "Slide the figure down", CommandId.ItchPanDown, "▼", "Down", pad, gap: side + gapAround + pad, tone: "slide")));
    }

    /// <summary>The Itch button's words: a small arrow points the way the figure's panel opens (or folds back).</summary>
    private string ItchClosedLabel => _options.ItchOpensRight ? "Itch ▶" : "◀ Itch";
    private string ItchOpenLabel => _options.ItchOpensRight ? "Itch ◀" : "▶ Itch";

    /// <summary>
    /// The strip while Itch is open: the four direction words with their arrows, then the editable words, each
    /// said at once. Wider words get more room, so every button can show its text at a readable size.
    /// </summary>
    private IEnumerable<Cell> SpokenCells() =>
        _phrases.Spoken.Select(w => ItchArrows.FirstOrDefault(d => string.Equals(d.Word, w, StringComparison.OrdinalIgnoreCase)) is { Arrow: { } arrow }
                // A direction word gets its arrow; it can be moved, renamed or deleted like any other word.
                ? new Cell($"dir-{w}", $"{arrow} {w}", CellKind.Spoken, Payload: w, Hint: EditHint(true), Span: 1.2)
                : new Cell(SpokenWordPrefix + w, w, CellKind.Spoken, Payload: w, Hint: EditHint(true), Span: SpokenSpan(w)))
            .Concat(EditMode ? new[] { AddCell("spoken") with { Span = 1.4 } } : Array.Empty<Cell>());

    private static double SpokenSpan(string word) => Math.Clamp(0.6 + word.Length * 0.12, 1.0, 2.4);

    /// <summary>A button that moves the figure: a large symbol with a one-word caption, drawn in the tool colour.</summary>
    private static Cell Tool(string id, string label, CommandId cmd, string icon, string caption, double span = 1, double gap = 0, string? tone = null) =>
        new(id, label, CellKind.Tool, cmd, Icon: icon, Caption: caption, Span: span, Gap: gap, Tone: tone);

    /// <summary>The Move-the-figure rows are eight units wide, so every button keeps one size from row to row.</summary>
    private const double MoveUnits = 8;
    private static RowView MoveRow(string name, params Cell[] cells) => new(ItchMoveRowPrefix + name, cells.Select(c => new CellView(c)).ToList(), MoveUnits);

    /// <summary>The direction words, each with its arrow: fixed, said out loud with one press.</summary>
    public static readonly IReadOnlyList<(string Arrow, string Word)> ItchArrows = new[]
    {
        ("▲", "Up"), ("▼", "Down"), ("◀", "Left"), ("▶", "Right"),
    };

    /// <summary>While on, phrase buttons are edited instead of spoken or loaded.</summary>
    public bool EditMode { get; private set; }

    public void SetEditMode(bool on)
    {
        if (EditMode == on) return;
        EditMode = on;
        Rebuild();
        RaiseAll();
    }

    private List<string>? EditableList(PhraseEdit edit) => edit.List switch
    {
        PhraseList.Urgent => _phrases.PinnedPhrases ??= PhraseLibrary.DefaultPinnedPhrases.ToList(),
        PhraseList.Quick => _phrases.QuickPhrases,
        PhraseList.Spoken => _phrases.SpokenPhrases ??= PhraseLibrary.DefaultSpokenPhrases.ToList(),
        _ => _phrases.Categories.FirstOrDefault(c => c.Name == edit.Category)?.Phrases,
    };

    /// <summary>Where the button is in its row: (1-based position, how many), or null when it is not there.</summary>
    public (int Position, int Count)? PlaceOf(PhraseEdit edit) =>
        EditableList(edit) is { } list && edit.OldText is { } text && list.IndexOf(text) is var at and >= 0 ? (at + 1, list.Count) : null;

    /// <summary>Moves a button earlier (negative) or later (positive) among its row's buttons; false at either end.</summary>
    public bool MoveButton(PhraseEdit edit, int by)
    {
        if (EditableList(edit) is not { } list || edit.OldText is not { } text) return false;
        var at = list.IndexOf(text);
        var to = at + by;
        if (at < 0 || to < 0 || to >= list.Count || by == 0) return false;
        list.RemoveAt(at);
        list.Insert(to, text);
        Rebuild();
        RaiseAll();
        PhrasesChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Renames, adds (when <see cref="PhraseEdit.OldText"/> is null) or, with empty text, deletes a button.</summary>
    public void ApplyEdit(PhraseEdit edit, string? newText)
    {
        var list = EditableList(edit);
        if (list is null) return;
        var text = newText?.Trim() ?? "";
        var at = edit.OldText is null ? -1 : list.IndexOf(edit.OldText);
        if (at >= 0 && text.Length == 0) list.RemoveAt(at);
        else if (at >= 0) list[at] = text;
        else if (text.Length > 0) list.Add(text);
        else return;
        Rebuild();
        RaiseAll();
        PhrasesChanged?.Invoke(this, EventArgs.Empty);
    }

    private string? EditHint(bool editable) => EditMode && editable ? "✎ tap to change" : null;

    public string Draft { get; private set; } = "";
    /// <summary>Which drawer is open below the keyboard (None when closed).</summary>
    public Drawer Drawer { get; private set; }
    public string? OpenCategory => _openCategory;
    /// <summary>A problem worth showing for a few seconds (voice missing, settings unreadable, a crash). Not routine status.</summary>
    public string? Notice { get; private set; }
    public IReadOnlyList<RowView> Rows => _rows;
    public string StatusText { get; private set; } = "Ready.";
    public bool IsSpeaking => _speech.IsSpeaking;
    public string? SpeakingText => _speech.Current?.DisplayText;

    /// <summary>
    /// What the message box shows while speech is going: the text being said (it stays there, with the word
    /// being said highlighted, until it finishes), unless the user has started typing the next message.
    /// Single words said while typing (word-by-word mode) are not shown.
    /// </summary>
    public string? ReadAloudText => _speech.Current is { Kind: not UtteranceKind.Word } u && Draft.Trim().Length == 0 ? u.DisplayText : null;

    /// <summary>The word of <see cref="ReadAloudText"/> being said (0 = the first), or null before the voice starts.</summary>
    public int? ReadAloudWord => _speech.SpokenWord;

    /// <summary>Raised as the voice moves to the next word (the window moves the highlight).</summary>
    public event EventHandler? SpeechProgress;
    public string? LastSpoken => _speech.LastSpokenText;
    /// <summary>Word suggestion slots on the keyboard; the row always has this many so nothing below it moves.</summary>
    public const int SuggestionSlots = 6;

    /// <summary>
    /// Which prediction (0 = likeliest) goes in a slot of the green row, counted from the left. From the middle,
    /// six slots hold ranks 4 2 0 1 3 5: the likeliest just left of centre, the next just right of it, and so on outward.
    /// </summary>
    public static int SuggestionRank(int slot, bool fromMiddle)
    {
        if (!fromMiddle) return slot;
        var centre = (SuggestionSlots - 1) / 2;
        return slot <= centre ? 2 * (centre - slot) : 2 * (slot - centre) - 1;
    }
    public IReadOnlyList<MessageMetric> CompletedMessages => _metrics;
    public int TotalActions { get; private set; }

    public int FocusRow => Math.Max(0, IndexOfRow(r => r.Name == _focusRowName));
    public int FocusCol => _focusCol;
    public Cell? FocusedCell => _rows.Count == 0 ? null : _rows[FocusRow].Cells.ElementAtOrDefault(_focusCol)?.Cell;

    // ---------- input entry points ----------

    /// <summary>A gated action from the keyboard/QuadStick.</summary>
    public void HandleAction(InputAction action)
    {
        CountAction();
        switch (action)
        {
            case InputAction.MoveUp: MoveFocus(-1, 0); break;
            case InputAction.MoveDown: MoveFocus(1, 0); break;
            case InputAction.MoveLeft: MoveFocus(0, -1); break;
            case InputAction.MoveRight: MoveFocus(0, 1); break;
            case InputAction.Select: if (FocusedCell is { } c) Execute(c); break;
            // Approve is kept as a key name for older settings files; it now just speaks.
            case InputAction.Approve or InputAction.Speak: Speak(); break;
            case InputAction.StopVoice: StopVoice(); break;
            case InputAction.DeleteLetter: DeleteLetter(); break;
            case InputAction.Undo: Undo(); break;
            case InputAction.Home: CloseDrawer(); break;
            case InputAction.ToggleWindow: WindowToggleRequested?.Invoke(this, EventArgs.Empty); break;
            case InputAction.DollTurnLeft when ItchOpen: ItchCommand(CommandId.ItchTurnLeft); break;
            case InputAction.DollTurnRight when ItchOpen: ItchCommand(CommandId.ItchTurnRight); break;
            case InputAction.DollCloser when ItchOpen: ZoomItch(1); break;
            case InputAction.DollFarther when ItchOpen: ZoomItch(-1); break;
            case InputAction.DollMoveUp when ItchOpen: PanItchStep(0, 1); break;
            case InputAction.DollMoveDown when ItchOpen: PanItchStep(0, -1); break;
            case InputAction.DollMoveLeft when ItchOpen: PanItchStep(-1, 0); break;
            case InputAction.DollMoveRight when ItchOpen: PanItchStep(1, 0); break;
            case InputAction.DollTiltAbove when ItchOpen: ItchCommand(CommandId.ItchTiltAbove); break;
            case InputAction.DollTiltBelow when ItchOpen: ItchCommand(CommandId.ItchTiltBelow); break;
            case InputAction.DollFront when ItchOpen: ItchCommand(CommandId.ItchFront); break;
            case InputAction.DollBack when ItchOpen: ItchCommand(CommandId.ItchBack); break;
            case InputAction.DollWholeBody when ItchOpen: ItchCommand(CommandId.ItchCentre); break;
            case InputAction.DeleteWord: DeleteWord(); break;
            case InputAction.Space: RunCommand(CommandId.Space); break;
            case InputAction.Clear: RunCommand(CommandId.Clear); break;
            case InputAction.Quick: Toggle(Drawer.Quick); break;
            case InputAction.Phrases: Toggle(Drawer.Phrases); break;
            case InputAction.Itch: OpenItch(!ItchOpen); break;
        }
        RaiseAll();
    }

    /// <summary>A click on an on-screen target (QuadStick in mouse mode). Repeat clicks on the same target are debounced.</summary>
    public void ActivateCell(string cellId)
    {
        var now = _clock.Now;
        // Delete keys are tapped fast on purpose (and repeat while held), so they get a much shorter guard;
        // so do panel buttons (Itch, Quick, Phrases, Controls), so closing and reopening one feels instant.
        var kind = _rows.SelectMany(r => r.Cells).FirstOrDefault(cv => cv.Cell.Id == cellId)?.Cell.Kind;
        var debounce = cellId is "del-letter" or "del-word" ? _options.DeletePointerDebounceMs
            : kind == CellKind.Menu ? _options.TogglePointerDebounceMs
            : _options.PointerDebounceMs;
        if (_lastPointer is { } lp && lp.Id == cellId && (now - lp.At).TotalMilliseconds < debounce) return;
        _lastPointer = (cellId, now);

        for (var r = 0; r < _rows.Count; r++)
        {
            var idx = _rows[r].Cells.ToList().FindIndex(cv => cv.Cell.Id == cellId);
            if (idx < 0) continue;
            CountAction();
            SetFocus(_rows[r].Name, idx);
            Execute(_rows[r].Cells[idx].Cell);
            RaiseAll();
            return;
        }
    }

    /// <summary>Text typed through any other route (Windows on-screen keyboard, a QuadStick key macro).</summary>
    public void TypeText(string text)
    {
        var clean = new string(text.Where(ch => !char.IsControl(ch)).ToArray());
        if (clean.Length == 0) return;
        CountAction();
        SetDraft(Draft + clean, typed: true);
        RaiseAll();
    }

    /// <summary>Shows a problem from the host app (e.g. a settings problem) briefly over the message box.</summary>
    public void ShowNotice(string text)
    {
        StatusText = text;
        Notice = text;
        RaiseAll();
    }

    // ---------- operations ----------

    /// <summary>Says the whole message now (one press, one time), then clears it if set to.</summary>
    public void Speak()
    {
        var text = Draft.Trim();
        var outcome = _speech.SpeakMessage(text);
        switch (outcome)
        {
            case SpeakOutcome.Started:
                CompleteMessage(text, _messageKind);
                if (_options.LearnFromSpokenMessages && !PhraseMemory.LooksPrivate(text))
                {
                    _predictor.Learn(text);
                    _memory.Record(text, _clock.Now);
                    LearningChanged?.Invoke(this, EventArgs.Empty);
                }
                StatusText = "Speaking.";
                if (_options.ClearMessageAfterSpeaking) SetDraft("");
                break;
            case SpeakOutcome.Empty:
                StatusText = "Nothing to say yet.";
                break;
            case SpeakOutcome.CoolingDown:
                StatusText = "Just spoke. Speak is paused for a moment.";
                break;
            case SpeakOutcome.Busy:
                StatusText = "Still speaking. Choose Stop voice first, or wait.";
                break;
        }
        Rebuild();
    }

    public void StopVoice()
    {
        _speech.Stop();
        StatusText = "Stopped.";
    }

    public void DeleteLetter()
    {
        if (Draft.Length == 0) return;
        SetDraft(Draft[..^1]);
    }

    public void DeleteWord()
    {
        var t = Draft.TrimEnd();
        var cut = t.LastIndexOf(' ');
        SetDraft(cut < 0 ? "" : t[..(cut + 1)]);
    }

    public void Undo()
    {
        if (_undo.Count == 0) { StatusText = "Nothing to undo."; return; }
        SetDraft(_undo.Pop(), recordUndo: false);
        StatusText = "Undone.";
    }

    /// <summary>Opens a drawer below the keyboard, closing any other; focus moves into it.</summary>
    public void Open(Drawer drawer)
    {
        if (drawer == Drawer.None) { CloseDrawer(); return; }
        _drawerOpener = drawer == Drawer.Quick ? CommandId.GoQuick : CommandId.GoPhrases;
        Drawer = drawer;
        if (drawer == Drawer.Phrases) _openCategory ??= Categories().FirstOrDefault()?.Name;
        Rebuild();
        FocusDrawer();
    }

    /// <summary>The same menu button opens its drawer or, if it is already open, closes it.</summary>
    public void Toggle(Drawer drawer)
    {
        if (Drawer == drawer) CloseDrawer();
        else Open(drawer);
    }

    /// <summary>Closes the drawer; focus goes back to the menu button that opened it.</summary>
    public void CloseDrawer()
    {
        if (Drawer == Drawer.None) return;
        var opener = _drawerOpener;
        Drawer = Drawer.None;
        Rebuild();
        FocusCommand(opener);
    }

    // ---------- execution of a selected cell ----------

    private void Execute(Cell cell)
    {
        if (cell.Command != CommandId.ForgetLearning) _forgetArmedAt = null;
        switch (cell.Kind)
        {
            case CellKind.Command or CellKind.Menu or CellKind.Tool: RunCommand(cell.Command); break;
            case CellKind.Letter: SetDraft(Draft + cell.Payload, typed: true); _messageKind = MessageKind.Typed; break;
            case CellKind.Prediction: ApplyPrediction(cell.Payload!); break;
            case CellKind.Pinned when EditMode: EditRequested?.Invoke(this, new PhraseEdit(PhraseList.Urgent, null, cell.Payload)); break;
            case CellKind.QuickPhrase when EditMode: EditRequested?.Invoke(this, new PhraseEdit(PhraseList.Quick, null, cell.Payload)); break;
            case CellKind.Spoken when EditMode && (cell.Id.StartsWith(SpokenWordPrefix, StringComparison.Ordinal) || cell.Id.StartsWith("dir-", StringComparison.Ordinal)):
                EditRequested?.Invoke(this, new PhraseEdit(PhraseList.Spoken, null, cell.Payload));
                break;
            case CellKind.Spoken: SayOutLoud(cell.Payload!); break;
            case CellKind.Phrase when EditMode && _openCategory != FrequentCategory:
                EditRequested?.Invoke(this, new PhraseEdit(PhraseList.Category, _openCategory, cell.Payload));
                break;
            case CellKind.QuickPhrase or CellKind.Pinned: QuickPhrase(cell.Payload!); break;
            case CellKind.Category:
                _openCategory = cell.Payload; // the grid below switches; focus stays on the chip
                Rebuild();
                break;
            case CellKind.Phrase:
                SetDraft(cell.Payload!);
                CloseDrawer();
                _messageKind = MessageKind.Phrase;
                StatusText = "Phrase loaded. Press Speak to say it.";
                if (_options.JumpToSpeakAfterChoosing) FocusCommand(CommandId.Speak);
                break;
        }
    }

    private void RunCommand(CommandId command)
    {
        switch (command)
        {
            case CommandId.GoQuick: Toggle(Drawer.Quick); break;
            case CommandId.GoKeyboard: CloseDrawer(); break;
            case CommandId.GoPhrases: Toggle(Drawer.Phrases); break;
            case CommandId.Speak: Speak(); break;
            case CommandId.StopVoice: StopVoice(); break;
            case CommandId.Space: SetDraft(Draft.Length == 0 || Draft.EndsWith(' ') ? Draft : Draft + " ", typed: true); break;
            case CommandId.DeleteLetter: DeleteLetter(); break;
            case CommandId.DeleteWord: DeleteWord(); break;
            case CommandId.Clear: SetDraft(""); StatusText = "Cleared. Undo brings it back."; break;
            case CommandId.Undo: Undo(); break;
            case CommandId.ForgetLearning: ForgetLearning(); break;
            case CommandId.HideWindow: WindowToggleRequested?.Invoke(this, EventArgs.Empty); break;
            case CommandId.OpenSettings: SettingsRequested?.Invoke(this, EventArgs.Empty); break;
            case CommandId.ClosePanel: CloseDrawer(); break;
            case CommandId.Itch: OpenItch(!ItchOpen); break;
            case >= CommandId.ItchTurnLeft and <= CommandId.ItchPanRight: ItchCommand(command); break;
            case CommandId.AddPhrase:
                EditRequested?.Invoke(this, FocusedCell?.Payload switch
                {
                    "urgent" => new PhraseEdit(PhraseList.Urgent, null, null),
                    "quick" => new PhraseEdit(PhraseList.Quick, null, null),
                    "spoken" => new PhraseEdit(PhraseList.Spoken, null, null),
                    _ => new PhraseEdit(PhraseList.Category, _openCategory, null),
                });
                break;
        }
    }

    /// <summary>Two deliberate selections within 10 seconds erase everything learned.</summary>
    private void ForgetLearning()
    {
        var now = _clock.Now;
        if (_forgetArmedAt is { } armed && (now - armed).TotalMilliseconds <= ForgetConfirmMs)
        {
            _forgetArmedAt = null;
            _predictor.ClearPersonal();
            _memory.Clear();
            LearningChanged?.Invoke(this, EventArgs.Empty);
            StatusText = "Forgot everything the app had learned.";
        }
        else
        {
            _forgetArmedAt = now;
            StatusText = "To erase everything learned, select Confirm forget again. Anything else cancels.";
        }
        Rebuild();
    }

    private void QuickPhrase(string text)
    {
        if (!_options.QuickPhrasesSpeakImmediately)
        {
            SetDraft(text);
            _messageKind = MessageKind.Quick;
            FocusCommand(CommandId.Speak);
            StatusText = "Press Speak to say it.";
            return;
        }
        var outcome = _speech.SpeakQuickPhrase(text);
        if (outcome == SpeakOutcome.Started)
        {
            CompleteMessage(text, MessageKind.Quick);
            StatusText = $"Saying: {text}";
        }
        else if (outcome == SpeakOutcome.CoolingDown) StatusText = "Already said that just now.";
        else if (outcome == SpeakOutcome.Busy) StatusText = "Still speaking.";
    }

    /// <summary>
    /// A Speak out loud word is always said straight away, whatever the quick-phrase setting: it guides someone in the
    /// moment, and the message being written stays as it is.
    /// </summary>
    private void SayOutLoud(string text)
    {
        var outcome = _speech.SpeakQuickPhrase(text);
        if (outcome == SpeakOutcome.Started)
        {
            CompleteMessage(text, MessageKind.Quick);
            StatusText = $"Saying: {text}";
        }
        else if (outcome == SpeakOutcome.CoolingDown) StatusText = "Already said that just now.";
        else if (outcome == SpeakOutcome.Busy) StatusText = "Still speaking.";
    }

    private void ApplyPrediction(string word)
    {
        var (_, partial) = WordPredictor.SplitForPrediction(Draft);
        var baseText = partial.Length > 0 ? Draft[..^partial.Length] : (Draft.Length == 0 || Draft.EndsWith(' ') ? Draft : Draft + " ");
        SetDraft(baseText + word + " ", typed: true);
    }

    /// <param name="typed">True for typing (letters, Space, a word suggestion, typed text): only then can
    /// word-by-word mode speak a finished word. Loading, deleting and Undo never speak.</param>
    private void SetDraft(string text, bool recordUndo = true, bool typed = false)
    {
        if (text == Draft) return;
        if (recordUndo) _undo.Push(Draft);
        Draft = text;
        SayFinishedWords(typed);


        _draftStore?.Save(Draft);
        Rebuild();
    }

    private static readonly System.Text.RegularExpressions.Regex WordPattern = new(@"[\p{L}\p{N}]+(?:'[\p{L}]+)?");
    /// <summary>The start of the message up to the end of the last word already said (or already there).</summary>
    private string _saidUpTo = "";

    /// <summary>
    /// Word-by-word mode: once a word is followed by a space or punctuation it is said, once. Words that
    /// were already in the message (loaded, restored by Undo, or kept after deleting) are never said again.
    /// </summary>
    private void SayFinishedWords(bool typed)
    {
        // A word is finished once a space or punctuation follows it (an apostrophe does not: "don't").
        var finished = WordPattern.Matches(Draft)
            .Where(m => m.Index + m.Length < Draft.Length && (char.IsWhiteSpace(Draft[m.Index + m.Length]) || ".,!?;:".Contains(Draft[m.Index + m.Length])))
            .ToList();
        var finishedEnd = finished.Count == 0 ? 0 : finished[^1].Index + finished[^1].Length;
        if (!typed || !_options.SpeakEachWord)
        {
            // Deleting only the space after a said word keeps it said, so retyping the space is silent.
            if (!(Draft.StartsWith(_saidUpTo, StringComparison.Ordinal) && _saidUpTo.Length > finishedEnd))
                _saidUpTo = Draft[..finishedEnd];
            return;
        }
        if (!Draft.StartsWith(_saidUpTo, StringComparison.Ordinal))
        {
            var common = 0;
            while (common < Math.Min(Draft.Length, _saidUpTo.Length) && Draft[common] == _saidUpTo[common]) common++;
            _saidUpTo = Draft[..finished.Where(m => m.Index + m.Length <= common).Select(m => m.Index + m.Length).DefaultIfEmpty(0).Max()];
        }
        var fresh = finished.Where(m => m.Index >= _saidUpTo.Length).Select(m => m.Value).ToList();
        _saidUpTo = Draft[..finishedEnd];
        if (fresh.Count > 0) _speech.SpeakWord(string.Join(' ', fresh));
    }

    private void CountAction()
    {
        TotalActions++;
        _actionsThisMessage++;
        _messageStartedAt ??= _clock.Now;
    }

    private void CompleteMessage(string text, MessageKind kind)
    {
        var started = _messageStartedAt ?? _clock.Now;
        _metrics.Add(new MessageMetric(_actionsThisMessage, _clock.Now - started, text.Length, kind));
        _actionsThisMessage = 0;
        _messageStartedAt = null;
        _messageKind = MessageKind.Typed;
    }

    // ---------- board ----------

    private void Rebuild()
    {
        var hasText = Draft.Trim().Length > 0;
        var rows = new List<RowView>();

        // The top strip is "action bar 1": the urgent phrases, one selection away, with Itch at its right end.
        // While Itch is open the Itch button stays where it is and the strip becomes "action bar 2": the words said
        // out loud to guide the person scratching, drawn like the Itch button. Pressing Itch again brings the urgent
        // phrases back. The strip keeps its height, so the keyboard never moves.
        var itch = new Cell("itch", ItchOpen ? ItchOpenLabel : ItchClosedLabel, CellKind.Menu, CommandId.Itch, Span: ItchOpen ? 1.5 : 1, IsOn: ItchOpen);
        var strip = ItchOpen
            ? SpokenCells()
            : _phrases.Pinned.Select(p => new Cell($"pin-{p}", p, CellKind.Pinned, Payload: p, Hint: EditHint(true)))
                .Concat(EditMode ? new[] { AddCell("urgent") } : Array.Empty<Cell>());
        rows.Add(Row(PinnedRow, strip.Append(itch).ToArray()));

        // Green row: only predicted words, always six slots (blank and disabled when empty) so it never moves.
        // By default the likeliest word sits in the middle and the rest alternate right and left outward.
        var words = _predictor.Predict(Draft, SuggestionSlots);
        rows.Add(Row(SuggestionsRow, Enumerable.Range(0, SuggestionSlots)
            .Select(slot => SuggestionRank(slot, _options.SuggestionsFromMiddle))
            .Select((rank, slot) => rank < words.Count
                ? new Cell($"pred-{slot}", words[rank], CellKind.Prediction, Payload: words[rank])
                : new Cell($"pred-{slot}", "", CellKind.Prediction, IsEnabled: false))
            .ToArray()));

        // Every key is one unit wide, like a real keyboard; rows are staggered by half a key and a key
        // and a half, and nothing is stretched to fill its row.
        var stagger = new[] { 0.0, 0.5, 1.5 };
        foreach (var (letters, i) in KeyboardLayout.Select((l, i) => (l, i)))
        {
            var keys = letters.Select((l, k) => new Cell($"key-{l}", l.ToUpperInvariant(), CellKind.Letter, Payload: l, Gap: k == 0 ? stagger[i] : 0)).ToList();
            // Backspace sits where it does on a real keyboard, after P: one key split into letter | word.
            if (i == 0)
            {
                keys.Add(new Cell("del-letter", "Delete letter", CellKind.Command, CommandId.DeleteLetter, Icon: "⌫", Caption: "", Part: KeyPart.Start));
                keys.Add(new Cell("del-word", "Delete word", CellKind.Command, CommandId.DeleteWord, Part: KeyPart.End));
            }
            // Speak is where Enter is, with a small Stop segment attached that lights up only while speaking.
            if (i == 1)
            {
                keys.Add(new Cell("speak", "Speak", CellKind.Command, CommandId.Speak, IsEnabled: hasText && !IsSpeaking, Icon: "🔊", Span: 2, Part: KeyPart.Start));
                keys.Add(new Cell("stop", "Stop speaking", CellKind.Command, CommandId.StopVoice, IsEnabled: IsSpeaking, Icon: "■", Caption: "Stop", Span: 0.5, Part: KeyPart.End));
            }
            rows.Add(KeyRow($"keys-{i}", keys.ToArray()));
        }

        // Bottom row: the three drawer buttons at the left, Undo, a five-key space bar and Clear, then
        // Settings and, last, Hide at the right edge (the row is 12 keys wide with Hide, 11 without).
        rows.Add(KeyRow("edit",
            Menu("go-quick", "Quick", CommandId.GoQuick, "⚡", on: Drawer == Drawer.Quick),
            Menu("go-phrases", "Phrases", CommandId.GoPhrases, "☰", on: Drawer == Drawer.Phrases),
            Cmd("undo", "Undo", CommandId.Undo, icon: "↶"),
            Cmd("space", "Space", CommandId.Space, icon: "␣", span: 6),
            Cmd("clear", "Clear", CommandId.Clear, icon: "✕"),
            Cmd("settings", "Settings", CommandId.OpenSettings, icon: "⚙"))
            .WithHide(_options.ShowHideKey ? Cmd("hide", "Hide", CommandId.HideWindow, icon: "◀") : null));

        var drawerRows = DrawerContent().Select((cells, i) => Row($"{DrawerRowPrefix}{i}", cells)).ToList();
        if (_options.DrawerAbove) rows.InsertRange(0, drawerRows);
        else rows.AddRange(drawerRows);

        // Itch opens beside the keyboard (the host draws these rows in a panel on the right).
        if (ItchOpen) AddItchRows(rows);

        FinishRebuild(rows);
    }

    private void FinishRebuild(List<RowView> rows)
    {
        var previousIndex = IndexOfRow(r => r.Name == _focusRowName);
        Reconcile(rows);
        if (_rows.All(r => r.Name != _focusRowName) || _rows.First(r => r.Name == _focusRowName).Cells.Count == 0)
        {
            var idx = Math.Clamp(previousIndex < 0 ? 0 : previousIndex, 0, _rows.Count - 1);
            while (idx < _rows.Count - 1 && _rows[idx].Cells.Count == 0) idx++;
            while (idx > 0 && _rows[idx].Cells.Count == 0) idx--; // the last rows can be button-less (the doll)
            _focusRowName = _rows[idx].Name;
        }
        _focusCol = Math.Clamp(_focusCol, 0, _rows[FocusRow].Cells.Count - 1);
        ApplyFocus();
    }

    private List<PhraseCategory> Categories()
    {
        var frequent = _memory.Frequent(15);
        return (frequent.Count > 0 ? new[] { new PhraseCategory { Name = FrequentCategory, Phrases = frequent.ToList() } } : Array.Empty<PhraseCategory>())
            .Concat(_phrases.Categories).ToList();
    }

    /// <summary>The drawer's rows (at most <see cref="DrawerRows"/>), or none when it is closed.</summary>
    private List<Cell[]> DrawerContent()
    {
        static List<Cell[]> InRows(IReadOnlyList<Cell> cells, int rowCount) =>
            cells.Count == 0 ? new() : cells.Chunk((int)Math.Ceiling(cells.Count / (double)rowCount)).ToList();

        switch (Drawer)
        {
            case Drawer.Quick:
                return InRows(_phrases.QuickPhrases.Select(p => new Cell($"quick-{p}", p, CellKind.QuickPhrase, Payload: p, Hint: EditHint(true)))
                    .Concat(EditMode ? new[] { AddCell("quick") } : Array.Empty<Cell>()).ToList(), DrawerRows);

            case Drawer.Phrases:
            {
                var categories = Categories();
                var chips = categories.Select(c => new Cell($"cat-{c.Name}", c.Name, CellKind.Category, Payload: c.Name, IsOn: c.Name == _openCategory)).ToList();
                if (_options.LearnFromSpokenMessages)
                    chips.Add(new Cell("forget", _forgetArmedAt is null ? "Forget learning" : "Confirm forget", CellKind.Command, CommandId.ForgetLearning,
                        Hint: _forgetArmedAt is null ? null : "erases learned words and phrases"));
                var phrases = categories.FirstOrDefault(c => c.Name == _openCategory)?.Phrases ?? new List<string>();
                var editable = _openCategory != FrequentCategory; // "Most used" is learned, not typed in
                var grid = InRows(phrases.Select(p => new Cell($"phrase-{p}", p, CellKind.Phrase, Payload: p, Hint: EditHint(editable)))
                    .Concat(EditMode && editable ? new[] { AddCell("category") } : Array.Empty<Cell>()).ToList(), DrawerRows - 1);
                return grid.Prepend(chips.ToArray()).ToList();
            }

            default:
                return new();
        }
    }

    private void FocusDrawer()
    {
        var first = IndexOfRow(r => r.Name.StartsWith(DrawerRowPrefix, StringComparison.Ordinal));
        if (first >= 0) SetFocus(_rows[first].Name, 0);
    }

    private int IndexOfRow(Func<RowView, bool> match)
    {
        for (var i = 0; i < _rows.Count; i++)
            if (match(_rows[i])) return i;
        return -1;
    }

    /// <summary>
    /// Brings the shown rows in line with <paramref name="rows"/> while keeping every row whose targets are
    /// the same (by name and cell ids): those only get their changed cells updated. Rebuilding the whole
    /// board on each keystroke made the UI recreate every button, which was the main source of lag.
    /// </summary>
    private void Reconcile(List<RowView> rows)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            var fresh = rows[i];
            if (i < _rows.Count)
            {
                var old = _rows[i];
                if (old.Name == fresh.Name && old.Units == fresh.Units && old.Cells.Count == fresh.Cells.Count &&
                    old.Cells.Zip(fresh.Cells).All(p => p.First.Cell.Id == p.Second.Cell.Id))
                {
                    for (var c = 0; c < old.Cells.Count; c++) old.Cells[c].Cell = fresh.Cells[c].Cell;
                }
                else _rows[i] = fresh;
            }
            else _rows.Add(fresh);
        }
        while (_rows.Count > rows.Count) _rows.RemoveAt(_rows.Count - 1);
    }

    private static RowView Row(string name, params Cell[] cells) => new(name, cells.Select(c => new CellView(c)).ToList());
    private static RowView KeyRow(string name, params Cell[] cells) => new(name, cells.Select(c => new CellView(c)).ToList(), KeyboardUnits);

    /// <summary>Keyboard rows are this many key-widths wide: Q–P plus the two backspace halves.</summary>
    private const double KeyboardUnits = 12;
    private static Cell AddCell(string list) => new($"add-{list}", "＋ Add", CellKind.Command, CommandId.AddPhrase, Payload: list);

    private static Cell Menu(string id, string label, CommandId cmd, string icon, bool on) =>
        new(id, label, CellKind.Menu, cmd, Icon: icon, IsOn: on);

    private static Cell Cmd(string id, string label, CommandId cmd, bool enabled = true, string? icon = null, string? caption = null, double span = 1) =>
        new(id, label, CellKind.Command, cmd, IsEnabled: enabled, Icon: icon, Caption: caption, Span: span);

    private void MoveFocus(int dRow, int dCol)
    {
        // Rows without targets (the 3D view) are stepped over.
        var navigable = _rows.Where(x => x.Cells.Count > 0).ToList();
        var from = Math.Max(0, navigable.FindIndex(x => x.Name == _focusRowName));
        var (r, c) = GridNavigator.Move(navigable.Select(x => x.Geometry).ToList(), from, _focusCol, dRow, dCol, _options.WrapNavigation);
        SetFocus(navigable[r].Name, c);
    }

    private void SetFocus(string rowName, int col)
    {
        var idx = IndexOfRow(r => r.Name == rowName);
        if (idx < 0) return;
        _focusRowName = rowName;
        _focusCol = Math.Clamp(col, 0, _rows[idx].Cells.Count - 1);
        ApplyFocus();
    }

    private void FocusCommand(CommandId command)
    {
        foreach (var row in _rows)
        {
            var col = row.Cells.ToList().FindIndex(c => c.Cell.Command == command);
            if (col < 0) continue;
            SetFocus(row.Name, col);
            return;
        }
    }

    private void FocusFirstPageRow() => SetFocus("keys-0", 0);

    private void ApplyFocus()
    {
        var fr = FocusRow;
        for (var r = 0; r < _rows.Count; r++)
            for (var c = 0; c < _rows[r].Cells.Count; c++)
                _rows[r].Cells[c].IsFocused = r == fr && c == _focusCol;
    }

    private void OnUi(Action a)
    {
        if (_ui is null || SynchronizationContext.Current == _ui) a();
        else _ui.Post(_ => a(), null);
    }

    private void RaiseAll() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
}
