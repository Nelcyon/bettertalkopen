using System.Text;
using System.Text.Json;
using Aac.Core.Abstractions;

namespace Aac.Core.Speech;

/// <summary>
/// One program's sound on one speaker (a Windows audio session), as seen by <see cref="AudioDucker"/>.
/// Reading or setting the volume can fail (the program just closed its sound), so both say whether they worked.
/// </summary>
public interface IAudioSession
{
    /// <summary>This one session, while it lasts (device + session instance).</summary>
    string Id { get; }
    /// <summary>The program's sound on that device, the same each time the program runs (device + session identifier).</summary>
    string AppId { get; }
    int ProcessId { get; }
    bool TryGetVolume(out float volume);
    bool TrySetVolume(float volume);
}

/// <summary>Lists the sound sessions playing now, on every speaker. Never throws: no sound system means none.</summary>
public interface IAudioMixer
{
    IReadOnlyList<IAudioSession> Sessions();
}

/// <summary>A program BetterTalk turned down: its volume before, and what BetterTalk set it to.</summary>
public sealed class DuckedSound
{
    public string Id { get; set; } = "";
    public string AppId { get; set; } = "";
    public float Original { get; set; }
    public float Ducked { get; set; }
    public DateTimeOffset At { get; set; }
}

/// <summary>Remembers turned-down programs on disk, so a crash or a power cut never leaves other sounds quiet.</summary>
public interface IDuckedSoundStore
{
    IReadOnlyList<DuckedSound> Load();
    /// <summary>Saves the list; an empty list removes the file.</summary>
    void Save(IReadOnlyCollection<DuckedSound> sounds);
}

/// <summary>
/// Turns other programs' sound down while BetterTalk speaks, and back up exactly where it was afterwards.
/// <list type="bullet">
/// <item>Only other programs are touched: BetterTalk's own sound (its process) never is.</item>
/// <item>A program that starts playing during speech is turned down too.</item>
/// <item>Afterwards each program goes back to its own earlier volume, unless someone changed that volume
///   meanwhile (then their choice stays).</item>
/// <item>Windows remembers a program's volume after it closes, so a program that closed while turned down is
///   put back the next time it plays. The list is kept on disk until then, so a crash cannot leave things quiet.</item>
/// </list>
/// Not thread-safe: the app calls it from one background thread.
/// </summary>
public sealed class AudioDucker
{
    /// <summary>Volumes this close count as the same (Windows rounds them a little).</summary>
    public const float Tolerance = 0.015f;
    /// <summary>Gaps shorter than this between utterances keep the sound down, so it does not bob up and down.</summary>
    public const int HoldMs = 350;
    /// <summary>While turned down, programs that just started playing are looked for this often.</summary>
    public const int RefreshMs = 400;
    /// <summary>While programs that closed when turned down are waiting to be put back, they are looked for this often.</summary>
    public const int PendingCheckMs = 5000;
    /// <summary>A program not seen again in this long is forgotten (it was probably uninstalled).</summary>
    public static readonly TimeSpan ForgetAfter = TimeSpan.FromDays(30);

    private readonly IAudioMixer _mixer;
    private readonly int _ownProcessId;
    private readonly IDuckedSoundStore? _store;
    private readonly IClock _clock;
    private readonly Dictionary<string, DuckedSound> _ducked = new();
    private readonly List<DuckedSound> _pending = new();
    private DateTimeOffset? _quietSince;
    private DateTimeOffset _lastRefresh = DateTimeOffset.MinValue;

    public AudioDucker(IAudioMixer mixer, int ownProcessId, IClock clock, IDuckedSoundStore? store = null)
    {
        _mixer = mixer;
        _ownProcessId = ownProcessId;
        _clock = clock;
        _store = store;
        // Programs turned down when BetterTalk last stopped without putting them back (a crash).
        if (store is not null) _pending.AddRange(store.Load().Where(s => s.AppId.Length > 0));
    }

    /// <summary>How loud other programs stay while BetterTalk speaks: 0.5 = half their volume.</summary>
    public double Level { get; set; } = 0.5;

    public bool IsDucked { get; private set; }

    /// <summary>Programs that closed while turned down (or were left down by a crash) and are put back when next seen.</summary>
    public IReadOnlyList<DuckedSound> Pending => _pending;

    /// <summary>
    /// Called often (and whenever speaking starts or stops): turns other sound down when speaking starts,
    /// looks for new programs while speaking, and puts everything back a moment after speaking ends.
    /// </summary>
    public void Tick(bool speaking)
    {
        var now = _clock.Now;
        if (speaking)
        {
            _quietSince = null;
            if (!IsDucked) Duck();
            else if ((now - _lastRefresh).TotalMilliseconds >= RefreshMs) Refresh();
            return;
        }
        if (IsDucked)
        {
            _quietSince ??= now;
            if ((now - _quietSince.Value).TotalMilliseconds >= HoldMs) Restore();
            else if ((now - _lastRefresh).TotalMilliseconds >= RefreshMs) Refresh();
            return;
        }
        if (_pending.Count > 0 && (now - _lastRefresh).TotalMilliseconds >= PendingCheckMs) Refresh();
    }

