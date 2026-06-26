# Chatbot — C# .NET 8 | Multi-Provider AI

A multi-provider AI chatbot with runtime provider switching across Azure OpenAI, Anthropic, and Ollama via a provider-agnostic abstraction layer with token streaming.

---

## Providers

| Provider | Model | Requires |
|---|---|---|
| Azure OpenAI | GPT-4o | Azure subscription + API key |
| Anthropic | Claude Sonnet | Anthropic API key |
| Ollama | Llama3, Mistral, Phi3 etc. | Local install, no key needed |

---

## Project Structure

```
Chatbot/
├── Abstractions/
│   ├── ChatMessage.cs               ← Domain type (Role + Content)
│   └── IChatCompletionClient.cs     ← Provider-agnostic interface
├── Core/
│   └── ChatSession.cs               ← Owns history, manages streaming
├── Infrastructure/
│   └── Providers/
│       ├── AzureOpenAIChatClient.cs ← Azure implementation
│       ├── AnthropicChatClient.cs   ← Anthropic implementation
│       ├── OllamaChatClient.cs      ← Ollama implementation
│       └── ChatMessageMapper.cs     ← Domain ↔ Azure SDK type mapping
├── Program.cs                       ← Startup, provider selection, I/O loop
├── appsettings.json                 ← Non-secret config (committed)
├── .env                             ← Local secrets (gitignored)
├── .env.example                     ← Secret template (committed)
└── .gitignore

Chatbot.Tests/
├── ChatSessionTests.cs              ← 14 unit tests, zero network dependency
└── FakeChatCompletionClient.cs      ← Streaming-capable in-memory test double
```

---

## Architecture

```
Program.cs  (provider selection)
     │
     ▼
IChatCompletionClient  (interface — the provider-agnostic seam)
     │
     ├── AzureOpenAIChatClient   (Azure.AI.OpenAI SDK)
     ├── AnthropicChatClient     (Anthropic.SDK)
     └── OllamaChatClient        (HttpClient — no extra package)
     │
     ▼
ChatSession  (owns history, coordinates streaming)
```

**Key principle:** `ChatSession` depends only on `IChatCompletionClient`.
It has zero knowledge of Azure, Anthropic, or Ollama.
Switching providers at runtime requires no changes to session or application logic.

---

## Setup

### 1. Clone and open in Visual Studio
Open `Chatbot.sln`

### 2. Configure appsettings.json
```json
{
  "AzureOpenAI": {
    "Endpoint": "https://YOUR-RESOURCE.openai.azure.com/",
    "DeploymentName": "gpt-4o",
    "MaxTokens": 1024,
    "UseApiKey": true
  },
  "Anthropic": {
    "Model": "claude-sonnet-4-6",
    "MaxTokens": 1024
  },
  "Ollama": {
    "BaseUrl": "http://localhost:11434",
    "Model": "llama3",
    "MaxTokens": 1024
  },
  "Chatbot": {
    "SystemPrompt": "You are a helpful assistant. Be concise and clear."
  }
}
```

### 3. Create your .env 

Fill in the keys for the providers you want to use:
```
AZURE_OPENAI_API_KEY=your-azure-key-here
ANTHROPIC_API_KEY=your-anthropic-key-here
# Ollama needs no key
```

### 4. Run
```bash
dotnet run 
```

---

## Getting API Keys

**Azure OpenAI**
1. Go to [portal.azure.com](https://portal.azure.com)
2. Create an **Azure OpenAI** resource
3. Go to **Keys and Endpoint** → copy Key and Endpoint
4. Go to **Azure OpenAI Studio** → **Deployments** → deploy `gpt-4o`

**Anthropic**
1. Go to [console.anthropic.com](https://console.anthropic.com)
2. Sign up → **API Keys** → **Create Key**
3. Add billing credit (minimum $5)

**Ollama (free, local)**
1. Install from [ollama.com](https://ollama.com)
2. Run `ollama pull llama3`
3. No API key needed

---

## Runtime Provider Selection

On startup, the app prompts you to choose a provider:

```
╔══════════════════════════════════════╗
║         Chatbot  |  .NET 8           ║
╠══════════════════════════════════════╣
║  Select a provider:                  ║
║                                      ║
║  [1]  Azure OpenAI  (GPT-4o)         ║
║  [2]  Anthropic     (Claude)         ║
║  [3]  Ollama        (Local / Free)   ║
╚══════════════════════════════════════╝

Enter choice (1, 2 or 3):
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


## Adding a New Provider

Implement `IChatCompletionClient` and add it to the selection menu:

```csharp
// 1. New provider client
public sealed class MyProviderChatClient : IChatCompletionClient
{
    public Task<string> CompleteAsync(IReadOnlyList<ChatMessage> history, CancellationToken ct = default)
    { ... }

    public IAsyncEnumerable<string> StreamAsync(IReadOnlyList<ChatMessage> history, CancellationToken ct = default)
    { ... }
}

// 2. Add to factory switch in Program.cs — zero other changes needed
"4" => new MyProviderChatClient(...)
```

---

