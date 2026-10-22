using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using Aac.Core.Speech;
using Aac.Core.Storage;
using NAudio.Wave;
using SherpaOnnx;

namespace Aac.App;

/// <summary>
/// Where natural voices live: %LOCALAPPDATA%\BetterTalk\voices\{pack id}. They are large, so they are
/// kept out of the roaming settings folder, and test copies share them.
/// </summary>
public static class VoicePacks
{
    public static readonly string Root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BetterTalk", "voices");

    public static string FolderOf(VoicePack pack) => Path.Combine(Root, pack.Id);

    public static bool IsInstalled(VoicePack pack) => FindModel(FolderOf(pack)) is not null;

    /// <summary>The voice model inside an unpacked pack (the archive holds one folder with the .onnx in it).</summary>
    public static string? FindModel(string folder) =>
        Directory.Exists(folder) ? Directory.EnumerateFiles(folder, "*.onnx", SearchOption.AllDirectories).FirstOrDefault() : null;

    /// <summary>Removes a downloaded voice from this computer (it can be downloaded again).</summary>
    public static void Delete(VoicePack pack)
    {
        var folder = FolderOf(pack);
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(30) };

    /// <summary>
    /// Downloads and unpacks a voice (once). Progress is 0..1 while downloading. The archive goes to a
    /// temporary file first and is unpacked into a fresh folder, so a cancelled or failed download never
    /// leaves a half-installed voice behind.
    /// </summary>
    public static async Task InstallAsync(VoicePack pack, IProgress<double>? progress, CancellationToken cancel)
    {
        if (IsInstalled(pack)) return;
        Directory.CreateDirectory(Root);
        var archive = Path.Combine(Root, pack.Id + ".tar.bz2.part");
        var staging = Path.Combine(Root, pack.Id + ".unpacking");
        try
        {
            using (var response = await Http.GetAsync(pack.Url, HttpCompletionOption.ResponseHeadersRead, cancel))
            {
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength ?? (long)(pack.SizeMb * 1e6);
                await using var from = await response.Content.ReadAsStreamAsync(cancel);
                await using var to = File.Create(archive);
                var buffer = new byte[1 << 16];
                long done = 0;
                int n;
                while ((n = await from.ReadAsync(buffer, cancel)) > 0)
                {
                    await to.WriteAsync(buffer.AsMemory(0, n), cancel);
                    done += n;
                    progress?.Report(Math.Min(1, done / (double)total));
                }
            }

            // Windows 10 and 11 include tar (bsdtar), which unpacks .tar.bz2.
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
            Directory.CreateDirectory(staging);
            var tar = Path.Combine(Environment.SystemDirectory, "tar.exe");
            var start = new ProcessStartInfo(tar) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
            foreach (var a in new[] { "-xjf", archive, "-C", staging }) start.ArgumentList.Add(a);
            using (var p = Process.Start(start) ?? throw new IOException("Could not start tar to unpack the voice."))
            {
                var errors = await p.StandardError.ReadToEndAsync(cancel);
                await p.WaitForExitAsync(cancel);
                if (p.ExitCode != 0) throw new IOException($"Unpacking the voice failed: {errors.Trim()}");
            }
            if (FindModel(staging) is null) throw new IOException("The downloaded voice has no model in it.");
            var folder = FolderOf(pack);
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
            Directory.Move(staging, folder);
        }
        finally
        {
            try { File.Delete(archive); } catch (IOException) { }
            try { if (Directory.Exists(staging)) Directory.Delete(staging, true); } catch (IOException) { }
        }
    }
}

/// <summary>
/// Speaks with a natural (Piper, Kokoro or Kitten) voice on this computer, through sherpa-onnx, and plays it with NAudio.
/// Audio starts as soon as the first sentence is ready (about a tenth of a second); later sentences are
/// made while the first is playing. Ended fires once per utterance, on the thread that created the engine.
/// </summary>
public sealed class NaturalSpeechEngine : ISpeechEngine, ISpeechProgress, IDisposable
{
    private readonly UtteranceQueue _queue = new();
    private readonly Task<OfflineTts> _tts;
    private readonly SynchronizationContext? _context = SynchronizationContext.Current;
    private readonly Thread _worker;
    private readonly float _speed;
    private readonly float _volume;
    private readonly int _speaker;
    private volatile WaveOutEvent? _playing;
    private volatile bool _disposed;

