using System.Diagnostics;
using System.Text.Json.Serialization;

using OrderManagement.Api.Infrastructure;
using OrderManagement.Application;
using OrderManagement.Application.Common;
using OrderManagement.Infrastructure;
using OrderManagement.Infrastructure.Persistence;

using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Logging: one summary line per request. Bodies and headers (including Authorization) are never logged.
builder.Host.UseSerilog((ctx, lc) => lc.ReadFrom.Configuration(ctx.Configuration).WriteTo.Console());

builder.Services
    .AddControllers(options => options.Filters.Add<ValidationFilter>())
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddProblemDetails(options =>
    options.CustomizeProblemDetails = ctx =>
        ctx.ProblemDetails.Extensions["traceId"] = Activity.Current?.Id ?? ctx.HttpContext.TraceIdentifier);
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Services.AddWebUiCors(builder.Configuration);
builder.Services.AddJwtAuthentication();
builder.Services.AddAuthPolicies();

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddSwaggerWithJwt();
builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>();

var app = builder.Build();

app.UseSerilogRequestLogging();
// Before the exception handler: it clears headers, so error responses would otherwise lose their CORS headers.
app.UseCors(CorsSetup.PolicyName);
app.UseExceptionHandler();
// Empty 4xx/5xx responses (401/403 from authorization, 404 for unknown routes) become ProblemDetails.
app.UseStatusCodePages();
app.UseSwaggerInDevelopment(app.Environment);

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");

// In Testing the test harness migrates and seeds.
if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Docker"))
{
    DatabaseMigrator.Migrate(app.Configuration.GetConnectionString("Default")!);
    await using AsyncServiceScope scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().SeedAsync(CancellationToken.None);
}

app.Run();

public partial class Program;
