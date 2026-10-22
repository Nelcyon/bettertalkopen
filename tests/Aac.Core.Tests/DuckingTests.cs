using Aac.Core.Abstractions;
using Aac.Core.Speech;

namespace Aac.Core.Tests;

public class DuckingTests
{
    private const int Own = 100;
    private readonly ManualClock _clock = new();
    private readonly FakeMixer _mixer = new();
    private readonly MemoryStore _store = new();

    private AudioDucker Make(double level = 0.5) => new(_mixer, Own, _clock, _store) { Level = level };

    [Fact]
    public void Other_programs_go_to_half_while_speaking_and_back_exactly_after()
    {
        var music = _mixer.Add("music", pid: 1, volume: 0.8f);
        var tv = _mixer.Add("tv", pid: 2, volume: 0.3f);
        var d = Make();

        d.Tick(speaking: true);
        Assert.Equal(0.4f, music.Volume, 3);
        Assert.Equal(0.15f, tv.Volume, 3);

        d.Tick(speaking: false);
        _clock.AdvanceMs(AudioDucker.HoldMs);
        d.Tick(speaking: false);
        Assert.False(d.IsDucked);
        Assert.Equal(0.8f, music.Volume);
        Assert.Equal(0.3f, tv.Volume);
        Assert.Empty(_store.Saved);
    }

    [Fact]
    public void BetterTalks_own_sound_is_never_touched()
    {
        var own = _mixer.Add("bettertalk", pid: Own, volume: 1f);
        var d = Make();
        d.Tick(true);
        Assert.Equal(1f, own.Volume);
        Assert.Equal(0, own.Sets);
    }

    [Fact]
    public void The_level_setting_decides_how_quiet()
    {
        var music = _mixer.Add("music", pid: 1, volume: 1f);
        var d = Make(level: 0.2);
        d.Duck();
        Assert.Equal(0.2f, music.Volume, 3);
    }

    [Fact]
    public void A_short_gap_between_utterances_keeps_the_sound_down()
    {
        var music = _mixer.Add("music", pid: 1, volume: 1f);
        var d = Make();
        d.Tick(true);
        d.Tick(false);
        _clock.AdvanceMs(AudioDucker.HoldMs - 50);
        d.Tick(false);
        d.Tick(true);
        Assert.True(d.IsDucked);
        Assert.Equal(1, music.Sets); // turned down once, not bobbing up and down
        Assert.Equal(0.5f, music.Volume, 3);
    }

    [Fact]
    public void A_program_that_starts_during_speech_is_turned_down_and_back_up()
    {
        var d = Make();
        d.Tick(true);
        var late = _mixer.Add("video", pid: 3, volume: 0.6f);
        _clock.AdvanceMs(AudioDucker.RefreshMs);
        d.Tick(true);
        Assert.Equal(0.3f, late.Volume, 3);
        d.Restore();
        Assert.Equal(0.6f, late.Volume);
    }

    [Fact]
    public void A_volume_someone_changed_during_speech_is_left_alone()
    {
        var music = _mixer.Add("music", pid: 1, volume: 0.8f);
        var d = Make();
        d.Duck();
        music.Volume = 0.1f; // turned down by hand in the Windows mixer
        d.Restore();
        Assert.Equal(0.1f, music.Volume);
    }

    [Fact]
    public void A_program_that_closed_while_down_is_put_back_when_it_plays_again()
    {
        var music = _mixer.Add("music", pid: 1, volume: 0.8f);
        var d = Make();
        d.Duck();
        _mixer.Remove(music);
        d.Restore();
        Assert.Single(d.Pending);
        Assert.Single(_store.Saved);

        // Windows remembers the turned-down volume for the program's next run (a new session).
        var again = _mixer.Add("music", pid: 7, volume: 0.4f, instance: "run2");
        _clock.AdvanceMs(AudioDucker.PendingCheckMs);
        d.Tick(false);
        Assert.Equal(0.8f, again.Volume);
        Assert.Empty(d.Pending);
        Assert.Empty(_store.Saved);
    }

    [Fact]
    public void A_program_that_closed_while_down_and_returns_during_speech_is_not_halved_twice()
    {
        var music = _mixer.Add("music", pid: 1, volume: 0.8f);
        var d = Make();
        d.Duck();
        _mixer.Remove(music);
        _clock.AdvanceMs(AudioDucker.RefreshMs);
        d.Tick(true);
        var again = _mixer.Add("music", pid: 7, volume: 0.4f, instance: "run2");
        _clock.AdvanceMs(AudioDucker.RefreshMs);
        d.Tick(true);
        Assert.Equal(0.4f, again.Volume);
        d.Restore();
        Assert.Equal(0.8f, again.Volume);
    }

