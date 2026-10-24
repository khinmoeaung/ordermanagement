using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using OrderManagement.Application.Assistant;
using OrderManagement.Application.Auth;
using OrderManagement.Application.Common;
using OrderManagement.Application.Customers;
using OrderManagement.Application.Orders;
using OrderManagement.Application.Products;
using OrderManagement.Infrastructure.Assistant;
using OrderManagement.Infrastructure.Persistence;
using OrderManagement.Infrastructure.Persistence.Repositories;
using OrderManagement.Infrastructure.Policies;
using OrderManagement.Infrastructure.Security;

namespace OrderManagement.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<AppDbContext>(o => o.UseMySQL(configuration.GetConnectionString("Default")!));
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<AppDbContext>());

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<ICustomerRepository, CustomerRepository>();
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<IOrderRepository, OrderRepository>();

        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddSingleton<IPasswordHasher, AspNetPasswordHasher>();
        services.AddScoped<DatabaseSeeder>();

        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .Validate(JwtOptions.Validate, "Invalid Jwt settings.")
            .ValidateOnStart();
        services.AddOptions<SeedOptions>()
            .Bind(configuration.GetSection(SeedOptions.SectionName))
            .Validate(SeedOptions.Validate, "Invalid Seed settings.")
            .ValidateOnStart();

        services.AddOptions<AssistantOptions>()
            .Bind(configuration.GetSection(AssistantOptions.SectionName))
            .Validate(AssistantOptions.Validate, "Invalid Assistant settings.")
            .ValidateOnStart();
        services.AddSingleton<ChatClientFactory>();
        services.AddSingleton<IAssistantModel>(sp => new ChatClientAssistantModel(
            sp.GetRequiredService<ChatClientFactory>().Create,
            sp.GetRequiredService<IOptions<AssistantOptions>>().Value.MaxTokens));
        services.AddScoped<IAssistantService>(sp => new AssistantService(
            sp.GetRequiredService<IAssistantModel>(),
            sp.GetRequiredService<ToolExecutor>(),
            sp.GetRequiredService<IOptions<AssistantOptions>>().Value.MaxToolRounds));

        services.AddPolicyInfrastructure(configuration);

        return services;
    }
}
