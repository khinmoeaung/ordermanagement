using Microsoft.OpenApi;

namespace OrderManagement.Api.Infrastructure;

public static class SwaggerSetup
{
    private const string BearerScheme = "Bearer";

    /// <summary>Adds Swagger generation with a JWT bearer input applied to every operation.</summary>
    public static IServiceCollection AddSwaggerWithJwt(this IServiceCollection services)
    {
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo { Title = "OrderManagement API", Version = "v1" });
            options.AddSecurityDefinition(BearerScheme, new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "Paste the access token returned by /api/v1/auth/login."
            });
            options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(BearerScheme, document)] = []
            });
        });
        return services;
    }

    /// <summary>Serves the Swagger JSON and UI in the Development and Docker environments only.</summary>
    public static WebApplication UseSwaggerInDevelopment(this WebApplication app, IWebHostEnvironment env)
    {
        if (env.IsDevelopment() || env.IsEnvironment("Docker"))
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }
        return app;
    }
}
