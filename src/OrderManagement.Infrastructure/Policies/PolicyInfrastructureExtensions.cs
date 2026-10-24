using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OllamaSharp;
using OrderManagement.Application.Policies;
using OrderManagement.Infrastructure.Assistant;
using Qdrant.Client;

namespace OrderManagement.Infrastructure.Policies;

public static class PolicyInfrastructureExtensions
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    /// <summary>Binds <see cref="PolicyOptions"/> and registers the policy ports. Needs <c>AddInfrastructure</c> for the assistant options.</summary>
    public static IServiceCollection AddPolicyInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<PolicyOptions>()
            .Bind(configuration.GetSection(PolicyOptions.SectionName))
            .Validate(PolicyOptions.Validate, "Invalid Policies settings.")
            .ValidateOnStart();

        services.AddSingleton(sp => sp.GetRequiredService<IOptions<PolicyOptions>>().Value.ToSettings());
        services.AddSingleton<IPolicyLoader>(sp =>
            new MarkdownPolicyLoader(sp.GetRequiredService<IOptions<PolicyOptions>>().Value.Directory));
        services.AddSingleton<IPolicyEmbedder>(sp =>
        {
            var embedding = sp.GetRequiredService<IOptions<PolicyOptions>>().Value.Embedding;
            return new OllamaPolicyEmbedder(() => CreateGenerator(embedding), embedding.Model, embedding.QueryInstruction);
        });
        services.AddSingleton<IPolicyAnswerGenerator>(sp => new PolicyAnswerGenerator(
            sp.GetRequiredService<ChatClientFactory>(),
            sp.GetRequiredService<IOptions<AssistantOptions>>().Value.MaxTokens));

        services.AddSingleton(sp =>
        {
            var qdrant = sp.GetRequiredService<IOptions<PolicyOptions>>().Value.Qdrant;
            return new QdrantClient(
                qdrant.Host, qdrant.GrpcPort, https: false,
                apiKey: string.IsNullOrWhiteSpace(qdrant.ApiKey) ? null : qdrant.ApiKey,
                grpcTimeout: Timeout);
        });
        services.AddSingleton<IPolicyVectorStore>(sp => new QdrantPolicyVectorStore(
            sp.GetRequiredService<QdrantClient>(),
            sp.GetRequiredService<IOptions<PolicyOptions>>().Value.Qdrant.Collection));
        services.AddHostedService<PolicyIndexingHostedService>();

        return services;
    }

    private static IEmbeddingGenerator<string, Embedding<float>>? CreateGenerator(EmbeddingSettings embedding) =>
        string.IsNullOrWhiteSpace(embedding.Endpoint)
            ? null
            : new OllamaApiClient(new HttpClient { BaseAddress = new Uri(embedding.Endpoint), Timeout = Timeout }, embedding.Model);
}
