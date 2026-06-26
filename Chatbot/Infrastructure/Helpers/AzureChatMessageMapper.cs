using OpenAI.Chat;

using DomainMessage = Chatbot.ChatMessage;
using SdkMessage = OpenAI.Chat.ChatMessage;

namespace Chatbot.Infrastructure.Helpers;

/// <summary>
/// Maps between our domain ChatMessage type and Azure SDK ChatMessage types.
///
/// This is the ONLY place in the codebase that knows about both domain types
/// and Azure SDK types. If Azure changes their SDK, only this file changes.
/// </summary>
internal static class AzureChatMessageMapper
{
    internal static SdkMessage ToSdkMessage(DomainMessage message) =>
        message.Role switch
        {
            ChatRole.System => new SystemChatMessage(message.Content),
            ChatRole.User => new UserChatMessage(message.Content),
            ChatRole.Assistant => new AssistantChatMessage(message.Content),
            _ => throw new ArgumentOutOfRangeException(nameof(message.Role),
                     $"Unsupported role: {message.Role}")
        };

    internal static IEnumerable<SdkMessage> ToSdkMessages(
        IReadOnlyList<DomainMessage> history) =>
        history.Select(ToSdkMessage);
}