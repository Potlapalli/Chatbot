using Azure.AI.OpenAI;
using Azure.Identity;
using OpenAI.Chat;
using System.ClientModel;
using System.Runtime.CompilerServices;
using DomainMessage = Chatbot.ChatMessage;

namespace Chatbot;

/// <summary>
/// Azure OpenAI implementation of IChatCompletionClient.
/// This is the ONLY class that references the Azure SDK.
/// ChatSession depends only on IChatCompletionClient — zero Azure knowledge.
/// </summary>
public sealed class AzureOpenAIChatClient : IChatCompletionClient
{
    private readonly ChatClient _chatClient;
    private readonly int _maxTokens;

    public AzureOpenAIChatClient(
        AzureOpenAIClient azureClient,
        string deploymentName, int maxTokens = 1024)
    {
        ArgumentNullException.ThrowIfNull(azureClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(deploymentName);

        _chatClient = azureClient.GetChatClient(deploymentName);
        _maxTokens = maxTokens;
    }

    /// <inheritdoc/>
    public async Task<string> CompleteAsync(
        IReadOnlyList<DomainMessage> history,
        CancellationToken ct = default)
    {
        var options = new ChatCompletionOptions { MaxOutputTokenCount = _maxTokens };
        var sdkMessages = ChatMessageMapper.ToSdkMessages(history).ToList();
        var response = await _chatClient.CompleteChatAsync(sdkMessages,options, cancellationToken: ct);
        return response.Value.Content[0].Text;
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<string> StreamAsync(
        IReadOnlyList<DomainMessage> history,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var options = new ChatCompletionOptions { MaxOutputTokenCount = _maxTokens };
        var sdkMessages = ChatMessageMapper.ToSdkMessages(history).ToList();

        await foreach (var update in _chatClient.CompleteChatStreamingAsync(sdkMessages, options, cancellationToken: ct))
        {
            foreach (var part in update.ContentUpdate)
            {
                if (!string.IsNullOrEmpty(part.Text))
                    yield return part.Text;
            }
        }
    }
}