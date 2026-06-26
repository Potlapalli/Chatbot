using Chatbot;

namespace Chatbot.Tests;

/// <summary>
/// Deterministic in-memory fake for IChatCompletionClient.
///
/// Why a hand-rolled fake instead of Moq?
///   - StreamAsync returns IAsyncEnumerable — Moq can't set that up cleanly
///   - Exposes ReceivedHistories so we can assert exactly what history
///     snapshot was sent to the client on each call
///
/// Use Moq (in ChatSessionTests) for simple blocking-path tests where
/// you just need .ReturnsAsync("some response").
/// </summary>
public sealed class FakeChatCompletionClient : IChatCompletionClient
{
    private readonly Queue<string> _responses = new();

    // Every history snapshot sent — inspect in assertions
    public List<IReadOnlyList<ChatMessage>> ReceivedHistories { get; } = new();

    public FakeChatCompletionClient QueueResponse(string response)
    {
        _responses.Enqueue(response);
        return this; // fluent chaining
    }

    public Task<string> CompleteAsync(
        IReadOnlyList<ChatMessage> history,
        CancellationToken ct = default)
    {
        ReceivedHistories.Add(history.ToList());

        if (!_responses.TryDequeue(out var response))
            throw new InvalidOperationException("FakeChatCompletionClient: no more queued responses.");

        return Task.FromResult(response);
    }

    public async IAsyncEnumerable<string> StreamAsync(
        IReadOnlyList<ChatMessage> history,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        ReceivedHistories.Add(history.ToList());

        if (!_responses.TryDequeue(out var response))
            throw new InvalidOperationException("FakeChatCompletionClient: no more queued responses.");

        // Simulate streaming: yield each word as a separate token
        foreach (var token in response.Split(' '))
        {
            ct.ThrowIfCancellationRequested();
            yield return token + " ";
            await Task.Yield();
        }
    }
}
