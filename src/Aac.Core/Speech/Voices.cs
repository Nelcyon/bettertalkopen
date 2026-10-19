namespace Aac.Core.Speech;

public enum VoiceEngineKind
{
    /// <summary>A voice installed in Windows (SAPI): instant, but robotic.</summary>
    Windows,
    /// <summary>A Piper neural voice (VITS), run on this computer by sherpa-onnx: natural and fast.</summary>
    Piper,
    /// <summary>A Kokoro voice: the most natural, but it takes about a second before it starts speaking.</summary>
    Kokoro,
    /// <summary>A KittenTTS voice: small and natural, about a second before it starts speaking.</summary>
    Kitten,
}

/// <summary>A downloadable voice: one archive from the sherpa-onnx model releases, unpacked into its own folder.</summary>
public sealed record VoicePack(string Id, string Url, double SizeMb, string License, string Source);

/// <summary>
/// One choice in the voice list. <see cref="Key"/> is what the settings file stores; <see cref="Group"/>
/// is the heading it is listed under; <see cref="Speaker"/> picks the voice inside a pack of several.
/// </summary>
public sealed record VoiceChoice(string Key, VoiceEngineKind Engine, string Name, string Description, VoicePack? Pack = null, int Speaker = 0, string Group = "")
{
    public string Label => Engine == VoiceEngineKind.Windows ? $"{Name} (Windows voice)" : $"{Name}: {Description}";
}

/// <summary>
/// Every free English voice the app can download. All run offline on this computer (no account, and no
/// internet after the one-time download), packaged for sherpa-onnx by the k2-fsa project:
/// - Piper voices start speaking almost at once (about 0.1 s); 70 to 130 MB each. Licences come from each
///   voice's model card; all allow personal use, those marked NC not commercial use.
/// - Kokoro (Kokoro-82M, Apache 2.0) and KittenTTS (Apache 2.0) voices take about a second before speech
///   starts; each family is one download shared by all its voices.
/// Left out: voices whose licence could not be confirmed, voices imitating a game character, and the two
/// speaker sets too big for a list (VCTK, 109 speakers; LibriTTS, 904).
/// </summary>
public static class VoiceCatalog
{
    private const string Releases = "https://github.com/k2-fsa/sherpa-onnx/releases/download/tts-models/";

    /// <summary>Settings keys: "piper:{pack}[#speaker]", "kokoro:{voice}" or "kitten:{voice}" for a natural voice; anything else is a Windows voice name.</summary>
    public const string PiperPrefix = "piper:", KokoroPrefix = "kokoro:", KittenPrefix = "kitten:";

    public const string QuickGroup = "Natural voices: start speaking almost at once (each is its own download)";
    public const string SetsGroup = "More natural voices, in sets: one download per set";
    public const string KokoroGroup = "Most natural voices (Kokoro): about a second before speaking, one download for all";
    public const string KittenGroup = "Kitten voices: about a second before speaking, one download for all";

    private const string PiperSource = "Piper voices (rhasspy/piper-voices), packaged by sherpa-onnx";
    private const string Mimic3 = "Mycroft Mimic 3 voice licence (see github.com/MycroftAI/mimic3-voices)";
    private const string LibriVox = "Public domain (LibriVox recordings)";
    private const string HiFi = "CC BY-NC-SA 4.0 (Hi-Fi Captain)";
    private const string OpenSlr83 = "CC BY-SA 4.0 (OpenSLR 83)";

