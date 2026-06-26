using Anthropic.SDK;
using Azure.AI.OpenAI;
using Azure.Identity;
using Chatbot;
using DotNetEnv;
using Microsoft.Extensions.Configuration;
using System.ClientModel;

// ─────────────────────────────────────────────
// 1. Configuration
// ─────────────────────────────────────────────
Env.TraversePath().Load();

var config = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional: false)
    .AddEnvironmentVariables()
    .Build();

var systemPrompt = config["Chatbot:SystemPrompt"] ?? "You are a helpful assistant.";

// ─────────────────────────────────────────────
// 2. Provider selection — user chooses at startup
// ─────────────────────────────────────────────
var provider = PromptProviderSelection();

// ─────────────────────────────────────────────
// 3. Build IChatCompletionClient based on user's choice
// ─────────────────────────────────────────────
IChatCompletionClient completionClient;

try
{
    completionClient = provider switch
    {
        "1" => BuildAzureClient(config),
        "2" => BuildAnthropicClient(config),
        "3" => BuildOllamaClient(config),
        _ => throw new InvalidOperationException("Invalid provider selection.")
    };
}
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"\n[Startup Error] {ex.Message}");
    Console.ResetColor();
    return;
}

// ─────────────────────────────────────────────
// 4. Wire up ChatSession
// ─────────────────────────────────────────────
var session = new ChatSession(completionClient, systemPrompt);
var cts = new CancellationTokenSource();
var providerName = provider switch { "1" => "Azure OpenAI", "2" => "Anthropic", "3" => "Ollama", _ => "Unknown" };

Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

Console.WriteLine("\n══════════════════════════════════════");
Console.WriteLine($"  Chatbot  |  .NET 8  |  {providerName}");
Console.WriteLine("  Commands : 'exit' | 'reset'");
Console.WriteLine("══════════════════════════════════════\n");

// ─────────────────────────────────────────────
// 5. I/O loop — streaming
// ─────────────────────────────────────────────
while (!cts.IsCancellationRequested)
{
    Console.Write("You: ");
    var input = Console.ReadLine()?.Trim();

    if (string.IsNullOrEmpty(input)) continue;

    if (input.Equals("exit", StringComparison.OrdinalIgnoreCase)) break;

    if (input.Equals("reset", StringComparison.OrdinalIgnoreCase))
    {
        session.Reset();
        Console.WriteLine("[Session reset — history cleared]\n");
        continue;
    }

    try
    {
        Console.Write("\nAssistant: ");

        await foreach (var token in session.StreamAsync(input, cts.Token))
            Console.Write(token);

        Console.WriteLine("\n");
    }
    catch (OperationCanceledException)
    {
        Console.WriteLine("\n[Cancelled]");
        break;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"\n[Error] {ex.Message}\n");
    }
}

Console.WriteLine("Goodbye.");

// ─────────────────────────────────────────────
// Provider selection prompt
// ─────────────────────────────────────────────

static string PromptProviderSelection()
{
    while (true)
    {
        Console.Clear();
        Console.WriteLine("╔══════════════════════════════════════╗");
        Console.WriteLine("║         Chatbot  |  .NET 8           ║");
        Console.WriteLine("╠══════════════════════════════════════╣");
        Console.WriteLine("║  Select a provider:                  ║");
        Console.WriteLine("║                                      ║");
        Console.WriteLine("║  [1]  Azure OpenAI  (GPT-4o)         ║");
        Console.WriteLine("║  [2]  Anthropic     (Claude)         ║");
        Console.WriteLine("║  [3]  Ollama        (Local / Free)   ║");
        Console.WriteLine("╚══════════════════════════════════════╝");
        Console.Write("\nEnter choice (1, 2 or 3): ");

        var input = Console.ReadLine()?.Trim();

        if (input is "1" or "2" or "3")
            return input;

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("Invalid choice. Please enter 1, 2 or 3.");
        Console.ResetColor();
        Thread.Sleep(1200);
    }
}

// ─────────────────────────────────────────────
// Provider factory methods
// ─────────────────────────────────────────────

static IChatCompletionClient BuildAzureClient(IConfiguration config)
{
    var endpoint = config["AzureOpenAI:Endpoint"]
                         ?? throw new InvalidOperationException("AzureOpenAI:Endpoint not configured.");
    var deploymentName = config["AzureOpenAI:DeploymentName"] ?? "gpt-4o";
    var maxTokens = int.Parse(config["AzureOpenAI:MaxTokens"] ?? "1024");
    var useApiKey = bool.Parse(config["AzureOpenAI:UseApiKey"] ?? "true");

    AzureOpenAIClient azureClient;

    if (useApiKey)
    {
        var apiKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY")
                     ?? throw new InvalidOperationException(
                         "AZURE_OPENAI_API_KEY not set. Add it to your .env file.");
        azureClient = new AzureOpenAIClient(new Uri(endpoint), new ApiKeyCredential(apiKey));
    }
    else
    {
        azureClient = new AzureOpenAIClient(new Uri(endpoint), new DefaultAzureCredential());
    }

    return new AzureOpenAIChatClient(azureClient, deploymentName, maxTokens);
}

static IChatCompletionClient BuildAnthropicClient(IConfiguration config)
{
    var apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
                 ?? throw new InvalidOperationException(
                     "ANTHROPIC_API_KEY not set. Add it to your .env file.");

    var model = config["Anthropic:Model"] ?? "claude-sonnet-4-6";
    var maxTokens = int.Parse(config["Anthropic:MaxTokens"] ?? "1024");

    return new AnthropicChatClient(new AnthropicClient(apiKey), model, maxTokens);
}

static IChatCompletionClient BuildOllamaClient(IConfiguration config)
{
    var baseUrl = config["Ollama:BaseUrl"] ?? "http://localhost:11434";
    var model = config["Ollama:Model"] ?? "llama3";

    return new OllamaChatClient(baseUrl, model);
}