    public NaturalSpeechEngine(VoiceChoice voice, SpeechSettings settings)
    {
        if (voice.Pack is null) throw new ArgumentException("Not a natural voice.", nameof(voice));
        Voice = voice;
        _settings = settings;
        _speed = VoiceCatalog.SpeedFor(settings.Rate);
        _volume = Math.Clamp(settings.Volume, 0, 100) / 100f;
        _speaker = voice.Speaker;
        var folder = VoicePacks.FolderOf(voice.Pack);
        // Loading takes a couple of seconds, so it happens in the background; the first Speak waits for it.
        _tts = Task.Run(() => Load(voice, folder));
        _worker = new Thread(Run) { IsBackground = true, Name = "Natural voice", Priority = ThreadPriority.AboveNormal };
        _worker.Start();
    }

    public VoiceChoice Voice { get; }
    public event EventHandler<SpeechEndedEventArgs>? Ended;
    /// <summary>Roughly which character is being said, from how much of the audio has played (about 20 times a second).</summary>
    public event EventHandler<SpeechProgressEventArgs>? Progress;
    /// <summary>Raised (once) if the natural voice cannot be used; speech then falls back to the Windows voice.</summary>
    public event EventHandler<string>? Problem;
    private System.Speech.Synthesis.SpeechSynthesizer? _fallback;
    private readonly SpeechSettings _settings;

    /// <summary>Waits for the voice to finish loading; false (with the reason) if it could not be loaded.</summary>
    public bool WaitUntilLoaded(out string? problem)
    {
        try { _tts.GetAwaiter().GetResult(); problem = null; return true; }
        catch (Exception ex) { problem = ex.Message; return false; }
    }

    private static OfflineTts Load(VoiceChoice voice, string folder)
    {
        var model = VoicePacks.FindModel(folder) ?? throw new FileNotFoundException("The voice is not downloaded.");
        var dir = Path.GetDirectoryName(model)!;
        var config = new OfflineTtsConfig();
        if (voice.Engine == VoiceEngineKind.Kokoro)
        {
            config.Model.Kokoro.Model = model;
            config.Model.Kokoro.Voices = Path.Combine(dir, "voices.bin");
            config.Model.Kokoro.Tokens = Path.Combine(dir, "tokens.txt");
            config.Model.Kokoro.DataDir = Path.Combine(dir, "espeak-ng-data");
            config.Model.Kokoro.DictDir = Path.Combine(dir, "dict");
            // British voices get British pronunciations.
            var british = voice.Key.StartsWith(VoiceCatalog.KokoroPrefix + "b", StringComparison.Ordinal);
            config.Model.Kokoro.Lexicon = Path.Combine(dir, british ? "lexicon-gb-en.txt" : "lexicon-us-en.txt");
            config.Model.Kokoro.LengthScale = 1;
        }
        else if (voice.Engine == VoiceEngineKind.Kitten)
        {
            config.Model.Kitten.Model = model;
            config.Model.Kitten.Voices = Path.Combine(dir, "voices.bin");
            config.Model.Kitten.Tokens = Path.Combine(dir, "tokens.txt");
            config.Model.Kitten.DataDir = Path.Combine(dir, "espeak-ng-data");
            config.Model.Kitten.LengthScale = 1;
        }
        else
        {
            config.Model.Vits.Model = model;
            config.Model.Vits.Tokens = Path.Combine(dir, "tokens.txt");
            config.Model.Vits.DataDir = Path.Combine(dir, "espeak-ng-data");
            config.Model.Vits.NoiseScale = 0.667f;
            config.Model.Vits.NoiseScaleW = 0.8f;
            config.Model.Vits.LengthScale = 1;
        }
        config.Model.NumThreads = Math.Clamp(Environment.ProcessorCount / 2, 1, 4);
        config.Model.Provider = "cpu";
        config.MaxNumSentences = 1; // one sentence at a time, so speech starts after the first
        var tts = new OfflineTts(config);
        tts.Generate("Ready.", 1, voice.Speaker); // the first run is slow; do it now rather than on the user's first message
        return tts;
    }

