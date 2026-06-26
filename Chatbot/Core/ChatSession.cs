namespace Chatbot;

/// <summary>
/// Stateful conversation session. Owns the message history and coordinates
/// with the injected IChatCompletionClient.
///
/// Key design decisions:
///   - Depends on IChatCompletionClient (interface), not any Azure SDK type
///   - History is a List<ChatMessage> — our domain type, not provider types
///   - System prompt is prepended at index 0 and never removed
///   - Both blocking and streaming paths update history identically
/// </summary>
public sealed class ChatSession
{
    private readonly IChatCompletionClient _client;
    private readonly List<ChatMessage> _history = new();

    public IReadOnlyList<ChatMessage> History => _history.AsReadOnly();

    public ChatSession(IChatCompletionClient client, string systemPrompt)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(systemPrompt);

        _client = client;

        // System prompt always lives at index 0.
        // Included in every API call as part of the full history.
        _history.Add(new ChatMessage(ChatRole.System, systemPrompt));
    }

    /// <summary>
    /// Blocking path: appends user turn, awaits full response, appends assistant turn.
    /// </summary>
    public async Task<string> SendAsync(string userMessage, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userMessage);

        _history.Add(new ChatMessage(ChatRole.User, userMessage));

        var snapshot = _history.ToList(); // capture current history for this turn
        var reply = await _client.CompleteAsync(snapshot, ct);

        _history.Add(new ChatMessage(ChatRole.Assistant, reply));

        return reply;
    }

    /// <summary>
    /// Streaming path: appends user turn, yields tokens as they arrive,
    /// then appends the fully-assembled reply to history when stream ends.
    /// History is updated atomically AFTER the stream completes, not per-token.
    /// </summary>
    public async IAsyncEnumerable<string> StreamAsync(
        string userMessage,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userMessage);

        _history.Add(new ChatMessage(ChatRole.User, userMessage));

        var snapshot = _history.ToList();
        var assembled = new System.Text.StringBuilder();

        await foreach (var token in _client.StreamAsync(snapshot, ct))
        {
            assembled.Append(token);
            yield return token;
        }

        // Append full assembled reply — next turn has complete context
        _history.Add(new ChatMessage(ChatRole.Assistant, assembled.ToString()));
    }

    /// <summary>
    /// Clears conversation history, keeping only the system prompt.
    /// </summary>
    public void Reset()
    {
        var systemPrompt = _history[0];
        _history.Clear();
        _history.Add(systemPrompt);
    }
}
