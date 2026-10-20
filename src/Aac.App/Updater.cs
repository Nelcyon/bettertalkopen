using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using Aac.Core.Updates;

namespace Aac.App;

/// <summary>
/// Settings → Updates. Nothing here runs by itself: only Check for updates asks the internet anything.
/// Updating first keeps a snapshot of this version (the program folder, and the settings, buttons, panel layout
/// and learned words), downloads the newest installer and runs it quietly; the installer closes BetterTalk,
/// keeps the user's data as always, and starts the new version. Go back starts the snapshot's own copy of the
/// old program, which waits for this one to close, puts itself and the old setup back, and starts again.
/// Only the newest snapshot is kept.
/// </summary>
public static class Updater
{
    /// <summary>The argument the snapshot's copy of the program is started with to put itself back.</summary>
    public const string RestoreArgument = "--restore-snapshot";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(20) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("BetterTalk-updater");
        return http;
    }

    public static Version Current => typeof(Updater).Assembly.GetName().Version ?? new Version(0, 0, 0);

    public static string CurrentText => $"{Current.Major}.{Current.Minor}.{Math.Max(0, Current.Build)}";

    /// <summary>%LOCALAPPDATA%\BetterTalk\Snapshot (inside the test folder for a test copy): the version before the last update.</summary>
    public static string SnapshotFolder => Path.Combine(
        Environment.GetEnvironmentVariable("BETTERTALK_DATA_DIR") is { Length: > 0 } test
            ? test
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BetterTalk"),
        "Snapshot");

    private static string SnapshotInfo => Path.Combine(SnapshotFolder, "snapshot.json");
    private static string SnapshotApp => Path.Combine(SnapshotFolder, "app");
    private static string SnapshotData => Path.Combine(SnapshotFolder, "data");

    public sealed record SnapshotRecord(string Version, DateTime TakenAt, string ProgramFolder, string DataFolder);

    /// <summary>The snapshot that Go back would restore, or null when there is none.</summary>
    public static SnapshotRecord? Snapshot()
    {
        try
        {
            return File.Exists(SnapshotInfo) && File.Exists(Path.Combine(SnapshotApp, "BetterTalk.exe"))
                ? JsonSerializer.Deserialize<SnapshotRecord>(File.ReadAllText(SnapshotInfo)) : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static async Task<ReleaseInfo?> CheckAsync(CancellationToken cancel) =>
        ReleaseInfo.Parse(await Http.GetStringAsync(ReleaseInfo.LatestUrl, cancel));

    public static Task<string> ReadmeAsync(ReleaseInfo release, CancellationToken cancel) => Http.GetStringAsync(release.ReadmeUrl, cancel);

    /// <summary>Keeps this version and its setup, so the update can be undone.</summary>
    public static void TakeSnapshot()
    {
        if (Directory.Exists(SnapshotFolder)) Directory.Delete(SnapshotFolder, recursive: true);
        var program = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        CopyFolder(program, SnapshotApp, recursive: true, _ => true);
        // The setup: settings, buttons, layout, window size, learned words. Logs and the unsent draft stay out.
        CopyFolder(AppPaths.Root, SnapshotData, recursive: false,
            f => Path.GetExtension(f) is ".json" or ".bin" && !string.Equals(Path.GetFileName(f), "draft.bin", StringComparison.OrdinalIgnoreCase));
        File.WriteAllText(SnapshotInfo, JsonSerializer.Serialize(new SnapshotRecord(CurrentText, DateTime.Now, program, AppPaths.Root)));
    }

    /// <summary>Downloads the new installer to the temp folder, reporting 0–1.</summary>
    public static async Task<string> DownloadAsync(ReleaseInfo release, IProgress<double> progress, CancellationToken cancel)
    {
        var target = Path.Combine(Path.GetTempPath(), $"BetterTalk-Setup-{release.Tag}.exe");
        using var response = await Http.GetAsync(release.InstallerUrl, HttpCompletionOption.ResponseHeadersRead, cancel);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength ?? 0;
        await using (var source = await response.Content.ReadAsStreamAsync(cancel))
        await using (var file = File.Create(target + ".part"))
        {
            var buffer = new byte[81920];
            long done = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancel)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read), cancel);
                done += read;
                if (total > 0) progress.Report((double)done / total);
            }
        }
        File.Move(target + ".part", target, overwrite: true);
        return target;
    }

    /// <summary>Runs the installer without its pages; it closes BetterTalk, installs, and starts the new version (/relaunch=1).</summary>
    public static void StartInstaller(string installer) =>
        Process.Start(new ProcessStartInfo(installer, "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /relaunch=1") { UseShellExecute = true });

    /// <summary>Starts the snapshot's copy of the old program, which puts itself back once this one has closed.</summary>
    public static void StartRestore() =>
        Process.Start(new ProcessStartInfo(Path.Combine(SnapshotApp, "BetterTalk.exe"))
        {
            ArgumentList = { RestoreArgument, Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            UseShellExecute = false,
        });

    /// <summary>
    /// Run by the snapshot's copy (started with <see cref="RestoreArgument"/>): waits for the newer BetterTalk to
    /// close, copies the old program and setup back where they were, and starts the restored program.
    /// </summary>
    public static void RestoreFromSnapshot(string[] args)
    {
        if (args.Length > 1 && int.TryParse(args[1], out var pid))
        {
            try { Process.GetProcessById(pid).WaitForExit(60_000); }
            catch (ArgumentException) { } // already closed
        }
        if (Snapshot() is not { } snap) return;
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                CopyFolder(SnapshotApp, snap.ProgramFolder, recursive: true, _ => true);
                CopyFolder(SnapshotData, snap.DataFolder, recursive: false, _ => true);
                break;
            }
            catch (IOException) when (attempt < 20)
            {
                Thread.Sleep(500); // a file still in use for a moment after the newer version closed
            }
        }
        Process.Start(new ProcessStartInfo(Path.Combine(snap.ProgramFolder, "BetterTalk.exe")) { UseShellExecute = true });
    }

    private static void CopyFolder(string from, string to, bool recursive, Func<string, bool> include)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.GetFiles(from))
            if (include(file)) File.Copy(file, Path.Combine(to, Path.GetFileName(file)), overwrite: true);
        if (!recursive) return;
        foreach (var dir in Directory.GetDirectories(from))
            CopyFolder(dir, Path.Combine(to, Path.GetFileName(dir)), recursive: true, include);
    }
}
