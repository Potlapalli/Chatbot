namespace Chatbot;

/// <summary>
/// Provider-agnostic representation of a single conversation turn.
/// No SDK types here — this is a pure domain type.
/// </summary>
public sealed record ChatMessage(ChatRole Role, string Content);

public enum ChatRole
{
    System,
    User,
    Assistant
}
