using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using Aac.Core.Feedback;

namespace Aac.App;

/// <summary>
/// Send feedback: the words go into feedback-outbox.json first, then everything waiting is sent to the address
/// named in the public repository's feedback.json. Only the person's feedback words, the BetterTalk version and
/// the Windows version are sent. The internet is asked only when feedback is sent, or at start-up when some is
/// still waiting from before.
/// </summary>
public static class FeedbackSender
{
    public enum Result { Sent, SavedNoInternet, SavedNotSetUp }

    private static readonly HttpClient Http = CreateClient();
    private static readonly SemaphoreSlim OneAtATime = new(1, 1);
    private static readonly FeedbackOutbox Outbox = new(Path.Combine(AppPaths.Root, "feedback-outbox.json"));

    /// <summary>Where people without the app (or with a question) can write instead: the project's issue forms.</summary>
    public const string GitHubPage = "https://github.com/Nelcyon/bettertalkopen/issues/new/choose";

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("BetterTalk-feedback");
        return http;
    }

    public static string WindowsVersion()
    {
        var v = Environment.OSVersion.Version;
        var name = v.Major == 10 && v.Build >= 22000 ? "Windows 11" : v.Major == 10 ? "Windows 10" : "Windows";
        return $"{name} ({v.Major}.{v.Minor}.{v.Build})";
    }

    public static bool AnyWaiting => Outbox.Load().Count > 0;

    public static async Task<Result> SendAsync(string text)
    {
        await OneAtATime.WaitAsync();
        try
        {
            Outbox.Add(FeedbackItem.Create(text, Updater.CurrentText, WindowsVersion(), DateTime.UtcNow));
            return await FlushLockedAsync();
        }
        finally { OneAtATime.Release(); }
    }

    /// <summary>Sends feedback left over from an earlier time, quietly (at start-up).</summary>
    public static async Task<int> SendWaitingAsync()
    {
        if (!AnyWaiting) return 0;
        await OneAtATime.WaitAsync();
        try
        {
            var before = Outbox.Load().Count;
            await FlushLockedAsync();
            return before - Outbox.Load().Count;
        }
        finally { OneAtATime.Release(); }
    }

    private static async Task<Result> FlushLockedAsync()
    {
        Uri? endpoint;
        try { endpoint = FeedbackConfig.Parse(await Http.GetStringAsync(FeedbackConfig.ConfigUrl)).Endpoint; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { return Result.SavedNoInternet; }
        if (endpoint is null) return Result.SavedNotSetUp;
        await Outbox.FlushAsync(async item =>
        {
            using var body = new StringContent(item.ToJson(), Encoding.UTF8, "application/json");
            using var response = await Http.PostAsync(endpoint, body);
            return response.IsSuccessStatusCode && FeedbackConfig.Accepted(await response.Content.ReadAsStringAsync());
        });
        return AnyWaiting ? Result.SavedNoInternet : Result.Sent;
    }

    public static void OpenGitHubPage()
    {
        try { Process.Start(new ProcessStartInfo(GitHubPage) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
    }
}
