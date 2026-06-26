using Anthropic.SDK;
using Anthropic.SDK.Messaging;
using System.Runtime.CompilerServices;

// Alias to avoid collision between Chatbot.ChatMessage and Anthropic.SDK.Messaging.Message
using DomainMessage = Chatbot.ChatMessage;

namespace Chatbot;

/// <summary>
/// Anthropic (Claude) implementation of IChatCompletionClient.
/// Uses Anthropic.SDK — the community .NET SDK for the Anthropic API.
///
/// Key differences from Azure SDK:
///   - System prompt is a SEPARATE parameter, not part of the message list
///   - Messages are List<Message> with RoleType enum (User/Assistant only)
///   - Streaming uses StreamClaudeMessageAsync yielding MessageResponse chunks
///   - Auth is via ANTHROPIC_API_KEY env var (read automatically by AnthropicClient)
/// </summary>
public sealed class AnthropicChatClient : IChatCompletionClient
{
    private readonly AnthropicClient _client;
    private readonly string _model;
    private readonly int _maxTokens;

    public AnthropicChatClient(AnthropicClient client, string model, int maxTokens = 1024)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);

        _client    = client;
        _model     = model;
        _maxTokens = maxTokens;
    }

    /// <inheritdoc/>
    public async Task<string> CompleteAsync(
        IReadOnlyList<DomainMessage> history,
        CancellationToken ct = default)
    {
        var parameters = BuildParameters(history, stream: false);
        var response   = await _client.Messages.GetClaudeMessageAsync(parameters, ct);
        return response.Content.OfType<TextContent>().FirstOrDefault()?.Text ?? string.Empty;
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<string> StreamAsync(
        IReadOnlyList<DomainMessage> history,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var parameters = BuildParameters(history, stream: true);

        await foreach (var chunk in _client.Messages.StreamClaudeMessageAsync(parameters, ct))
        {
            var token = chunk.Delta?.Text;
            if (!string.IsNullOrEmpty(token))
                yield return token;
        }
    }

    private MessageParameters BuildParameters(IReadOnlyList<DomainMessage> history, bool stream)
    {
        // Anthropic separates system prompt from the message list.
        // Extract the system message from history (always index 0) and
        // pass the remaining user/assistant turns as the message list.
        var systemPrompt = history
            .FirstOrDefault(m => m.Role == ChatRole.System)?.Content ?? string.Empty;

        var messages = history
            .Where(m => m.Role != ChatRole.System)
            .Select(m => new Message(
                m.Role == ChatRole.User ? RoleType.User : RoleType.Assistant,
                m.Content))
            .ToList();

        return new MessageParameters
        {
            Model     = _model,
            MaxTokens = _maxTokens,
            System    = new List<SystemMessage> { new SystemMessage(systemPrompt) },
            Messages  = messages,
            Stream    = stream
        };
    }
}
