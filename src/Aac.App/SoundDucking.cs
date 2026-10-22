using System.Runtime.InteropServices;
using Aac.Core.Abstractions;
using Aac.Core.Speech;
using Aac.Core.Storage;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace Aac.App;

/// <summary>
/// Turns other programs' sound down while BetterTalk speaks (Settings → Voice), on a background thread of its
/// own (Windows' audio session objects want one thread, and never the UI's). Also puts back sound a crash left
/// turned down, at every start, even with the setting off.
/// </summary>
public sealed class SoundDucking : IDisposable
{
    private readonly SpeechController _speech;
    private readonly bool _enabled;
    private readonly double _level;
    private readonly AutoResetEvent _wake = new(false);
    private readonly Thread _thread;
    private volatile bool _stopping;

    public SoundDucking(SpeechController speech, SpeechSettings settings)
    {
        _speech = speech;
        _enabled = settings.DuckOtherSounds;
        _level = Math.Clamp(settings.DuckOtherSoundsTo, 0, 100) / 100.0;
        // Speaking starting or stopping wakes the thread at once, so the sound drops as the voice begins.
        speech.StateChanged += (_, _) => _wake.Set();
        _thread = new Thread(Run) { IsBackground = true, Name = "Sound ducking" };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
    }

    private void Run()
    {
        WindowsAudioMixer? mixer = null;
        try
        {
            mixer = new WindowsAudioMixer();
            var ducker = new AudioDucker(mixer, Environment.ProcessId, SystemClock.Instance, new DuckedSoundFile(AppPaths.DuckedSounds)) { Level = _level };
            ducker.Refresh(); // anything a crash left turned down goes back up now
            while (!_stopping)
            {
                _wake.WaitOne(ducker.IsDucked ? 100 : 1000);
                if (_stopping) break;
                try { ducker.Tick(_enabled && _speech.IsSpeaking); }
                catch (Exception ex) { App.LogError(ex); }
            }
            ducker.Restore();
        }
        catch (Exception ex) { App.LogError(ex); }
        finally { mixer?.Dispose(); }
    }

    /// <summary>Puts other programs' sound back and stops (on exit, and when BetterTalk is crashing).</summary>
    public void Dispose()
    {
        _stopping = true;
        _wake.Set();
        if (Thread.CurrentThread != _thread) _thread.Join(TimeSpan.FromSeconds(2));
    }
}

/// <summary>Every program's sound session on every active speaker or headset, through the Windows Core Audio API.</summary>
internal sealed class WindowsAudioMixer : IAudioMixer, IDisposable
{
    private readonly MMDeviceEnumerator _devices = new();
    // Windows' session objects from the last listing, released at the next one.
    private readonly List<IDisposable> _held = new();

    public IReadOnlyList<IAudioSession> Sessions()
    {
        Release();
        var list = new List<IAudioSession>();
        try
        {
            foreach (var device in _devices.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                _held.Add(device);
                try
                {
                    var sessions = device.AudioSessionManager.Sessions;
                    for (var i = 0; i < sessions.Count; i++)
                    {
                        var control = sessions[i];
                        _held.Add(control);
                        if (control.State == AudioSessionState.AudioSessionStateExpired) continue;
                        list.Add(new Session(device.ID, control));
                    }
                }
                catch (Exception ex) when (ex is COMException or InvalidCastException or UnauthorizedAccessException) { }
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or UnauthorizedAccessException) { }
        return list;
    }

    private void Release()
    {
        // Sessions before the devices they came from.
        for (var i = _held.Count - 1; i >= 0; i--)
        {
            try { _held[i].Dispose(); }
            catch (Exception ex) when (ex is COMException or InvalidCastException) { }
        }
        _held.Clear();
    }

    public void Dispose()
    {
        Release();
        _devices.Dispose();
    }

    private sealed class Session(string device, AudioSessionControl control) : IAudioSession
    {
        public string Id { get; } = device + "|" + Read<string?>(() => control.GetSessionInstanceIdentifier, null);
        public string AppId { get; } = device + "|" + Read<string?>(() => control.GetSessionIdentifier, null);
        public int ProcessId { get; } = (int)Read(() => control.GetProcessID, 0u);

        public bool TryGetVolume(out float volume)
        {
            try
            {
                volume = control.SimpleAudioVolume.Volume;
                return true;
            }
            catch (Exception ex) when (ex is COMException or InvalidCastException)
            {
                volume = 0;
                return false;
            }
        }

        public bool TrySetVolume(float volume)
        {
            try
            {
                control.SimpleAudioVolume.Volume = volume;
                return true;
            }
            catch (Exception ex) when (ex is COMException or InvalidCastException or ArgumentOutOfRangeException) { return false; }
        }

        private static T Read<T>(Func<T> read, T otherwise)
        {
            try { return read(); }
            catch (Exception ex) when (ex is COMException or InvalidCastException) { return otherwise; }
        }
    }
}
