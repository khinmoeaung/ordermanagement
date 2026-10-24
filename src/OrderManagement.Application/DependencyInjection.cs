using FluentValidation;
using OrderManagement.Application.Assistant;
using OrderManagement.Application.Assistant.Tools;
using OrderManagement.Application.Auth;
using OrderManagement.Application.Customers;
using OrderManagement.Application.Orders;
using OrderManagement.Application.Policies;
using OrderManagement.Application.Products;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace OrderManagement.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IProductService, ProductService>();
        services.AddScoped<ICustomerService, CustomerService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IOrderService, OrderService>();

        services.AddScoped<IAssistantTool, GetOrderStatusTool>();
        services.AddScoped<IAssistantTool, ListMyOrdersTool>();
        services.AddScoped<ToolExecutor>();
        // Infrastructure re-registers this with MaxToolRounds from configuration.
        services.AddScoped<IAssistantService>(sp =>
            new AssistantService(sp.GetRequiredService<IAssistantModel>(), sp.GetRequiredService<ToolExecutor>()));

        services.AddPolicies();
        return services;
    }
}
