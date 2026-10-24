namespace OrderManagement.IntegrationTests;

/// <summary>
/// Base class for API tests. Before each test the database is emptied and re-seeded.
/// Derived classes must also be marked <c>[Collection("Api")]</c> and take an <see cref="ApiFactory"/>.
/// </summary>
[Collection("Api")]
public abstract class IntegrationTestBase(ApiFactory factory) : IAsyncLifetime
{
    protected ApiFactory Factory { get; } = factory;

    /// <summary>Helpers for registering, logging in and creating test data.</summary>
    protected TestClient Api { get; } = new(factory);

    /// <summary>An unauthenticated client.</summary>
    protected HttpClient Client { get; } = factory.CreateClient();
    // Every other client comes from Api, which disposes them all.

    public virtual async ValueTask InitializeAsync()
    {
        Factory.Assistant.Reset();
        Factory.Policies.Reset();
        await Factory.ResetDatabaseAsync(TestContext.Current.CancellationToken);
    }

    public virtual ValueTask DisposeAsync()
    {
        Client.Dispose();
        Api.Dispose();
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }
}
