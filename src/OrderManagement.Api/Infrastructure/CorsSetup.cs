using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Options;

namespace OrderManagement.Api.Infrastructure;

/// <summary>The "Cors" configuration section.</summary>
public sealed class WebUiCorsOptions
{
    public const string SectionName = "Cors";

    public string[] AllowedOrigins { get; set; } = [];

    public static bool Validate(WebUiCorsOptions o) => o.AllowedOrigins.All(IsValidOrigin);

    private static bool IsValidOrigin(string origin) =>
        Uri.TryCreate(origin, UriKind.Absolute, out Uri? uri)
        && uri.Scheme is "http" or "https"
        && uri.AbsolutePath == "/"
        && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment)
        && !origin.EndsWith('/');
}

/// <summary>
/// CORS for browser clients (the Angular web UI). Allowed origins come from <c>Cors:AllowedOrigins</c>;
/// the base settings list none, and the Development and Docker settings allow <c>http://localhost:4200</c>.
/// </summary>
public static class CorsSetup
{
    public const string PolicyName = "WebUi";

    public static IServiceCollection AddWebUiCors(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<WebUiCorsOptions>()
            .Bind(configuration.GetSection(WebUiCorsOptions.SectionName))
            .Validate(WebUiCorsOptions.Validate,
                "Invalid Cors:AllowedOrigins: use origins like http://localhost:4200 (scheme, host, optional port; no path, no trailing slash, no wildcard).")
            .ValidateOnStart();

        services.AddCors();
        // Built from the validated options when CORS is first used, so configuration added by a test host or the environment applies.
        services.AddOptions<CorsOptions>().Configure<IOptions<WebUiCorsOptions>>((cors, settings) =>
            // Authentication is by bearer token in the Authorization header, so credentials (cookies) are not allowed.
            cors.AddPolicy(PolicyName, policy => policy
                .WithOrigins(settings.Value.AllowedOrigins)
                .WithHeaders("Authorization", "Content-Type")
                .WithMethods("GET", "POST", "PUT", "DELETE")
                .SetPreflightMaxAge(TimeSpan.FromMinutes(10))));
        return services;
    }
}