    /// <summary>Single Piper voices, men first then women, American then British.</summary>
    public static readonly IReadOnlyList<VoiceChoice> Natural = new[]
    {
        Piper("en_US-ryan-high", "Ryan", "American man, clear and warm", 115.6, "CC BY-NC-SA 4.0 (RyanSpeech)"),
        Piper("en_US-joe-medium", "Joe", "American man", 67.2, "CC0 (OHF voice datasets)"),
        Piper("en_US-john-medium", "John", "American man, older, calm", 67.2, LibriVox),
        Piper("en_US-norman-medium", "Norman", "American man, deep", 67.2, LibriVox),
        Piper("en_US-bryce-medium", "Bryce", "American man, younger", 67.3, LibriVox),
        Piper("en_US-hfc_male-medium", "Hi-Fi Male", "American man, crisp", 67.2, HiFi),
        Piper("en_US-kusal-medium", "Kusal", "man, Mycroft voice", 67.2, "Mycroft Mimic 2 voice licence (see github.com/MycroftAI/mimic2)"),
        Piper("en_US-danny-low", "Danny", "American man, lower quality", 67.1, Mimic3),
        Piper("en_US-reza_ibrahim-medium", "Reza", "man, lightly accented", 67.2, "CC0 (Hedayatfar dataset)"),
        Piper("en_US-sam-medium", "Sam", "American, non-binary voice", 67.2, "Apache 2.0 (Sam, non-binary voice project)"),
        Piper("en_US-lessac-high", "Lessac", "American woman, expressive", 115.5, "Blizzard 2013 research licence (non-commercial)"),
        Piper("en_US-kristin-medium", "Kristin", "American woman", 67.3, LibriVox),
        Piper("en_US-ljspeech-high", "Linda", "American woman, reading voice", 115.8, "Public domain (LJ Speech)"),
        Piper("en_US-hfc_female-medium", "Hi-Fi Female", "American woman, crisp", 67.2, HiFi),
        Piper("en_US-amy-medium", "Amy", "American woman", 67.2, Mimic3),
        Piper("en_US-kathleen-low", "Kathleen", "American woman, lower quality", 67.1, "CC0 (Kathleen dataset)"),
        Piper("en_GB-alan-medium", "Alan", "British man", 67.2, Mimic3),
        Piper("en_GB-northern_english_male-medium", "Northern", "British man, northern English", 67.2, OpenSlr83),
        Piper("en_GB-southern_english_male-medium", "Southern", "British man, southern English", 80.3, OpenSlr83),
        Piper("en_GB-cori-high", "Cori", "British woman", 115.6, LibriVox),
        Piper("en_GB-jenny_dioco-medium", "Jenny", "British woman", 67.2, "Jenny TTS dataset licence (attribution; see github.com/dioco-group/jenny-tts-dataset)"),
        Piper("en_GB-alba-medium", "Alba", "Scottish woman", 67.2, "CC BY 4.0 (University of Edinburgh)"),
        Piper("en_GB-southern_english_female-medium", "Southern Female", "British woman, southern English", 80.3, OpenSlr83),
    };

    private static readonly VoicePack Semaine = Pack("vits-piper-en_GB-semaine-medium", 80.2, "CC BY-NC-SA 4.0 (DFKI SEMAINE)", PiperSource);
    private static readonly VoicePack Arctic = Pack("vits-piper-en_US-arctic-medium", 80.3, "CMU ARCTIC licence (free use with attribution)", PiperSource);
    private static readonly VoicePack L2Arctic = Pack("vits-piper-en_US-l2arctic-medium", 80.3, "CC BY-NC 4.0 (L2-ARCTIC)", PiperSource);
    private static readonly VoicePack Aru = Pack("vits-piper-en_GB-aru-medium", 80.3, "CC BY 4.0 (University of Liverpool ARU corpus)", PiperSource);