    public void Speak(Utterance utterance)
    {
        if (_disposed) return;
        _queue.Add(utterance);
    }

    /// <summary>Stop: the sound stops at once, and the utterance ends now (not when the voice finishes its sentence).</summary>
    public void CancelAll()
    {
        // Only the utterances there now are stopped, each by its own signal: one spoken right after this
        // (a quick phrase or word cutting in) starts fresh and cannot bring a stopped one back.
        var stopped = _queue.StopAll();
        try { _playing?.Stop(); } catch (InvalidOperationException) { }
        foreach (var u in stopped) RaiseEnded(u.Id, true);
    }

    private void Run()
    {
        while (_queue.Next() is { } job)
        {
            var cancelled = true;
            try { cancelled = !Play(job.Utterance, job.Stopped); }
            catch (Exception ex) { Trace.WriteLine($"Natural voice failed: {ex}"); }
            if (_queue.Finish(job)) RaiseEnded(job.Utterance.Id, cancelled || job.IsStopped);
        }
    }

    /// <summary>Makes and plays one utterance; false if it was stopped.</summary>
    private bool Play(Utterance utterance, CancellationToken stop)
    {
        var clock = Stopwatch.StartNew();
        if (stop.IsCancellationRequested) return false;
        OfflineTts tts;
        try { tts = _tts.GetAwaiter().GetResult(); }
        catch (Exception ex) { return PlayWithWindowsVoice(utterance, ex.Message, stop); }
        var buffer = new BufferedWaveProvider(new WaveFormat(tts.SampleRate, 16, 1))
        {
            BufferDuration = TimeSpan.FromMinutes(5),
            ReadFully = true, // keep the device running (silence) between sentences
            DiscardOnBufferOverflow = true,
        };
        using var output = new WaveOutEvent { DesiredLatency = 120, NumberOfBuffers = 3 };
        output.Init(buffer);
        _playing = output;
        var started = false;
        var pcm = Array.Empty<byte>();

        // The voice makes one sentence at a time; each piece of audio is matched to its sentence (where it
        // starts in the text and how long it is), and within it the position moves through the letters in
        // step with the sound played. An ellipsis is a pause: silence between the parts either side of it.
        var pieces = new List<(long Start, int Samples, int CharStart, int CharLength)>();
        long made = 0;
        var gate = new object();
        var latency = tts.SampleRate * output.DesiredLatency / 1000;
        var lastPosition = -1;
        using var follow = new System.Threading.Timer(_ =>
        {
            int position;
            lock (gate)
            {
                if (!started || pieces.Count == 0) return;
                var played = Math.Max(0, made - buffer.BufferedBytes / 2 - latency);
                var k = pieces.FindLastIndex(p => p.Start <= played);
                if (k < 0) return;
                var piece = pieces[k];
                var within = Math.Clamp((played - piece.Start) / (double)Math.Max(1, piece.Samples), 0, 1);
                position = piece.CharStart + (int)(within * piece.CharLength);
            }
            if (position == lastPosition || stop.IsCancellationRequested) return;
            lastPosition = position;
            var args = new SpeechProgressEventArgs(utterance.Id, position);
            if (_context is null) Progress?.Invoke(this, args);
            else _context.Post(_ => Progress?.Invoke(this, args), null);
        }, null, 50, 50);

        var parts = SpeechPauses.Split(utterance.SpokenText);
        if (parts.Count == 0) parts = new[] { (utterance.SpokenText, 0) };
        for (var part = 0; part < parts.Count && !stop.IsCancellationRequested; part++)
        {
            var (text, offset) = parts[part];
            if (part > 0)
            {
                var gap = tts.SampleRate * SpeechPauses.PauseMs / 1000;
                var (prevText, prevOffset) = parts[part - 1];
                lock (gate)
                {
                    buffer.AddSamples(new byte[gap * 2], 0, gap * 2);
                    pieces.Add((made, gap, prevOffset + prevText.Length, 0));
                    made += gap;
                }
            }
            var sentences = System.Text.RegularExpressions.Regex.Matches(text, @"[^.!?;:]+[.!?;:]*")
                .Select(m => (Start: offset + m.Index, Length: m.Length)).ToList();
            if (sentences.Count == 0) sentences.Add((offset, text.Length));
            var chunk = 0;
            tts.GenerateWithCallback(text, _speed, _speaker, (samples, n) =>
            {
                if (stop.IsCancellationRequested) return 0; // stop making the rest
                if (pcm.Length < n * 2) pcm = new byte[n * 2];
                var floats = new float[n];
                Marshal.Copy(samples, floats, 0, n);
                for (var i = 0; i < n; i++)
                {
                    var v = (short)Math.Clamp(floats[i] * _volume * short.MaxValue, short.MinValue, short.MaxValue);
                    pcm[2 * i] = (byte)v;
                    pcm[2 * i + 1] = (byte)(v >> 8);
                }
                var sentence = sentences[Math.Min(chunk++, sentences.Count - 1)];
                lock (gate)
                {
                    buffer.AddSamples(pcm, 0, n * 2);
                    pieces.Add((made, n, sentence.Start, sentence.Length));
                    made += n;
                }
                if (!started && !stop.IsCancellationRequested)
                {
                    output.Play();
                    started = true;
                    PerfLog.Record("voice.firstAudio", clock);
                }
                return 1;
            });
        }

        if (!started) { _playing = null; return !stop.IsCancellationRequested; }
        // Let what is queued play out (plus the device's own buffers), unless the user presses Stop.
        while (!stop.IsCancellationRequested && buffer.BufferedBytes > 0) Thread.Sleep(15);
        if (!stop.IsCancellationRequested) Thread.Sleep(output.DesiredLatency);
        _playing = null;
        output.Stop();
        PerfLog.Record("voice.utterance", clock);
        return !stop.IsCancellationRequested;
    }

