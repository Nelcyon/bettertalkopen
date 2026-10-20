namespace Aac.Core.Input;

/// <summary>
/// One square of a QuadStick sip-and-puff card: what that gesture on that tube sends to the PC. <see cref="Key"/>
/// is a key or mouse button BetterTalk receives ("Enter", "RightClick", "LeftClick", "Ctrl+C"); <see cref="Note"/>
/// names something that stays with the QuadStick or Windows (a mode switch, the Windows key). Both empty: unused.
/// </summary>
public sealed record CardCell(string? Key = null, string? Note = null);

/// <summary>What a card square means to BetterTalk.</summary>
public enum CardSendKind
{
    Nothing,
    /// <summary>A key or mouse button that an in-app action can be given to.</summary>
    Key,
    /// <summary>The left mouse button: it always clicks what the pointer is on.</summary>
    LeftClick,
    /// <summary>Ctrl-C, Ctrl-X or Ctrl-V: copy, cut or paste the whole message.</summary>
    Clipboard,
    /// <summary>Not for BetterTalk (a QuadStick mode switch, the Windows key, a dictation microphone).</summary>
    Elsewhere,
}

/// <summary>
/// The QuadStick card in Settings → Hotkeys: the tubes across, the sip and puff strengths down, like the paper
/// reference cards QuadStick users keep. The squares only record what each gesture sends; what BetterTalk does
/// with a key is still its in-app key binding, so the card and the key list are two views of the same keys.
/// </summary>
public static class QuadStickCard
{
    public static readonly IReadOnlyList<string> Tubes = new[] { "Left", "Left+Center", "Center", "Right+Center", "Right", "Side" };
    public static readonly IReadOnlyList<string> Gestures = new[] { "Soft puff", "Puff", "Sip", "Soft sip" };

    public const string LeftClick = "LeftClick";

    public static string CellId(string gesture, string tube) => gesture + "|" + tube;

    /// <summary>A common QuadStick set-up to start from; every square can be changed.</summary>
    public static Dictionary<string, CardCell> Default() => new()
    {
        [CellId("Soft puff", "Center")] = new("MiddleClick"),
        [CellId("Soft puff", "Right")] = new("Delete"),
        [CellId("Soft puff", "Side")] = new(Note: "On-screen keyboard"),
        [CellId("Puff", "Left")] = new("Tab"),
        [CellId("Puff", "Center")] = new(LeftClick),
        [CellId("Puff", "Right")] = new("Back"),
        [CellId("Puff", "Side")] = new(Note: "MODE −"),
        [CellId("Sip", "Left")] = new("Escape"),
        [CellId("Sip", "Left+Center")] = new(Note: "Windows key"),
        [CellId("Sip", "Center")] = new("RightClick"),
        [CellId("Sip", "Right")] = new("Enter"),
        [CellId("Sip", "Side")] = new(Note: "MODE +"),
        [CellId("Soft sip", "Left")] = new("Ctrl+X"),
        [CellId("Soft sip", "Center")] = new("Ctrl+C"),
        [CellId("Soft sip", "Right+Center")] = new(Note: "Ctrl+Alt+Delete"),
        [CellId("Soft sip", "Right")] = new("Ctrl+V"),
        [CellId("Soft sip", "Side")] = new(Note: "Dictation microphone"),
    };

    public static CardSendKind KindOf(CardCell? cell) => cell switch
    {
        { Key: { Length: > 0 } key } when string.Equals(key, LeftClick, StringComparison.OrdinalIgnoreCase) => CardSendKind.LeftClick,
        { Key: { Length: > 0 } key } when IsClipboard(key) => CardSendKind.Clipboard,
        { Key: { Length: > 0 } } => CardSendKind.Key,
        { Note: { Length: > 0 } } => CardSendKind.Elsewhere,
        _ => CardSendKind.Nothing,
    };

    public static bool IsClipboard(string key) =>
        key.Equals("Ctrl+C", StringComparison.OrdinalIgnoreCase) || key.Equals("Ctrl+X", StringComparison.OrdinalIgnoreCase)
        || key.Equals("Ctrl+V", StringComparison.OrdinalIgnoreCase);

    /// <summary>How a key is written on the card: "Backspace" rather than WPF's "Back", "Right click" rather than "RightClick".</summary>
    public static string Display(string key) => key switch
    {
        "Back" => "Backspace",
        "Return" => "Enter",
        "Next" => "Page Down",
        "Prior" => "Page Up",
        "RightClick" => "Right click",
        "MiddleClick" => "Middle click",
        "SideButton1" => "Side button 1",
        "SideButton2" => "Side button 2",
        LeftClick => "Left click",
        _ when key.StartsWith("Ctrl+", StringComparison.OrdinalIgnoreCase) => "Ctrl-" + key[5..].ToUpperInvariant(),
        _ => key,
    };

    /// <summary>What BetterTalk does with a clipboard key.</summary>
    public static string ClipboardMeaning(string key) => key.ToUpperInvariant() switch
    {
        "CTRL+C" => "Copies the message",
        "CTRL+X" => "Cuts the message",
        _ => "Pastes into the message",
    };
}