    /// <summary>Piper packs with several speakers each: one download gives all the voices in the set.</summary>
    public static readonly IReadOnlyList<VoiceChoice> Sets = new VoiceChoice[]
    {
        Speaker(Semaine, 3, "Poppy", "British woman, cheerful (SEMAINE set)"),
        Speaker(Semaine, 0, "Prudence", "British woman, matter-of-fact (SEMAINE set)"),
        Speaker(Semaine, 1, "Spike", "British man, gruff (SEMAINE set)"),
        Speaker(Semaine, 2, "Obadiah", "British man, gloomy (SEMAINE set)"),
        Speaker(Arctic, 7, "Arctic BDL", "American man (CMU Arctic set)"),
        Speaker(Arctic, 1, "Arctic RMS", "American man, deep (CMU Arctic set)"),
        Speaker(Arctic, 6, "Arctic AEW", "American man (CMU Arctic set)"),
        Speaker(Arctic, 0, "Arctic AWB", "Scottish man (CMU Arctic set)"),
        Speaker(Arctic, 8, "Arctic JMK", "Canadian man (CMU Arctic set)"),
        Speaker(Arctic, 3, "Arctic KSP", "Indian man (CMU Arctic set)"),
        Speaker(Arctic, 2, "Arctic SLT", "American woman (CMU Arctic set)"),
        Speaker(Arctic, 4, "Arctic CLB", "American woman (CMU Arctic set)"),
        Speaker(Arctic, 5, "Arctic LNH", "American woman (CMU Arctic set)"),
        Speaker(Arctic, 11, "Arctic LJM", "American woman (CMU Arctic set)"),
        Speaker(Arctic, 16, "Arctic EEY", "American woman (CMU Arctic set)"),
        Speaker(Arctic, 9, "Arctic RXR", "man (CMU Arctic set)"),
        Speaker(Arctic, 10, "Arctic FEM", "man (CMU Arctic set)"),
        Speaker(Arctic, 14, "Arctic AHW", "man (CMU Arctic set)"),
        Speaker(Arctic, 13, "Arctic AUP", "man, Indian English (CMU Arctic set)"),
        Speaker(Arctic, 17, "Arctic GKA", "man, Indian English (CMU Arctic set)"),
        Speaker(Arctic, 12, "Arctic SLP", "woman, Indian English (CMU Arctic set)"),
        Speaker(Arctic, 15, "Arctic AXB", "woman, Indian English (CMU Arctic set)"),
        L2(21, "ABA", "man, Arabic accent"), L2(18, "YBAA", "man, Arabic accent"),
        L2(23, "SKA", "woman, Arabic accent"), L2(3, "ZHAA", "woman, Arabic accent"),
        L2(20, "BWC", "man, Mandarin accent"), L2(0, "TXHC", "man, Mandarin accent"),
        L2(12, "LXC", "woman, Mandarin accent"), L2(13, "NCC", "woman, Mandarin accent"),
        L2(10, "ASI", "man, Hindi accent"), L2(19, "RRBI", "man, Hindi accent"),
        L2(2, "SVBI", "woman, Hindi accent"), L2(9, "TNI", "woman, Hindi accent"),
        L2(16, "HKK", "man, Korean accent"), L2(14, "YKWK", "man, Korean accent"),
        L2(11, "HJK", "woman, Korean accent"), L2(15, "YDCK", "woman, Korean accent"),
        L2(22, "EBVS", "man, Spanish accent"), L2(6, "ERMS", "man, Spanish accent"),
        L2(7, "MBMPS", "woman, Spanish accent"), L2(17, "NJS", "woman, Spanish accent"),
        L2(8, "HQTV", "man, Vietnamese accent"), L2(5, "TLV", "man, Vietnamese accent"),
        L2(4, "PNV", "woman, Vietnamese accent"), L2(1, "THV", "woman, Vietnamese accent"),
        Speaker(Aru, 3, "Liverpool 01", "British speaker (ARU set)"), Speaker(Aru, 9, "Liverpool 02", "British speaker (ARU set)"),
        Speaker(Aru, 0, "Liverpool 03", "British speaker (ARU set)"), Speaker(Aru, 11, "Liverpool 04", "British speaker (ARU set)"),
        Speaker(Aru, 7, "Liverpool 05", "British speaker (ARU set)"), Speaker(Aru, 1, "Liverpool 06", "British speaker (ARU set)"),
        Speaker(Aru, 10, "Liverpool 07", "British speaker (ARU set)"), Speaker(Aru, 5, "Liverpool 08", "British speaker (ARU set)"),
        Speaker(Aru, 4, "Liverpool 09", "British speaker (ARU set)"), Speaker(Aru, 2, "Liverpool 10", "British speaker (ARU set)"),
        Speaker(Aru, 6, "Liverpool 11", "British speaker (ARU set)"), Speaker(Aru, 8, "Liverpool 12", "British speaker (ARU set)"),
    };

    private static readonly VoicePack KokoroPack = Pack("kokoro-multi-lang-v1_0", 349.9,
        "Apache 2.0 (Kokoro-82M by hexgrad)", "Kokoro-82M v1.0, packaged by sherpa-onnx");