    /// <summary>
    /// If the natural voice could not be loaded (a damaged or deleted download), the user still gets speech:
    /// the Windows voice says it instead, and a notice says why.
    /// </summary>
    private bool PlayWithWindowsVoice(Utterance utterance, string reason, CancellationToken stop)
    {
        if (_fallback is null)
        {
            _fallback = new System.Speech.Synthesis.SpeechSynthesizer();
            _fallback.SetOutputToDefaultAudioDevice();
            _fallback.Rate = Math.Clamp(_settings.Rate, -10, 10);
            _fallback.Volume = Math.Clamp(_settings.Volume, 0, 100);
            var message = $"The {Voice.Name} voice could not be loaded ({reason}); using the Windows voice. Choose a voice again in Settings.";
            if (_context is null) Problem?.Invoke(this, message);
            else _context.Post(_ => Problem?.Invoke(this, message), null);
        }
        var prompt = _fallback.SpeakAsync(utterance.SpokenText);
        while (!prompt.IsCompleted)
        {
            if (stop.IsCancellationRequested) { _fallback.SpeakAsyncCancelAll(); return false; }
            Thread.Sleep(15);
        }
        return !stop.IsCancellationRequested;
    }

    private void RaiseEnded(long id, bool cancelled)
    {
        var args = new SpeechEndedEventArgs(id, cancelled);
        if (_context is null) Ended?.Invoke(this, args);
        else _context.Post(_ => Ended?.Invoke(this, args), null);
    }

    public void Dispose()
    {
        _disposed = true;
        _queue.Close();
        if (_tts.IsCompletedSuccessfully) _tts.Result.Dispose();
        _fallback?.Dispose();
    }
}
