using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Community.PowerToys.Run.Plugin.AIQuery;

public class AIClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly PluginSettings _settings;

    private const string SystemPrompt =
        "You are a concise assistant embedded in PowerToys Run on Windows. " +
        "Rules: Answer in the same language as the question. " +
        "Maximum 3 sentences. No greetings, no filler phrases. " +
        "Prefer facts, commands, code snippets, or numbers. " +
        "If code is needed, use one short inline block only. " +
        "Never use markdown headers or bullet lists longer than 3 items.";

    public AIClient(PluginSettings settings)
    {
        _settings = settings;
        _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds),
        };
    }

    public async Task<string> QueryAsync(string userMessage, string? context = null, CancellationToken ct = default)
    {
        var content = context is null
            ? userMessage
            : $"Context from previous answer:\n{context}\n\nFollow-up question: {userMessage}";

        return _settings.Provider == AIProvider.Claude
            ? await QueryClaudeAsync(content, ct)
            : await QueryOpenAIAsync(content, ct);
    }

    private async Task<string> QueryClaudeAsync(string message, CancellationToken ct)
    {
        var payload = new
        {
            model = _settings.Model,
            max_tokens = 512,
            system = SystemPrompt,
            messages = new[] { new { role = "user", content = message } },
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
        request.Headers.Add("x-api-key", _settings.ApiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var response = await _http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Claude API {(int)response.StatusCode}: {ExtractError(body)}");

        using var doc = JsonDocument.Parse(body);
        return doc.RootElement
            .GetProperty("content")[0]
            .GetProperty("text")
            .GetString() ?? string.Empty;
    }

    private async Task<string> QueryOpenAIAsync(string message, CancellationToken ct)
    {
        var payload = new
        {
            model = _settings.Model,
            max_tokens = 512,
            messages = new[]
            {
                new { role = "system", content = SystemPrompt },
                new { role = "user", content = message },
            },
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_settings.BaseUrl.TrimEnd('/')}/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.ApiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var response = await _http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"API {(int)response.StatusCode}: {ExtractError(body)}");

        using var doc = JsonDocument.Parse(body);
        return doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? string.Empty;
    }

    private static string ExtractError(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var err))
            {
                if (err.TryGetProperty("message", out var msg))
                    return msg.GetString() ?? body;
            }
        }
        catch { }
        return body.Length > 200 ? body[..200] : body;
    }

    public void Dispose() => _http.Dispose();
}
