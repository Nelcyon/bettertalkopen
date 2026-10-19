namespace Aac.Core.Prediction;

public sealed class PhraseCategory
{
    public string Name { get; set; } = "";
    public List<string> Phrases { get; set; } = new();
}

/// <summary>Fixed, human-authored phrases. Nothing here is AI generated.</summary>
public sealed class PhraseLibrary
{
    public static readonly IReadOnlyList<string> DefaultPinnedPhrases = new[] { "Help!", "I need the bathroom.", "I'm in pain.", "Yes.", "No." };

    /// <summary>
    /// Urgent phrases shown in a slim strip on every page, spoken with one selection.
    /// Null (older settings files) means the defaults; an empty list hides the strip.
    /// </summary>
    public List<string>? PinnedPhrases { get; set; }
    public List<string> QuickPhrases { get; set; } = new();
    public List<PhraseCategory> Categories { get; set; } = new();

    public IReadOnlyList<string> Pinned => PinnedPhrases ?? DefaultPinnedPhrases;

    public static PhraseLibrary CreateDefault() => new()
    {
        PinnedPhrases = DefaultPinnedPhrases.ToList(),
        QuickPhrases = new()
        {
            "Yes.", "No.", "Wait.", "Stop.", "I need help.",
            "Please repeat that.", "That's not what I meant.", "Give me a moment.", "I'm uncomfortable.", "I need to reposition.",
        },
        Categories = new()
        {
            new() { Name = "Needs", Phrases = { "I need water.", "I need to go to the bathroom.", "Please scratch my nose.", "Please adjust my head.", "I'm too hot.", "I'm too cold.", "I need my medication." } },
            new() { Name = "Questions", Phrases = { "What time is it?", "How are you?", "What did the doctor say?", "When are you coming back?", "Who is coming today?", "What do you think?" } },
            new() { Name = "Social", Phrases = { "Good morning.", "Thank you.", "I love you.", "How was your day?", "Tell me about your day.", "See you later." } },
            new() { Name = "Opinions", Phrases = { "I agree.", "I don't agree.", "I think that's a good idea.", "That's ridiculous.", "I'm not sure.", "Let me think about it." } },
            new() { Name = "Entertainment", Phrases = { "Can you put on a movie?", "Turn up the volume, please.", "Turn down the volume, please.", "Pause it, please.", "Did you see the game?", "That was a great episode." } },
            new() { Name = "Computer & games", Phrases = { "The computer isn't working.", "Please restart the computer.", "Can you check my QuadStick?", "I'm going to play for a while.", "Please plug in the controller." } },
            new() { Name = "Personal", Phrases = { } },
        },
    };
}
