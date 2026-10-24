using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OrderManagement.Application.Policies;
using OrderManagement.Infrastructure.Assistant;
using OrderManagement.Infrastructure.Policies;
using Shouldly;

namespace OrderManagement.UnitTests.Infrastructure;

public class PolicyInfrastructureExtensionsTests
{
    private static ServiceProvider Build(Dictionary<string, string?> settings)
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddSingleton(configuration);
        services.AddOptions<AssistantOptions>();
        services.AddSingleton<ChatClientFactory>();
        services.AddPolicyInfrastructure(configuration);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Registers_the_ports_and_binds_settings_from_configuration()
    {
        using var provider = Build(new()
        {
            ["Policies:Chunking:MaxChars"] = "900",
            ["Policies:Retrieval:TopK"] = "6",
            ["Policies:Retrieval:FinalCount"] = "3",
            ["Policies:Embedding:Model"] = "other-model",
        });

        provider.GetRequiredService<PolicySettings>().ShouldBe(new PolicySettings(900, 6, 3, 60));
        provider.GetRequiredService<IPolicyLoader>().ShouldBeOfType<MarkdownPolicyLoader>();
        provider.GetRequiredService<IPolicyEmbedder>().ModelName.ShouldBe("other-model");
        provider.GetRequiredService<IPolicyAnswerGenerator>().ShouldBeOfType<PolicyAnswerGenerator>();
    }

    [Fact]
    public async Task Embedder_without_an_endpoint_reports_not_configured()
    {
        using var provider = Build([]);

        await Should.ThrowAsync<PolicySearchNotConfiguredException>(
            () => provider.GetRequiredService<IPolicyEmbedder>().EmbedQueryAsync("q", TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Invalid_settings_fail_options_validation()
    {
        using var provider = Build(new() { ["Policies:Retrieval:TopK"] = "2", ["Policies:Retrieval:FinalCount"] = "5" });

        var ex = Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IOptions<PolicyOptions>>().Value);

        ex.Message.ShouldContain("Invalid Policies settings.");
    }
}
