# Chatbot — C# .NET 8 / Azure OpenAI

Stateful, streaming chatbot with a provider-agnostic session layer,
fully unit-tested without any Azure SDK or network dependency.

---

## Project Structure

```
Chatbot/                               ← Visual Studio solution root
│
├── Chatbot/                           ← Console project (single project)
│   │
│   ├── Abstractions/
│   │   ├── ChatMessage.cs             ← Domain type (Role + Content), no Azure types
│   │   └── IChatCompletionClient.cs   ← The seam — swap providers without touching session logic
│   │
│   ├── Core/
│   │   └── ChatSession.cs             ← Owns history, coordinates streaming, zero Azure knowledge
│   │
│   ├── Infrastructure/
│   │   └── Providers/
│   │       ├── AzureOpenAIChatClient.cs  ← Only class that references Azure SDK
│   │       └── ChatMessageMapper.cs      ← Translates domain types ↔ Azure SDK types
│   │
│   ├── Program.cs                     ← Config, DI wiring, I/O loop
│   ├── appsettings.json               ← Non-secret config (committed to git)
│   ├── .env                           ← Local secrets (gitignored — never commit)
│   ├── .env.example                   ← Template for .env (committed to git)
│   ├── .gitignore
│   └── Chatbot.csproj
│
└── tests/
    └── Chatbot.Tests/
        ├── ChatSessionTests.cs        ← 14 tests, zero Azure dependency
        ├── FakeChatCompletionClient.cs ← Streaming-capable in-memory test double
        └── Chatbot.Tests.csproj
```

---

## Architecture

```
Program.cs
    │
    │  new AzureOpenAIChatClient(openAIClient, deploymentName)
    │       implements IChatCompletionClient
    │
    ▼
ChatSession                         ← depends only on IChatCompletionClient
    │                                  no Azure SDK types, no network
    │  _client.StreamAsync(history)
    ▼
IChatCompletionClient  (interface)
    │
    ▼
AzureOpenAIChatClient              ← only class that imports Azure.AI.OpenAI
    │
    ▼
Azure OpenAI Service
```

**Key principle:** `ChatSession` never imports `Azure.AI.OpenAI`.
It depends only on `IChatCompletionClient`. To swap providers, implement
that interface in a new class — `ChatSession` and `Program.cs` logic are
unchanged.

---


## Setup

### 1. Fill in appsettings.json
```json
{
  "AzureOpenAI": {
    "Endpoint": "https://YOUR-RESOURCE.openai.azure.com/",
    "DeploymentName": "gpt-4o",
    "MaxTokens": 1024,
    "UseApiKey": true
  },
  "Chatbot": {
    "SystemPrompt": "You are a helpful assistant. Be concise and clear."
  }
}
```

### 2. Create your .env from the example
```bash
cp .env.example .env
```
Then edit `.env` and add your key:
```
AZURE_OPENAI_API_KEY=your-key-here
```

### 3. Run
```bash
dotnet run
```

### 4. Run tests
```bash
dotnet test
```

---

## Commands (at runtime)

| Input | Action |
|---|---|
| Any text | Send message, stream response |
| `reset` | Clear history, keep system prompt |
| `exit` | Quit |
| `Ctrl+C` | Graceful cancellation |

---