    /// <summary>Turns every other program down now.</summary>
    public void Duck()
    {
        IsDucked = true;
        _quietSince = null;
        Refresh();
    }

    /// <summary>Puts every program BetterTalk turned down back now (also on exit).</summary>
    public void Restore()
    {
        IsDucked = false;
        _quietSince = null;
        var sessions = OtherSessions();
        foreach (var sound in _ducked.Values)
        {
            var session = sessions.FirstOrDefault(s => s.Id == sound.Id);
            if (session is null) _pending.Add(sound);
            else PutBack(session, sound);
        }
        _ducked.Clear();
        RestorePending(sessions);
        _lastRefresh = _clock.Now;
        Persist();
    }

    /// <summary>
    /// While turned down: turns down programs that started playing, and notes ones that stopped. Always: puts
    /// back programs that were left turned down earlier and are playing again.
    /// </summary>
    public void Refresh()
    {
        var sessions = OtherSessions();
        if (IsDucked)
        {
            foreach (var session in sessions)
            {
                if (_ducked.ContainsKey(session.Id) || !session.TryGetVolume(out var volume)) continue;
                var earlier = _pending.FindIndex(p => p.AppId == session.AppId && Same(p.Ducked, volume));
                if (earlier >= 0)
                {
                    // Still turned down from before (it closed while BetterTalk spoke, or BetterTalk crashed):
                    // it is already down, and goes back to its volume from before that.
                    var old = _pending[earlier];
                    _pending.RemoveAt(earlier);
                    _ducked[session.Id] = new DuckedSound { Id = session.Id, AppId = session.AppId, Original = old.Original, Ducked = volume, At = _clock.Now };
                    continue;
                }
                var ducked = (float)Math.Clamp(volume * Level, 0, 1);
                if (!session.TrySetVolume(ducked)) continue;
                _ducked[session.Id] = new DuckedSound { Id = session.Id, AppId = session.AppId, Original = volume, Ducked = ducked, At = _clock.Now };
            }
            // A program that stopped while turned down keeps that volume in Windows: put it back when it returns.
            foreach (var gone in _ducked.Values.Where(d => sessions.All(s => s.Id != d.Id)).ToList())
            {
                _ducked.Remove(gone.Id);
                _pending.Add(gone);
            }
        }
        else RestorePending(sessions);
        _pending.RemoveAll(p => _clock.Now - p.At > ForgetAfter);
        _lastRefresh = _clock.Now;
        Persist();
    }

    private void RestorePending(IReadOnlyList<IAudioSession> sessions)
    {
        foreach (var session in sessions)
        {
            if (!session.TryGetVolume(out var volume)) continue;
            var i = _pending.FindIndex(p => p.AppId == session.AppId && Same(p.Ducked, volume));
            if (i < 0) continue;
            if (session.TrySetVolume(_pending[i].Original)) _pending.RemoveAt(i);
        }
    }

    private static void PutBack(IAudioSession session, DuckedSound sound)
    {
        // Someone moved this program's volume while BetterTalk spoke: leave their choice.
        if (!session.TryGetVolume(out var now) || !Same(now, sound.Ducked)) return;
        session.TrySetVolume(sound.Original);
    }

    private static bool Same(float a, float b) => Math.Abs(a - b) <= Tolerance;

    private List<IAudioSession> OtherSessions() =>
        _mixer.Sessions().Where(s => s.ProcessId != _ownProcessId).ToList();

    private string? _saved; // null: nothing written yet this run, so the first check always writes

    private void Persist()
    {
        if (_store is null) return;
        var all = _ducked.Values.Concat(_pending).ToList();
        // Only write when something changed (Refresh runs a few times a second while speaking).
        var key = string.Join("\n", all.Select(s => $"{s.Id}|{s.AppId}|{s.Original}|{s.Ducked}"));
        if (key == _saved) return;
        _store.Save(all);
        _saved = key;
    }
}

/// <summary>The list of turned-down programs, as a small JSON file that exists only while some are down.</summary>
public sealed class DuckedSoundFile(string path) : IDuckedSoundStore
{
    public IReadOnlyList<DuckedSound> Load()
    {
        try
        {
            if (!File.Exists(path)) return [];
            return JsonSerializer.Deserialize<List<DuckedSound>>(File.ReadAllText(path)) ?? [];
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or NotSupportedException)
        {
            return [];
        }
    }

    public void Save(IReadOnlyCollection<DuckedSound> sounds)
    {
        try
        {
            if (sounds.Count == 0)
            {
                File.Delete(path);
                return;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            var tmp = path + ".tmp";
            File.WriteAllBytes(tmp, Encoding.UTF8.GetBytes(JsonSerializer.Serialize(sounds)));
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