    /// <summary>All of Kokoro's English voices, by their number in the model (from its speaker list).</summary>
    public static readonly IReadOnlyList<VoiceChoice> Kokoro = new[]
    {
        KokoroVoice("af_heart", 3, "Heart", "American woman, warm (the best Kokoro voice)"),
        KokoroVoice("af_bella", 2, "Bella", "American woman, lively"),
        KokoroVoice("af_nicole", 6, "Nicole", "American woman, soft and close"),
        KokoroVoice("af_sarah", 9, "Sarah", "American woman"),
        KokoroVoice("af_aoede", 1, "Aoede", "American woman"),
        KokoroVoice("af_kore", 5, "Kore", "American woman"),
        KokoroVoice("af_alloy", 0, "Alloy", "American woman"),
        KokoroVoice("af_jessica", 4, "Jessica", "American woman"),
        KokoroVoice("af_nova", 7, "Nova", "American woman"),
        KokoroVoice("af_river", 8, "River", "American woman"),
        KokoroVoice("af_sky", 10, "Sky", "American woman"),
        KokoroVoice("am_michael", 16, "Michael", "American man"),
        KokoroVoice("am_fenrir", 14, "Fenrir", "American man, deep"),
        KokoroVoice("am_puck", 18, "Puck", "American man, bright"),
        KokoroVoice("am_eric", 13, "Eric", "American man, steady"),
        KokoroVoice("am_adam", 11, "Adam", "American man, plain"),
        KokoroVoice("am_echo", 12, "Echo", "American man"),
        KokoroVoice("am_liam", 15, "Liam", "American man"),
        KokoroVoice("am_onyx", 17, "Onyx", "American man, low"),
        KokoroVoice("am_santa", 19, "Santa", "American man, jolly"),
        KokoroVoice("bf_emma", 21, "Emma", "British woman"),
        KokoroVoice("bf_isabella", 22, "Isabella", "British woman, bright"),
        KokoroVoice("bf_alice", 20, "Alice", "British woman"),
        KokoroVoice("bf_lily", 23, "Lily", "British woman"),
        KokoroVoice("bm_george", 26, "George", "British man, older"),
        KokoroVoice("bm_fable", 25, "Fable", "British man, storyteller"),
        KokoroVoice("bm_lewis", 27, "Lewis", "British man, deep"),
        KokoroVoice("bm_daniel", 24, "Daniel", "British man, calm"),
    };

    private static readonly VoicePack KittenPack = Pack("kitten-mini-en-v0_8", 67.5,
        "Apache 2.0 (KittenTTS mini 0.8 by KittenML)", "KittenTTS, packaged by sherpa-onnx");

    /// <summary>KittenTTS mini's eight voices (four men, four women), by their number in the model.</summary>
    public static readonly IReadOnlyList<VoiceChoice> Kitten = new[]
    {
        KittenVoice(0, "Kitten Man 2"), KittenVoice(2, "Kitten Man 3"), KittenVoice(4, "Kitten Man 4"), KittenVoice(6, "Kitten Man 5"),
        KittenVoice(1, "Kitten Woman 2"), KittenVoice(3, "Kitten Woman 3"), KittenVoice(5, "Kitten Woman 4"), KittenVoice(7, "Kitten Woman 5"),
    };

    /// <summary>Every downloadable voice, in list order: quick single voices, sets, Kokoro, Kitten.</summary>
    public static IEnumerable<VoiceChoice> All => Natural.Concat(Sets).Concat(Kokoro).Concat(Kitten);

    private static VoicePack Pack(string archive, double sizeMb, string license, string source) =>
        new(archive.StartsWith("vits-piper-", StringComparison.Ordinal) ? archive["vits-piper-".Length..] : archive,
            $"{Releases}{archive}.tar.bz2", sizeMb, license, source);

    private static VoiceChoice Piper(string id, string name, string description, double sizeMb, string license) =>
        new(PiperPrefix + id, VoiceEngineKind.Piper, name, description, Pack("vits-piper-" + id, sizeMb, license, PiperSource), 0, QuickGroup);

    private static VoiceChoice Speaker(VoicePack pack, int speaker, string name, string description) =>
        new($"{PiperPrefix}{pack.Id}#{speaker}", VoiceEngineKind.Piper, name, description, pack, speaker, SetsGroup);

    private static VoiceChoice L2(int speaker, string code, string description) =>
        Speaker(L2Arctic, speaker, $"L2 {code}", description + " (L2-ARCTIC set)");

    private static VoiceChoice KokoroVoice(string id, int speaker, string name, string description) =>
        new(KokoroPrefix + id, VoiceEngineKind.Kokoro, name, description, KokoroPack, speaker, KokoroGroup);

    private static VoiceChoice KittenVoice(int speaker, string name) =>
        new($"{KittenPrefix}{speaker}", VoiceEngineKind.Kitten, name, speaker % 2 == 0 ? "American man" : "American woman", KittenPack, speaker, KittenGroup);

    /// <summary>The natural voice a settings value names, or null for a Windows voice (or an unknown one).</summary>
    public static VoiceChoice? FindNatural(string? key) =>
        key is null ? null : All.FirstOrDefault(v => string.Equals(v.Key, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>The speed factor for a natural voice from the -10..10 speed setting (Windows' scale): about 0.55x to 1.8x.</summary>
    public static float SpeedFor(int rate) => (float)Math.Pow(1.06, Math.Clamp(rate, -10, 10));
}
