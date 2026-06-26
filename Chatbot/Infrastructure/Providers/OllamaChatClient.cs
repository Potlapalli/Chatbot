using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

// Alias to avoid any future name collisions
using DomainMessage = Chatbot.ChatMessage;

namespace Chatbot;

/// <summary>
/// Ollama implementation of IChatCompletionClient.
/// Ollama runs locally — no API key required.
///
/// Install: https://ollama.com
/// Pull a model: ollama pull llama3
/// Then set Provider=Ollama in appsettings.json.
///
/// Uses Ollama's /api/chat endpoint directly via HttpClient.
/// No extra NuGet package needed — the REST API is simple enough to call directly.
///
/// Supports any model Ollama hosts: llama3, mistral, phi3, gemma, etc.
/// Change the model name in appsettings.json with zero code changes.
/// </summary>
public sealed class OllamaChatClient : IChatCompletionClient
{
    private readonly HttpClient _httpClient;
    private readonly string _model;

    public OllamaChatClient(string baseUrl, string model)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);

        _model = model;
        _httpClient = new HttpClient { BaseAddress = new Uri(baseUrl) };
    }

    /// <inheritdoc/>
    public async Task<string> CompleteAsync(
        IReadOnlyList<DomainMessage> history,
        CancellationToken ct = default)
    {
        var request  = BuildRequest(history, stream: false);
        var response = await _httpClient.PostAsJsonAsync("/api/chat", request, ct);

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<OllamaResponse>(cancellationToken: ct);
        return result?.Message?.Content ?? string.Empty;
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<string> StreamAsync(
        IReadOnlyList<DomainMessage> history,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var request = BuildRequest(history, stream: true);

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/api/chat")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(request),
                Encoding.UTF8,
                "application/json")
        };

        using var response = await _httpClient.SendAsync(
            httpRequest,
            HttpCompletionOption.ResponseHeadersRead,
            ct);

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader       = new StreamReader(stream);

        // Ollama streams one JSON object per line
        while (!reader.EndOfStream && !ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (string.IsNullOrWhiteSpace(line)) continue;

            var chunk = JsonSerializer.Deserialize<OllamaResponse>(line);
            var token = chunk?.Message?.Content;

            if (!string.IsNullOrEmpty(token))
                yield return token;

            if (chunk?.Done == true) break;
        }
    }

    private OllamaRequest BuildRequest(IReadOnlyList<DomainMessage> history, bool stream) =>
        new OllamaRequest
        {
            Model    = _model,
            Stream   = stream,
            Messages = history.Select(m => new OllamaMessage
            {
                Role    = m.Role switch
                {
                    ChatRole.System    => "system",
                    ChatRole.User      => "user",
                    ChatRole.Assistant => "assistant",
                    _                  => "user"
                },
                Content = m.Content
            }).ToList()
        };

    // ── Internal DTOs for Ollama REST API ──────────────────────────────────

    private sealed class OllamaRequest
    {
        [JsonPropertyName("model")]    public string Model    { get; set; } = string.Empty;
        [JsonPropertyName("stream")]   public bool   Stream   { get; set; }
        [JsonPropertyName("messages")] public List<OllamaMessage> Messages { get; set; } = new();
    }

    private sealed class OllamaMessage
    {
        [JsonPropertyName("role")]    public string Role    { get; set; } = string.Empty;
        [JsonPropertyName("content")] public string Content { get; set; } = string.Empty;
    }

    private sealed class OllamaResponse
    {
        [JsonPropertyName("message")] public OllamaMessage? Message { get; set; }
        [JsonPropertyName("done")]    public bool Done { get; set; }
    }
}
