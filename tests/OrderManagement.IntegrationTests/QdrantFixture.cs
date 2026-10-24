using Qdrant.Client;
using Testcontainers.Qdrant;

namespace OrderManagement.IntegrationTests;

/// <summary>
/// Starts one throwaway Qdrant container for all Qdrant tests. Each test uses its own collection name
/// (<see cref="NewCollectionName"/>) and deletes it, so tests do not see each other's points.
/// </summary>
public sealed class QdrantFixture : IAsyncLifetime
{
    private readonly QdrantContainer _container = new QdrantBuilder("qdrant/qdrant:v1.20.0").Build();

    public QdrantClient Client { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        Client = new QdrantClient(_container.Hostname, _container.GetMappedPublicPort(6334));
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _container.DisposeAsync();
    }

    public static string NewCollectionName() => "test_" + Guid.NewGuid().ToString("N");
}

[CollectionDefinition("Qdrant")]
public sealed class QdrantCollection : ICollectionFixture<QdrantFixture>;
