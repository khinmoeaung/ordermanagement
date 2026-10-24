using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using OrderManagement.Infrastructure.Assistant;
using Shouldly;

namespace OrderManagement.UnitTests.Infrastructure;

public class ChatClientFactoryTests
{
    private static ChatClientFactory Sut(AssistantOptions options, params (string Key, string? Value)[] settings)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.ToDictionary(s => s.Key, s => s.Value))
            .Build();
        return new ChatClientFactory(Options.Create(options), config);
    }

    [Theory]
    [InlineData(AssistantProvider.Claude)]
    [InlineData(AssistantProvider.OpenAI)]
    [InlineData(AssistantProvider.Gemini)]
    public void Missing_key_means_not_configured(AssistantProvider provider) =>
        Sut(new AssistantOptions { Provider = provider }, ("ANTHROPIC_API_KEY", ""), ("OPENAI_API_KEY", " "), ("GEMINI_API_KEY", null))
            .Create().ShouldBeNull();

    [Fact]
    public void Blank_ollama_endpoint_means_not_configured() =>
        Sut(new AssistantOptions { Provider = AssistantProvider.Ollama }).Create().ShouldBeNull();

    [Fact]
    public void Claude_with_key_creates_client_for_configured_model()
    {
        using IChatClient client = Sut(new AssistantOptions(), ("ANTHROPIC_API_KEY", "sk-test")).Create()!;
        client.GetService<ChatClientMetadata>()!.DefaultModelId.ShouldBe("claude-haiku-5-5");
    }

    [Fact]
    public void OpenAI_with_key_creates_client_for_configured_model()
    {
        var options = new AssistantOptions { Provider = AssistantProvider.OpenAI };
        options.OpenAI.Model = "model-x";
        using IChatClient client = Sut(options, ("OPENAI_API_KEY", "sk-test")).Create()!;
        client.GetService<ChatClientMetadata>()!.DefaultModelId.ShouldBe("model-x");
    }

    [Fact]
    public void Gemini_with_key_creates_a_client()
    {
        var options = new AssistantOptions { Provider = AssistantProvider.Gemini };
        using IChatClient? client = Sut(options, ("GEMINI_API_KEY", "test-key")).Create();
        client.ShouldNotBeNull();
    }

    [Fact]
    public void Ollama_with_endpoint_creates_client_for_configured_model()
    {
        var options = new AssistantOptions { Provider = AssistantProvider.Ollama };
        options.Ollama.Endpoint = "http://192.168.1.50:11434";
        using IChatClient client = Sut(options).Create()!;
        client.GetService<ChatClientMetadata>()!.DefaultModelId.ShouldBe("qwen2.5:7b");
    }
}
