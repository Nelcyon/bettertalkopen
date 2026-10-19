using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Aac.Core.Generation;

/// <summary>
/// Calls a language model server running on this computer through the OpenAI-compatible
/// chat-completions endpoint that Ollama, llama.cpp's llama-server and LM Studio all expose.
/// Refuses any endpoint that is not loopback, so messages cannot leave the machine through this class.
/// </summary>
public sealed class LocalLlmGenerator : ISentenceGenerator
{
    private readonly HttpClient _http;
    private readonly Uri _endpoint;
    private readonly string _model;

    public LocalLlmGenerator(HttpClient http, string endpoint, string model)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)) throw new ArgumentException("Invalid endpoint URL.", nameof(endpoint));
        if (!uri.IsLoopback) throw new ArgumentException("The local AI endpoint must be on this computer (localhost).", nameof(endpoint));
        if (string.IsNullOrWhiteSpace(model)) throw new ArgumentException("A model name is required.", nameof(model));
        _http = http;
        _endpoint = uri;
        _model = model;
    }

    public string Name => $"Local AI ({_model})";
    public CandidateSource Source => CandidateSource.LocalAi;

    internal const string SystemPrompt =
        "You help a person with ALS speak faster. They type a few keywords; you propose complete sentences they might say, " +
        "in the first person, in plain conversational English. Rules: keep the meaning of every keyword; keep any " +
        "negation; keep names and numbers exactly; do not add people, places, times, events, feelings or promises " +
        "that the keywords do not imply; make the options genuinely different (for example a statement, a question, " +
        "a request); keep each under 25 words. Recent messages, if given, are only background and may be unrelated. " +
        "Reply with only a JSON array of strings.";

    public async Task<IReadOnlyList<string>> GenerateAsync(GenerationRequest request, CancellationToken cancellationToken)
    {
        var user = request.RecentMessages.Count == 0
            ? $"Keywords: {request.Keywords}\nGive {request.MaxCandidates} options."
            : $"Recent messages they said (background only):\n- {string.Join("\n- ", request.RecentMessages)}\n\nKeywords: {request.Keywords}\nGive {request.MaxCandidates} options.";

        var body = new JsonObject
        {
            ["model"] = _model,
            ["temperature"] = 0.4,
            ["stream"] = false,
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "system", ["content"] = SystemPrompt },
                new JsonObject { ["role"] = "user", ["content"] = user },
            },
        };

        using var response = await _http.PostAsJsonAsync(_endpoint, body, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonNode>(cancellationToken).ConfigureAwait(false);
        var content = json?["choices"]?[0]?["message"]?["content"]?.GetValue<string>() ?? "";
        return ParseCandidates(content).Take(request.MaxCandidates).ToList();
    }

    /// <summary>Accepts a JSON array (possibly inside a code fence) or falls back to one sentence per line.</summary>
    internal static IEnumerable<string> ParseCandidates(string content)
    {
        var start = content.IndexOf('[');
        var end = content.LastIndexOf(']');
        if (start >= 0 && end > start)
        {
            List<string>? parsed = null;
            try { parsed = JsonSerializer.Deserialize<List<string>>(content[start..(end + 1)]); }
            catch (JsonException) { }
            if (parsed is not null) return parsed.Select(s => s.Trim()).Where(s => s.Length > 0);
        }
        return content.Split('\n')
            .Select(l => Regex.Replace(l, @"^\s*(?:[-*•]|\d+[.)])\s*", "").Trim().Trim('"'))
            .Where(l => l.Length > 0);
    }
}
