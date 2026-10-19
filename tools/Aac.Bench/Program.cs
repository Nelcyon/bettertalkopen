using System.Text;
using Aac.Core.Abstractions;
using Aac.Core.Board;
using Aac.Core.Input;
using Aac.Core.Prediction;
using Aac.Core.Session;
using Aac.Core.Speech;

// Counts the physical actions (moves + selects) an error-free user would need for representative
// tasks under different strategies, by driving the real CommunicationSession with simulated input.
// Synthetic only: it says nothing about real speed, fatigue or errors until measured with the user.

var tasks = new (string Name, string Target, string? Keywords, string? Quick, (string Category, string Phrase)? Phrase)[]
{
    ("Answer yes/no", "yes", null, "Yes.", null),
    ("Ask for help", "i need help", null, "I need help.", null),
    ("Ask for water", "i need water", "need water", null, ("Needs", "I need water.")),
    ("Simple sentence", "i want coffee", "want coffee", null, null),
    ("Opinion about a documentary", "did you see the documentary yesterday the ending was ridiculous", "documentary yesterday ending ridiculous", null, null),
    ("Talk about a game", "the game last night was amazing", "game last night amazing", null, null),
    ("Negated feeling", "i'm not tired", "not tired", null, null),
};

var sb = new StringBuilder();
sb.AppendLine("| Task | Letters only | Letters + word prediction | Quick / Phrases drawer |");
sb.AppendLine("|---|---:|---:|---:|");
foreach (var t in tasks)
{
    var letters = Run(s => TypeWords(s, t.Target, usePrediction: false) && Speak(s));
    var predicted = Run(s => TypeWords(s, t.Target, usePrediction: true) && Speak(s));
    string quick = "–";
    if (t.Quick is not null) quick = Run(s =>
        SelectCell(s, c => c.Command == CommandId.GoQuick) &&
        SelectCell(s, c => c.Kind == CellKind.QuickPhrase && c.Payload == t.Quick));
    else if (t.Phrase is { } p) quick = Run(s =>
        SelectCell(s, c => c.Command == CommandId.GoPhrases) &&
        SelectCell(s, c => c.Kind == CellKind.Category && c.Payload == p.Category) &&
        SelectCell(s, c => c.Kind == CellKind.Phrase && c.Payload == p.Phrase) &&
        Speak(s, alreadyOnSpeak: true));
    sb.AppendLine($"| {t.Name} | {letters} | {predicted} | {quick} |");
}

Console.WriteLine(sb.ToString());
Console.WriteLine("Counts include pressing Speak (each navigation step and each selection is one action).");

static string Run(Func<CommunicationSession, bool> script)
{
    var clock = new ManualClock();
    var speech = new SpeechController(new SilentEngine(), clock, new SpeechOptions { CooldownMs = 0 });
    var session = new CommunicationSession(speech, WordPredictor.CreateDefault(), PhraseLibrary.CreateDefault(),
        new SessionOptions(), clock);
    var before = session.TotalActions;
    var ok = script(session);
    return ok && speech.SpokenLog.Count == 1 ? (session.TotalActions - before).ToString() : "miss";
}

static bool TypeWords(CommunicationSession s, string text, bool usePrediction)
{
    foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
    {
        var typed = 0;
        var done = false;
        while (!done)
        {
            if (usePrediction && Find(s, c => c.Kind == CellKind.Prediction && c.Payload == word) is not null)
            {
                SelectCell(s, c => c.Kind == CellKind.Prediction && c.Payload == word);
                done = true;
            }
            else if (typed < word.Length)
            {
                var letter = word[typed].ToString();
                if (!SelectCell(s, c => c.Kind == CellKind.Letter && c.Payload == letter)) return false;
                typed++;
            }
            else
            {
                SelectCell(s, c => c.Command == CommandId.Space);
                done = true;
            }
        }
    }
    return true;
}

static bool Speak(CommunicationSession s, bool alreadyOnSpeak = false)
{
    if (alreadyOnSpeak && s.FocusedCell?.Command == CommandId.Speak)
    {
        s.HandleAction(InputAction.Select);
        return true;
    }
    return SelectCell(s, c => c.Command == CommandId.Speak);
}

static (int Row, int Col)? Find(CommunicationSession s, Func<Cell, bool> pick)
{
    for (var r = 0; r < s.Rows.Count; r++)
        for (var c = 0; c < s.Rows[r].Cells.Count; c++)
            if (pick(s.Rows[r].Cells[c].Cell)) return (r, c);
    return null;
}

/// <summary>Moves with arrow actions to the target (rows first, then columns) and selects it.</summary>
static bool SelectCell(CommunicationSession s, Func<Cell, bool> pick)
{
    if (Find(s, pick) is not { } target) return false;
    for (var guard = 0; guard < 60 && s.FocusRow != target.Row; guard++)
        s.HandleAction(s.FocusRow < target.Row ? InputAction.MoveDown : InputAction.MoveUp);
    for (var guard = 0; guard < 60 && s.FocusCol != target.Col; guard++)
        s.HandleAction(s.FocusCol < target.Col ? InputAction.MoveRight : InputAction.MoveLeft);
    if (s.FocusRow != target.Row || s.FocusCol != target.Col) return false;
    s.HandleAction(InputAction.Select);
    return true;
}


sealed class SilentEngine : ISpeechEngine
{
    public event EventHandler<SpeechEndedEventArgs>? Ended;
    public void Speak(Utterance utterance) => Ended?.Invoke(this, new SpeechEndedEventArgs(utterance.Id, false));
    public void CancelAll() { }
}
