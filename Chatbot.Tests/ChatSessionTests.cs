using Chatbot;
using FluentAssertions;
using Moq;

namespace Chatbot.Tests;

/// <summary>
/// Unit tests for ChatSession.
///
/// ALL tests use either FakeChatCompletionClient or Mock<IChatCompletionClient>.
/// No Azure SDK. No network calls. No environment variables needed.
/// This is exactly why the IChatCompletionClient abstraction exists.
/// </summary>
public sealed class ChatSessionTests
{
    private const string SystemPrompt = "You are a helpful assistant.";

    // ─────────────────────────────────────────
    // Construction
    // ─────────────────────────────────────────

    [Fact]
    public void Constructor_ShouldPrependSystemPromptToHistory()
    {
        var fake    = new FakeChatCompletionClient();
        var session = new ChatSession(fake, SystemPrompt);

        session.History.Should().HaveCount(1);
        session.History[0].Role.Should().Be(ChatRole.System);
        session.History[0].Content.Should().Be(SystemPrompt);
    }

    [Fact]
    public void Constructor_WhenClientIsNull_ShouldThrow()
    {
        var act = () => new ChatSession(null!, SystemPrompt);
        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Constructor_WhenSystemPromptIsNullOrWhitespace_ShouldThrow(string? prompt)
    {
        var fake = new FakeChatCompletionClient();
        var act  = () => new ChatSession(fake, prompt!);
        act.Should().Throw<ArgumentException>();
    }

    // ─────────────────────────────────────────
    // SendAsync (blocking path)
    // ─────────────────────────────────────────

    [Fact]
    public async Task SendAsync_ShouldReturnAssistantReply()
    {
        var fake    = new FakeChatCompletionClient().QueueResponse("Hello there!");
        var session = new ChatSession(fake, SystemPrompt);

        var reply = await session.SendAsync("Hi");

        reply.Should().Be("Hello there!");
    }

    [Fact]
    public async Task SendAsync_ShouldAppendUserMessageToHistory()
    {
        var fake    = new FakeChatCompletionClient().QueueResponse("Response");
        var session = new ChatSession(fake, SystemPrompt);

        await session.SendAsync("Tell me about CQRS");

        session.History.Should().HaveCount(3); // system + user + assistant
        session.History[1].Role.Should().Be(ChatRole.User);
        session.History[1].Content.Should().Be("Tell me about CQRS");
    }

    [Fact]
    public async Task SendAsync_ShouldAppendAssistantReplyToHistory()
    {
        var fake    = new FakeChatCompletionClient().QueueResponse("CQRS separates reads and writes.");
        var session = new ChatSession(fake, SystemPrompt);

        await session.SendAsync("What is CQRS?");

        session.History[2].Role.Should().Be(ChatRole.Assistant);
        session.History[2].Content.Should().Be("CQRS separates reads and writes.");
    }

    [Fact]
    public async Task SendAsync_ShouldSendFullHistoryToClient_OnEachTurn()
    {
        // Verifies the model receives context from prior turns —
        // the fundamental requirement for a stateful chatbot.
        var fake    = new FakeChatCompletionClient()
                          .QueueResponse("First response")
                          .QueueResponse("Second response");
        var session = new ChatSession(fake, SystemPrompt);

        await session.SendAsync("First message");
        await session.SendAsync("Second message");

        // First call:  system + user1 = 2 messages
        fake.ReceivedHistories[0].Should().HaveCount(2);

        // Second call: system + user1 + assistant1 + user2 = 4 messages
        fake.ReceivedHistories[1].Should().HaveCount(4);
    }

    [Fact]
    public async Task SendAsync_ShouldAlwaysSendSystemPromptAsFirstMessage()
    {
        var fake    = new FakeChatCompletionClient().QueueResponse("Answer");
        var session = new ChatSession(fake, SystemPrompt);

        await session.SendAsync("Any question");

        fake.ReceivedHistories[0][0].Role.Should().Be(ChatRole.System);
        fake.ReceivedHistories[0][0].Content.Should().Be(SystemPrompt);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SendAsync_WhenMessageIsEmpty_ShouldThrow(string message)
    {
        var fake    = new FakeChatCompletionClient();
        var session = new ChatSession(fake, SystemPrompt);

        var act = async () => await session.SendAsync(message);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    // ─────────────────────────────────────────
    // StreamAsync (streaming path)
    // ─────────────────────────────────────────

    [Fact]
    public async Task StreamAsync_ShouldYieldTokensAsTheyArrive()
    {
        var fake    = new FakeChatCompletionClient().QueueResponse("Hello world");
        var session = new ChatSession(fake, SystemPrompt);

        var tokens = new List<string>();
        await foreach (var token in session.StreamAsync("Hi"))
            tokens.Add(token);

        tokens.Should().NotBeEmpty();
        string.Join("", tokens).Trim().Should().Be("Hello world");
    }

    [Fact]
    public async Task StreamAsync_ShouldAppendAssembledReplyToHistoryAfterStreamCompletes()
    {
        var fake    = new FakeChatCompletionClient().QueueResponse("Streaming response");
        var session = new ChatSession(fake, SystemPrompt);

        await foreach (var _ in session.StreamAsync("Question")) { }

        session.History.Should().HaveCount(3);
        session.History[2].Role.Should().Be(ChatRole.Assistant);
        session.History[2].Content.Should().Contain("Streaming");
    }

    [Fact]
    public async Task StreamAsync_ShouldSendFullHistoryIncludingPriorTurns()
    {
        var fake    = new FakeChatCompletionClient()
                          .QueueResponse("First")
                          .QueueResponse("Second");
        var session = new ChatSession(fake, SystemPrompt);

        await foreach (var _ in session.StreamAsync("Message 1")) { }
        await foreach (var _ in session.StreamAsync("Message 2")) { }

        // Second call should see 4 messages (sys + user1 + asst1 + user2)
        fake.ReceivedHistories[1].Should().HaveCount(4);
    }

    [Fact]
    public async Task StreamAsync_WhenCancelled_ShouldThrowOperationCancelledException()
    {
        var fake    = new FakeChatCompletionClient().QueueResponse("Long response");
        var session = new ChatSession(fake, SystemPrompt);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () =>
        {
            await foreach (var _ in session.StreamAsync("Question", cts.Token)) { }
        };

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ─────────────────────────────────────────
    // Reset
    // ─────────────────────────────────────────

    [Fact]
    public async Task Reset_ShouldClearHistoryButPreserveSystemPrompt()
    {
        var fake    = new FakeChatCompletionClient().QueueResponse("Answer");
        var session = new ChatSession(fake, SystemPrompt);

        await session.SendAsync("Some question");
        session.History.Should().HaveCount(3);

        session.Reset();

        session.History.Should().HaveCount(1);
        session.History[0].Role.Should().Be(ChatRole.System);
        session.History[0].Content.Should().Be(SystemPrompt);
    }

    [Fact]
    public async Task Reset_ShouldAllowFreshConversationAfterReset()
    {
        var fake    = new FakeChatCompletionClient()
                          .QueueResponse("First answer")
                          .QueueResponse("Fresh answer");
        var session = new ChatSession(fake, SystemPrompt);

        await session.SendAsync("First question");
        session.Reset();
        await session.SendAsync("New question");

        // After reset, second call should see only: system + new user = 2 messages
        fake.ReceivedHistories[1].Should().HaveCount(2);
    }

    // ─────────────────────────────────────────
    // Moq variant — shows both test double styles
    // ─────────────────────────────────────────

    [Fact]
    public async Task SendAsync_WithMoq_ShouldCallClientWithCorrectHistory()
    {
        var mock = new Mock<IChatCompletionClient>();
        mock.Setup(c => c.CompleteAsync(
                It.IsAny<IReadOnlyList<ChatMessage>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("Mocked response");

        var session = new ChatSession(mock.Object, SystemPrompt);
        var reply   = await session.SendAsync("Hello");

        reply.Should().Be("Mocked response");

        mock.Verify(c => c.CompleteAsync(
            It.Is<IReadOnlyList<ChatMessage>>(h =>
                h.Count == 2 &&
                h[0].Role == ChatRole.System &&
                h[1].Role == ChatRole.User &&
                h[1].Content == "Hello"),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