    [Fact]
    public void After_a_crash_the_next_start_puts_other_sound_back()
    {
        var music = _mixer.Add("music", pid: 1, volume: 0.8f);
        var crashed = Make();
        crashed.Duck(); // ...and BetterTalk dies here, without Restore
        Assert.Equal(0.4f, music.Volume, 3);
        Assert.Single(_store.Saved);

        var next = Make();
        next.Refresh(); // what the app does on start
        Assert.Equal(0.8f, music.Volume);
        Assert.Empty(_store.Saved);
    }

    [Fact]
    public void Old_leftovers_are_forgotten_after_a_month()
    {
        var music = _mixer.Add("music", pid: 1, volume: 0.8f);
        var d = Make();
        d.Duck();
        _mixer.Remove(music);
        d.Restore();
        _clock.Advance(AudioDucker.ForgetAfter + TimeSpan.FromDays(1));
        d.Refresh();
        Assert.Empty(d.Pending);
        Assert.Empty(_store.Saved);
    }

    [Fact]
    public void A_session_that_refuses_a_volume_change_is_skipped()
    {
        var stuck = _mixer.Add("stuck", pid: 1, volume: 0.8f);
        stuck.Fails = true;
        var music = _mixer.Add("music", pid: 2, volume: 0.8f);
        var d = Make();
        d.Duck();
        Assert.Equal(0.4f, music.Volume, 3);
        d.Restore();
        Assert.Equal(0.8f, music.Volume);
        Assert.Empty(_store.Saved);
    }

    [Fact]
    public void The_file_store_round_trips_and_removes_itself_when_empty()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bt-duck-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "ducked.json");
        try
        {
            var file = new DuckedSoundFile(path);
            Assert.Empty(file.Load());
            file.Save([new DuckedSound { Id = "a", AppId = "app", Original = 0.8f, Ducked = 0.4f, At = _clock.Now }]);
            var back = Assert.Single(file.Load());
            Assert.Equal(("a", "app", 0.8f, 0.4f), (back.Id, back.AppId, back.Original, back.Ducked));
            file.Save([]);
            Assert.False(File.Exists(path));
            File.WriteAllText(path, "not json");
            Assert.Empty(file.Load());
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    private sealed class FakeSession(string app, string instance, int pid, float volume) : IAudioSession
    {
        public string Id { get; } = $"dev|{app}|{instance}";
        public string AppId { get; } = $"dev|{app}";
        public int ProcessId { get; } = pid;
        public float Volume { get; set; } = volume;
        public int Sets { get; private set; }
        public bool Fails { get; set; }

        public bool TryGetVolume(out float v)
        {
            v = Volume;
            return !Fails;
        }

        public bool TrySetVolume(float v)
        {
            if (Fails) return false;
            Volume = v;
            Sets++;
            return true;
        }
    }

    private sealed class FakeMixer : IAudioMixer
    {
        private readonly List<FakeSession> _sessions = new();
        public FakeSession Add(string app, int pid, float volume, string instance = "run1")
        {
            var s = new FakeSession(app, instance, pid, volume);
            _sessions.Add(s);
            return s;
        }
        public void Remove(FakeSession s) => _sessions.Remove(s);
        public IReadOnlyList<IAudioSession> Sessions() => _sessions.ToList();
    }

    private sealed class MemoryStore : IDuckedSoundStore
    {
        public List<DuckedSound> Saved { get; private set; } = new();
        public IReadOnlyList<DuckedSound> Load() => Saved.Select(s => new DuckedSound { Id = s.Id, AppId = s.AppId, Original = s.Original, Ducked = s.Ducked, At = s.At }).ToList();
        public void Save(IReadOnlyCollection<DuckedSound> sounds) => Saved = sounds.ToList();
    }
}

public class DuckingLevelSettingTests
{
    [Fact]
    public void Other_sounds_go_to_a_quarter_by_default_and_the_old_half_setting_does_not_carry_over()
    {
        Assert.Equal(25, new Aac.Core.Storage.SpeechSettings().OtherSoundsLevel);
        var from040 = System.Text.Json.JsonSerializer.Deserialize<Aac.Core.Storage.SpeechSettings>(
            """{ "DuckOtherSounds": true, "DuckOtherSoundsTo": 50 }""", Aac.Core.Storage.SettingsStore.JsonOptions)!;
        Assert.Equal(25, from040.OtherSoundsLevel);
        Assert.True(from040.DuckOtherSounds);
    }
}
