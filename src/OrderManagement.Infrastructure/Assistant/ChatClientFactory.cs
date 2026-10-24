using System.ClientModel;
using Anthropic;
using Google.GenAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using OllamaSharp;
using OpenAI;

namespace OrderManagement.Infrastructure.Assistant;

/// <summary>Builds the <see cref="IChatClient"/> for <c>Assistant:Provider</c>. Returns null when that provider isn't configured.</summary>
public sealed class ChatClientFactory(IOptions<AssistantOptions> options, IConfiguration configuration)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    private readonly AssistantOptions _options = options.Value;

    public IChatClient? Create() => _options.Provider switch
    {
        AssistantProvider.Claude => CreateClaude(),
        AssistantProvider.OpenAI => CreateOpenAI(),
        AssistantProvider.Gemini => CreateGemini(),
        AssistantProvider.Ollama => CreateOllama(),
        _ => throw new InvalidOperationException($"Unknown assistant provider {(int)_options.Provider}."),
    };

    private string? Key(string name) => configuration[name] is { } value && !string.IsNullOrWhiteSpace(value) ? value : null;

    private IChatClient? CreateClaude()
    {
        if (Key("ANTHROPIC_API_KEY") is not { } apiKey) return null;

        // ANTHROPIC_BASE_URL is an optional override, used by tests to point at a local stub server.
        AnthropicClient client = Key("ANTHROPIC_BASE_URL") is { } baseUrl
            ? new AnthropicClient { ApiKey = apiKey, Timeout = Timeout, BaseUrl = baseUrl }
            : new AnthropicClient { ApiKey = apiKey, Timeout = Timeout };
        return client.AsIChatClient(_options.Claude.Model, _options.MaxTokens);
    }

    private IChatClient? CreateOpenAI()
    {
        if (Key("OPENAI_API_KEY") is not { } apiKey) return null;

        var clientOptions = new OpenAIClientOptions { NetworkTimeout = Timeout };
        if (!string.IsNullOrWhiteSpace(_options.OpenAI.Endpoint))
            clientOptions.Endpoint = new Uri(_options.OpenAI.Endpoint);
        return new OpenAIClient(new ApiKeyCredential(apiKey), clientOptions)
            .GetChatClient(_options.OpenAI.Model)
            .AsIChatClient();
    }

    private IChatClient? CreateGemini()
    {
        if (Key("GEMINI_API_KEY") is not { } apiKey) return null;

        // HttpOptions.Timeout is in milliseconds.
        var http = new Google.GenAI.Types.HttpOptions { Timeout = (int)Timeout.TotalMilliseconds };
        return new Client(apiKey: apiKey, httpOptions: http).AsIChatClient(_options.Gemini.Model);
    }

    private IChatClient? CreateOllama()
    {
        if (string.IsNullOrWhiteSpace(_options.Ollama.Endpoint)) return null;

        var http = new HttpClient { BaseAddress = new Uri(_options.Ollama.Endpoint), Timeout = Timeout };
        return new OllamaApiClient(http, _options.Ollama.Model);
    }
}
