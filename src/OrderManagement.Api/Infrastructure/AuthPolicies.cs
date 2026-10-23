using OrderManagement.Domain.Enums;

namespace OrderManagement.Api.Infrastructure;

public static class AuthPolicies
{
    public const string AdminOnly = nameof(AdminOnly);
    public const string CustomerOnly = nameof(CustomerOnly);

    public static IServiceCollection AddAuthPolicies(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .AddPolicy(AdminOnly, p => p.RequireAuthenticatedUser().RequireClaim("role", nameof(UserRole.Admin)))
            .AddPolicy(CustomerOnly, p => p.RequireAuthenticatedUser().RequireClaim("role", nameof(UserRole.Customer)));
        return services;
    }
}
