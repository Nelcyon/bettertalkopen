using System.Text.Json;
using System.Text.Json.Serialization;

namespace Aac.Core.Feedback;

/// <summary>
/// One piece of feedback as it is sent: the words the person wrote for the makers, the BetterTalk version and the
/// Windows version. Nothing else, never message text or learned words.
/// </summary>
public sealed record FeedbackItem(
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("windows")] string Windows,
    [property: JsonPropertyName("written")] DateTime WrittenUtc)
{
    /// <summary>Longer feedback is cut here, so one press can never send a huge block.</summary>
    public const int MaxLength = 4000;

    public static FeedbackItem Create(string text, string version, string windows, DateTime nowUtc) =>
        new(text.Trim() is var t && t.Length > MaxLength ? t[..MaxLength] : t, version, windows, nowUtc);

    public string ToJson() => JsonSerializer.Serialize(this);
}

/// <summary>Where feedback goes: read from feedback.json in the public repository at the moment of sending.</summary>
public sealed record FeedbackConfig([property: JsonPropertyName("url")] string? Url)
{
    /// <summary>The file that names the address, so the address can change without a new BetterTalk version.</summary>
    public const string ConfigUrl = "https://raw.githubusercontent.com/Nelcyon/bettertalkopen/main/feedback.json";

    /// <summary>The address, when it is a usable https address; null while feedback sending is not set up yet.</summary>
    public Uri? Endpoint => Uri.TryCreate(Url?.Trim(), UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps ? u : null;

    public static FeedbackConfig Parse(string json)
    {
        try { return JsonSerializer.Deserialize<FeedbackConfig>(json) ?? new FeedbackConfig((string?)null); }
        catch (JsonException) { return new FeedbackConfig((string?)null); }
    }

    /// <summary>The receiver answers "ok" once the feedback is safely written down; anything else counts as not sent.</summary>
    public static bool Accepted(string responseBody) => string.Equals(responseBody.Trim(), "ok", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Feedback not sent yet (no internet, or sending not set up yet) waits in feedback-outbox.json and goes the next
/// time BetterTalk starts or the next time feedback is sent, so nothing written is lost.
/// </summary>
public sealed class FeedbackOutbox
{
    private readonly string _path;

    public FeedbackOutbox(string path) => _path = path;

    public IReadOnlyList<FeedbackItem> Load()
    {
        try
        {
            return File.Exists(_path) ? JsonSerializer.Deserialize<List<FeedbackItem>>(File.ReadAllText(_path)) ?? new() : new();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new List<FeedbackItem>();
        }
    }

    public void Save(IReadOnlyList<FeedbackItem> items)
    {
        if (items.Count == 0)
        {
            if (File.Exists(_path)) File.Delete(_path);
            return;
        }
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, _path, overwrite: true);
    }

    public void Add(FeedbackItem item) => Save(Load().Append(item).ToList());

    /// <summary>
    /// Tries to send everything waiting, oldest first, and keeps only what did not go. Stops at the first failure,
    /// since the rest would fail the same way. Returns how many were sent.
    /// </summary>
    public async Task<int> FlushAsync(Func<FeedbackItem, Task<bool>> send)
    {
        var waiting = Load().ToList();
        var sent = 0;
        while (sent < waiting.Count)
        {
            bool ok;
            try { ok = await send(waiting[sent]); }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException) { ok = false; }
            if (!ok) break;
            sent++;
        }
        if (sent > 0) Save(waiting.Skip(sent).ToList());
        return sent;
    }
}
