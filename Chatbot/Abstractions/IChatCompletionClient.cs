namespace Chatbot;

/// <summary>
/// The seam between session logic and the underlying model provider.
///
/// ChatSession depends ONLY on this interface — it has no knowledge of
/// Azure, Anthropic, Ollama, or any other provider. Swap implementations
/// freely without touching any session or application code.
/// </summary>
public interface IChatCompletionClient
{
    /// <summary>
    /// Returns the complete assistant reply after the model finishes.
    /// </summary>
    Task<string> CompleteAsync(
        IReadOnlyList<ChatMessage> history,
        CancellationToken ct = default);

    /// <summary>
    /// Streams tokens as they are generated.
    /// Callers iterate with: await foreach (var token in client.StreamAsync(...))
    /// </summary>
    IAsyncEnumerable<string> StreamAsync(
        IReadOnlyList<ChatMessage> history,
        CancellationToken ct = default);
}
