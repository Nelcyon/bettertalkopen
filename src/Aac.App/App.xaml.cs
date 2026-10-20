using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using Aac.Core.Abstractions;
using Aac.Core.Generation;
using Aac.Core.Input;
using Aac.Core.Prediction;
using Aac.Core.Session;
using Aac.Core.Speech;
using Aac.Core.Storage;

namespace Aac.App;

public partial class App : Application
{
    private ISpeechEngine? _engine;
    private WordPredictor? _predictor;
    private PersonalModelStore? _learned;
    private PhraseMemory? _memory;
    private AppSettings? _settings;
    private TrayIcon? _tray;
    private HotCorner? _corner;
    private SettingsWindow? _settingsWindow;
    private bool _restart;

    private EventWaitHandle? _showSignal;
    private string _exitReason = "closed";

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // One copy only: launching again (desktop shortcut, Start menu) just brings the running one forward.
        // A test copy with its own data folder (BETTERTALK_DATA_DIR) runs alongside the real one instead of handing over to it.
        var signalName = @"Local\BetterTalk.Show" + (Environment.GetEnvironmentVariable("BETTERTALK_DATA_DIR") is { Length: > 0 } ? ".test" : "");
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, signalName, out var isFirst);
        if (!isFirst)
        {
            _showSignal.Set();
            Shutdown();
            return;
        }
        LogLifecycle($"started, version {typeof(App).Assembly.GetName().Version}, pid {Environment.ProcessId}");
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            LogError(args.ExceptionObject as Exception);
            LogLifecycle($"crashed: unhandled {(args.ExceptionObject as Exception)?.GetType().Name} (terminating: {args.IsTerminating})");
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            LogError(args.Exception);
            LogLifecycle($"background task error: {args.Exception.InnerException?.GetType().Name ?? args.Exception.GetType().Name}");
            args.SetObserved();
        };
        SessionEnding += (_, args) => _exitReason = $"Windows session ending ({args.ReasonSessionEnding})";
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher));

        // The 59k-word dictionary takes a moment to load; do it alongside the voice and settings setup.
        var predictorLoad = Task.Run(WordPredictor.CreateDefault);
        var notices = new List<string>();
        var settings = _settings = SettingsStore.Load(AppPaths.Settings, out var settingsProblem);
        if (settingsProblem is not null) notices.Add(settingsProblem);
        else if (!File.Exists(AppPaths.Settings)) SettingsStore.Save(AppPaths.Settings, settings);
        settings.Phrases = ButtonStore.Load(AppPaths.Buttons, settings.Phrases, out var buttonsProblem);
        if (buttonsProblem is not null) notices.Add(buttonsProblem);

        var clock = SystemClock.Instance;
        var protector = new DpapiProtector();

        _engine = CreateSpeechEngine(settings.Speech, notices);
        var speech = new SpeechController(_engine, clock, settings.Speech.Safety, new PronunciationLexicon(settings.Speech.Pronunciations));

        _predictor = predictorLoad.GetAwaiter().GetResult();
        _memory = new PhraseMemory();
        if (settings.Privacy.LearnFromSpokenMessages)
        {
            _learned = new PersonalModelStore(AppPaths.Learned, protector);
            var data = _learned.Load();
            _predictor.ImportPersonal(data);
            _memory.Import(data);
        }

        var drafts = new DraftStore(AppPaths.Draft, protector);
        if (!settings.Privacy.RecoverUnfinishedMessage) drafts.Clear();

        // Settings files written before a feature existed lack its default keys: add them (never overriding the user's own).
        foreach (var (key, action) in KeyMap.DefaultBindings)
            if (!settings.KeyBindings.ContainsKey(key) && !settings.KeyBindings.Values.Contains(action, StringComparer.OrdinalIgnoreCase))
                settings.KeyBindings[key] = action;

        KeyMap keyMap;
        try { keyMap = KeyMap.FromDictionary(settings.KeyBindings); }
        catch (ArgumentException ex)
        {
            keyMap = KeyMap.Default();
            notices.Add($"Key settings problem ({ex.Message}); using default keys.");
        }
        var gate = new InputGate(keyMap, settings.Input, clock);

        var session = new CommunicationSession(
            speech, _predictor, settings.Phrases,
            SessionOptions.From(settings), clock,
            settings.Privacy.RecoverUnfinishedMessage ? drafts : null,
            _memory);
        session.LearningChanged += (_, _) => SaveLearning();
        if (notices.Count > 0) session.ShowNotice(string.Join(" ", notices));

        var window = new MainWindow(session, gate, settings);
        MainWindow = window;
        _tray = new TrayIcon(window.ShowFromTray, window.HideToTray, OpenSettings, OpenSettingsFile, () => Quit(restart: false, "Exit chosen in the tray menu"),
            on => SetEditMode(session, window, on));
        _session = session;
        // For checking the Settings window on a test copy (it is otherwise opened from the tray icon).
        if (Environment.GetEnvironmentVariable("BETTERTALK_OPEN_SETTINGS") == "1" && Environment.GetEnvironmentVariable("BETTERTALK_DATA_DIR") is { Length: > 0 })
            Dispatcher.BeginInvoke(OpenSettings, DispatcherPriority.ApplicationIdle);
        session.SettingsRequested += (_, _) => OpenSettings();
        session.EditRequested += (_, edit) => EditPhrase(session, window, edit);
        session.PhrasesChanged += (_, _) => SavePhrases();
        if (settings.OpenFromTopLeftCorner) _corner = new HotCorner(settings.CornerTrigger, settings.CornerDwellMs, window.OnHotCorner);
        if (!settings.StartHidden) window.Show();
        // Get the Itch figure ready in the background, so opening Itch is quick even the first time.
        ItchView.Preload().ContinueWith(_ => Dispatcher.BeginInvoke(ItchViewHost.Prepare, DispatcherPriority.ApplicationIdle),
            TaskScheduler.Default);

        var signal = _showSignal;
        ThreadPool.RegisterWaitForSingleObject(signal, (_, _) => Dispatcher.BeginInvoke(window.ShowFromTray), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    private CommunicationSession? _session;

    private void SetEditMode(CommunicationSession session, MainWindow window, bool on)
    {
        session.SetEditMode(on);
        if (_tray is not null) _tray.EditModeChecked = on;
        if (on)
        {
            window.ShowFromTray();
            session.ShowNotice("Edit mode: tap a button to change its words, or ＋ Add. To stop, right-click the BetterTalk icon near the clock and untick Change the words on the buttons.");
        }
        else session.ShowNotice("Edit mode off.");
    }

    /// <summary>Opens the edit box for a button and applies the result.</summary>
    private static void EditPhrase(CommunicationSession session, MainWindow window, PhraseEdit edit)
    {
        var dialog = new PhraseEditWindow(edit) { Owner = window.IsVisible ? window : null };
        if (dialog.ShowDialog() == true && dialog.Result is { } text) session.ApplyEdit(edit, text);
    }

    /// <summary>Writes the button words to buttons.json the moment they change, so they survive any restart.</summary>
    private void SavePhrases()
    {
        if (_settings is null) return;
        try
        {
            ButtonStore.Save(AppPaths.Buttons, _settings.Phrases);
            LogLifecycle($"buttons saved ({_settings.Phrases.Pinned.Count} urgent, {_settings.Phrases.QuickPhrases.Count} quick)");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogError(ex);
            _session?.ShowNotice($"The button change could not be saved ({ex.Message}).");
        }
    }

    private void OpenSettings()
    {
        if (_settings is null) return;
        if (_settingsWindow is { IsVisible: true })
        {
            _settingsWindow.Activate();
            return;
        }
        _settingsWindow = new SettingsWindow(_settings, () => Quit(restart: true, "restart to apply new settings"),
            () => { if (_session is not null && MainWindow is MainWindow w) SetEditMode(_session, w, true); });
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    internal static void OpenSettingsFile()
    {
        try { Process.Start(new ProcessStartInfo("notepad.exe", $"\"{AppPaths.Settings}\"") { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { LogError(ex); }
    }

    /// <summary>Really closes the app (the window's close button only hides it to the tray).</summary>
    private void Quit(bool restart, string reason)
    {
        _restart = restart;
        _exitReason = reason;
        if (MainWindow is MainWindow w) w.AllowClose = true;
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_showSignal is not null && MainWindow is not null) LogLifecycle($"exit: {_exitReason} (code {e.ApplicationExitCode})");
        SaveLearning();
        _corner?.Dispose();
        _tray?.Dispose();
        (_engine as IDisposable)?.Dispose();
        base.OnExit(e);
        if (_restart && Environment.ProcessPath is { } exe)
        {
            try { Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = AppContext.BaseDirectory }); }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { LogError(ex); }
        }
    }

    /// <summary>Saved after every spoken message, so a crash never loses what was learned.</summary>
    private void SaveLearning()
    {
        if (_learned is null || _predictor is null || _memory is null) return;
        try
        {
            var data = new PersonalData();
            _predictor.ExportPersonal(data);
            _memory.Export(data);
            if (data.Words.Count == 0 && data.Phrases.Count == 0) _learned.Clear();
            else _learned.Save(data);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        {
            LogError(ex);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Keep the communication window alive: log, tell the user, carry on.
        LogError(e.Exception);
        LogLifecycle($"recovered from {e.Exception.GetType().Name} on the UI thread");
        e.Handled = true;
        if (MainWindow?.DataContext is CommunicationSession s)
            s.ShowNotice("Something went wrong, but you can keep going. Quick phrases still work.");
    }

    private const long AppLogLimit = 200 * 1024;

    /// <summary>Appends one line to app.log (start, exit reason, crashes). Never message text. Rotates at about 200 KB.</summary>
    internal static void LogLifecycle(string line)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.Root);
            var log = new FileInfo(AppPaths.AppLog);
            if (log.Exists && log.Length > AppLogLimit) File.Move(log.FullName, log.FullName + ".1", overwrite: true);
            File.AppendAllText(AppPaths.AppLog, $"{DateTimeOffset.Now:O} {line}{Environment.NewLine}");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    internal static void LogError(Exception? ex)
    {
        if (ex is null) return;
        try
        {
            Directory.CreateDirectory(AppPaths.Root);
            // Exception details only; message text is never written to the log.
            File.AppendAllText(AppPaths.ErrorLog, $"{DateTimeOffset.Now:O} {ex.GetType().FullName}: {ex.Message}{Environment.NewLine}{ex.StackTrace}{Environment.NewLine}");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>
    /// The chosen voice: a natural one if it is downloaded (Settings → Voice), otherwise the Windows voice.
    /// A natural voice that later fails to load falls back to the Windows voice by itself, with a notice.
    /// </summary>
    private static ISpeechEngine CreateSpeechEngine(SpeechSettings speech, List<string> notices)
    {
        if (VoiceCatalog.FindNatural(speech.VoiceName) is { Pack: { } pack } natural)
        {
            if (VoicePacks.IsInstalled(pack))
            {
                var engine = new NaturalSpeechEngine(natural, speech);
                engine.Problem += (_, message) => (Current?.MainWindow?.DataContext as CommunicationSession)?.ShowNotice(message);
                return engine;
            }
            notices.Add($"The {natural.Name} voice is not downloaded yet; using the Windows voice. Download it in Settings → Voice.");
            return new SapiSpeechEngine(new SpeechSettings { Rate = speech.Rate, Volume = speech.Volume });
        }
        var sapi = new SapiSpeechEngine(speech);
        if (sapi.VoiceProblem is not null) notices.Add(sapi.VoiceProblem);
        return sapi;
    }
}
