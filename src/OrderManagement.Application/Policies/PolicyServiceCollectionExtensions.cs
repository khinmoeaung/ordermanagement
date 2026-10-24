using Microsoft.Extensions.DependencyInjection;

namespace OrderManagement.Application.Policies;

public static class PolicyServiceCollectionExtensions
{
    /// <summary>
    /// Registers the policy services. The keyword index and indexer are singletons because they hold the in-memory index.
    /// Infrastructure supplies <see cref="PolicySettings"/> and the ports.
    /// </summary>
    public static IServiceCollection AddPolicies(this IServiceCollection services)
    {
        services.AddSingleton<PolicyKeywordIndex>();
        services.AddSingleton<IPolicyIndexer, PolicyIndexer>();
        services.AddSingleton<IPolicyRetriever, PolicyRetriever>();
        services.AddSingleton<IPolicyQuestionService, PolicyQuestionService>();
        return services;
    }
}
