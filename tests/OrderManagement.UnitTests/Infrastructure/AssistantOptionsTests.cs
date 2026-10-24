using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OrderManagement.Infrastructure.Assistant;
using Shouldly;

namespace OrderManagement.UnitTests.Infrastructure;

public class AssistantOptionsTests
{
    [Fact]
    public void Defaults_are_valid()
    {
        var o = new AssistantOptions();
        AssistantOptions.Validate(o).ShouldBeTrue();
        o.Provider.ShouldBe(AssistantProvider.Claude);
        o.MaxTokens.ShouldBe(4096);
        o.MaxToolRounds.ShouldBe(5);
        o.Claude.Model.ShouldBe("claude-haiku-5-5");
        o.Ollama.Model.ShouldBe("qwen2.5:7b");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Non_positive_max_tokens_invalid(int v) =>
        AssistantOptions.Validate(new AssistantOptions { MaxTokens = v }).ShouldBeFalse();

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public void Tool_rounds_out_of_range_invalid(int v) =>
        AssistantOptions.Validate(new AssistantOptions { MaxToolRounds = v }).ShouldBeFalse();

    [Fact]
    public void Blank_model_of_selected_provider_is_invalid()
    {
        var o = new AssistantOptions { Provider = AssistantProvider.OpenAI };
        o.OpenAI.Model = "";
        AssistantOptions.Validate(o).ShouldBeFalse();
    }

    [Fact]
    public void Blank_model_of_unselected_provider_is_ignored()
    {
        var o = new AssistantOptions { Provider = AssistantProvider.Claude };
        o.OpenAI.Model = "";
        AssistantOptions.Validate(o).ShouldBeTrue();
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("/relative")]
    public void Bad_ollama_endpoint_invalid(string url)
    {
        var o = new AssistantOptions { Provider = AssistantProvider.Ollama };
        o.Ollama.Endpoint = url;
        AssistantOptions.Validate(o).ShouldBeFalse();
    }

    [Fact]
    public void Bad_openai_endpoint_invalid()
    {
        var o = new AssistantOptions();
        o.OpenAI.Endpoint = "not a url";
        AssistantOptions.Validate(o).ShouldBeFalse();
    }

    [Fact]
    public void Valid_http_endpoints_are_accepted()
    {
        var o = new AssistantOptions { Provider = AssistantProvider.Ollama };
        o.Ollama.Endpoint = "http://192.168.1.50:11434";
        AssistantOptions.Validate(o).ShouldBeTrue();
    }

    private static AssistantOptions Bind(string provider)
    {
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Assistant:Provider"] = provider })
            .Build();
        var services = new ServiceCollection();
        services.AddOptions<AssistantOptions>().Bind(config.GetSection(AssistantOptions.SectionName));
        return services.BuildServiceProvider().GetRequiredService<IOptions<AssistantOptions>>().Value;
    }

    [Fact]
    public void Provider_name_binds_case_insensitively() =>
        Bind("ollama").Provider.ShouldBe(AssistantProvider.Ollama);

    [Fact]
    public void Unknown_provider_fails_to_bind() =>
        Should.Throw<Exception>(() => Bind("Gemini2"));
}
