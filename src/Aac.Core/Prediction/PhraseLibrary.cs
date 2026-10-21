namespace Aac.Core.Prediction;

public sealed class PhraseCategory
{
    public string Name { get; set; } = "";
    public List<string> Phrases { get; set; } = new();
}

/// <summary>Fixed, human-authored phrases. Nothing here is AI generated.</summary>
public sealed class PhraseLibrary
{
    public static readonly IReadOnlyList<string> DefaultPinnedPhrases = new[] { "I want a bag", "Tube Feeding please!", "Feet", "Q-tip", "Mom", "Echo... call mom", "Eye wipe" };
    public static readonly IReadOnlyList<string> DefaultQuickPhrases = new[]
    {
        "My quadstick tubes need blown out", "My quadstick tubes need reattached", "I need to Pee", "I need to Poop", "I wanna go to Bed",
    };

    /// <summary>The starting red and quick buttons of 0.1.0 to 0.3.4. A button file that still has exactly these was never changed by hand.</summary>
    private static readonly string[] OldDefaultPinned = { "Help!", "I need the bathroom.", "I'm in pain.", "Yes.", "No." };
    private static readonly string[] OldDefaultQuick =
    {
        "Yes.", "No.", "Wait.", "Stop.", "I need help.",
        "Please repeat that.", "That's not what I meant.", "Give me a moment.", "I'm uncomfortable.", "I need to reposition.",
    };
    /// <summary>Said to the person scratching, from the panel beside the Itch figure. Plain words that read right on their own.</summary>
    public static readonly IReadOnlyList<string> DefaultSpokenPhrases = new[] { "Up", "Down", "Left", "Right", "Keep going", "Scratch harder", "Scratch softer", "That's the spot", "Stop", "Thanks, that's better" };

    /// <summary>
    /// Urgent phrases shown in a slim strip on every page, spoken with one selection.
    /// Null (older settings files) means the defaults; an empty list hides the strip.
    /// </summary>
    public List<string>? PinnedPhrases { get; set; }
    public List<string> QuickPhrases { get; set; } = new();
    public List<PhraseCategory> Categories { get; set; } = new();

    public IReadOnlyList<string> Pinned => PinnedPhrases ?? DefaultPinnedPhrases;

    /// <summary>The "Speak out loud" words (Itch panel). Null (older button files) means the defaults.</summary>
    public List<string>? SpokenPhrases { get; set; }
    public IReadOnlyList<string> Spoken => SpokenPhrases ?? DefaultSpokenPhrases;

    /// <summary>1 once the direction words (Up, Down, Left, Right) are part of the spoken list and can be moved like the others.</summary>
    public int SpokenListVersion { get; set; }

    /// <summary>2 once the red and quick buttons are written out in the file, so a new version's starting buttons can never change them.</summary>
    public int ButtonsVersion { get; set; }

    /// <summary>
    /// Brings an older button file up to date, once; true when something changed (the caller saves it).
    /// Button files from before the arrows could be moved get them at the front of their spoken words.
    /// Red and quick rows that were never written out, or still are exactly the old starting buttons, get the
    /// new starting buttons; anything changed by hand is kept. Then every row is written out in full.
    /// </summary>
    public bool Upgrade()
    {
        var changed = false;
        if (SpokenListVersion < 1)
        {
            if (SpokenPhrases is { } words)
            {
                var missing = new[] { "Up", "Down", "Left", "Right" }.Where(d => !words.Contains(d, StringComparer.OrdinalIgnoreCase)).ToList();
                words.InsertRange(0, missing);
            }
            SpokenListVersion = 1;
            changed = true;
        }
        if (ButtonsVersion < 2)
        {
            var untouched = (PinnedPhrases is null || PinnedPhrases.SequenceEqual(OldDefaultPinned))
                && (QuickPhrases.Count == 0 || QuickPhrases.SequenceEqual(OldDefaultQuick));
            if (untouched)
            {
                PinnedPhrases = DefaultPinnedPhrases.ToList();
                QuickPhrases = DefaultQuickPhrases.ToList();
            }
            PinnedPhrases ??= DefaultPinnedPhrases.ToList();
            SpokenPhrases ??= DefaultSpokenPhrases.ToList();
            ButtonsVersion = 2;
            changed = true;
        }
        return changed;
    }

    public static PhraseLibrary CreateDefault() => new()
    {
        PinnedPhrases = DefaultPinnedPhrases.ToList(),
        SpokenPhrases = DefaultSpokenPhrases.ToList(),
        SpokenListVersion = 1,
        ButtonsVersion = 2,
        QuickPhrases = DefaultQuickPhrases.ToList(),
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
