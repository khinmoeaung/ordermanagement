using System.Net;
using System.Net.Sockets;
using OrderManagement.Application.Policies;
using OrderManagement.Infrastructure.Policies;
using Qdrant.Client;
using Shouldly;

namespace OrderManagement.IntegrationTests.Qdrant;

[Collection("Qdrant")]
public sealed class QdrantPolicyVectorStoreTests(QdrantFixture qdrant) : IAsyncLifetime
{
    private readonly string _collection = QdrantFixture.NewCollectionName();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private QdrantPolicyVectorStore Store() => new(qdrant.Client, _collection);

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (await qdrant.Client.CollectionExistsAsync(_collection, CancellationToken.None))
            await qdrant.Client.DeleteCollectionAsync(_collection);
    }

    private static IndexedChunk Indexed(string source, string path, float[] vector, string body = "body", int part = 0)
    {
        string text = path + "\n\n" + body;
        var chunk = new PolicyChunk(PolicyChunker.ChunkId(source, path, part), source, "Title " + source, path, part, text, PolicyChunker.Hash(text));
        return new IndexedChunk(chunk, PolicyChunker.Hash("model|" + chunk.ContentHash), vector);
    }

    private async Task<ulong> CountAsync() => await qdrant.Client.CountAsync(_collection, cancellationToken: Ct);

    [Fact]
    public async Task EnsureReady_creates_the_collection_and_is_idempotent()
    {
        var store = Store();

        await store.EnsureReadyAsync(4, Ct);
        await store.EnsureReadyAsync(4, Ct);

        (await qdrant.Client.CollectionExistsAsync(_collection, Ct)).ShouldBeTrue();
        var info = await qdrant.Client.GetCollectionInfoAsync(_collection, Ct);
        info.Config.Params.VectorsConfig.Params.Size.ShouldBe(4UL);
        info.Config.Params.VectorsConfig.Params.Distance.ToString().ShouldBe("Cosine");
    }

    [Fact]
    public async Task EnsureReady_with_a_different_size_throws_a_clear_error()
    {
        var store = Store();
        await store.EnsureReadyAsync(4, Ct);

        var ex = await Should.ThrowAsync<InvalidOperationException>(() => store.EnsureReadyAsync(8, Ct));

        ex.Message.ShouldContain(_collection);
        ex.Message.ShouldContain("4");
        ex.Message.ShouldContain("8");
        ex.Message.ShouldContain("delete", Case.Insensitive);
    }

    [Fact]
    public async Task Upserting_the_same_chunks_twice_leaves_one_point_per_id()
    {
        var store = Store();
        await store.EnsureReadyAsync(4, Ct);
        IndexedChunk[] chunks = [Indexed("a.md", "A", [1, 0, 0, 0]), Indexed("a.md", "B", [0, 1, 0, 0]), Indexed("b.md", "C", [0, 0, 1, 0])];

        await store.UpsertAsync(chunks, Ct);
        await store.UpsertAsync(chunks, Ct);

        (await CountAsync()).ShouldBe(3UL);
    }

    [Fact]
    public async Task GetFingerprints_returns_ids_and_hashes_across_more_than_one_page()
    {
        var store = Store();
        await store.EnsureReadyAsync(4, Ct);
        var chunks = Enumerable.Range(0, 300).Select(i => Indexed("big.md", "S" + i, [1, i, 0, 0])).ToList();
        await store.UpsertAsync(chunks, Ct);

        var fingerprints = await store.GetFingerprintsAsync(Ct);

        fingerprints.Count.ShouldBe(300);
        foreach (var chunk in chunks) fingerprints[chunk.Chunk.Id].ShouldBe(chunk.Fingerprint);
    }

    [Fact]
    public async Task GetFingerprints_and_Search_on_a_missing_collection_return_empty()
    {
        var store = Store();

        (await store.GetFingerprintsAsync(Ct)).ShouldBeEmpty();
        (await store.SearchAsync([1, 0, 0, 0], 5, Ct)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Delete_removes_only_the_given_ids()
    {
        var store = Store();
        await store.EnsureReadyAsync(4, Ct);
        var a = Indexed("a.md", "A", [1, 0, 0, 0]);
        var b = Indexed("a.md", "B", [0, 1, 0, 0]);
        var c = Indexed("a.md", "C", [0, 0, 1, 0]);
        await store.UpsertAsync([a, b, c], Ct);

        await store.DeleteAsync([a.Chunk.Id, c.Chunk.Id], Ct);

        (await store.GetFingerprintsAsync(Ct)).Keys.ShouldBe([b.Chunk.Id]);
    }

    [Fact]
    public async Task Search_returns_the_nearest_chunk_first_with_its_payload()
    {
        var store = Store();
        await store.EnsureReadyAsync(4, Ct);
        var near = Indexed("refund.md", "Refund > Amount", [1, 0, 0, 0], "Full refund.", part: 2);
        await store.UpsertAsync([near, Indexed("x.md", "X", [0, 1, 0, 0]), Indexed("y.md", "Y", [0, 0, 1, 0])], Ct);

        var hits = await store.SearchAsync([0.9f, 0.1f, 0, 0], 2, Ct);

        hits.Count.ShouldBe(2);
        hits[0].Score.ShouldBeGreaterThan(hits[1].Score);
        var top = hits[0].Chunk;
        top.Id.ShouldBe(near.Chunk.Id);
        top.Source.ShouldBe("refund.md");
        top.Title.ShouldBe("Title refund.md");
        top.HeadingPath.ShouldBe("Refund > Amount");
        top.Part.ShouldBe(2);
        top.Text.ShouldBe(near.Chunk.Text);
        top.Body.ShouldBe("Full refund.");
        top.ContentHash.ShouldBe(near.Chunk.ContentHash);
    }

    [Fact]
    public async Task Unreachable_qdrant_becomes_unavailable()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int closedPort = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        using var client = new QdrantClient("127.0.0.1", closedPort, https: false, grpcTimeout: TimeSpan.FromSeconds(5));
        var store = new QdrantPolicyVectorStore(client, _collection);

        await Should.ThrowAsync<PolicySearchUnavailableException>(() => store.GetFingerprintsAsync(Ct));
        await Should.ThrowAsync<PolicySearchUnavailableException>(() => store.SearchAsync([1, 0, 0, 0], 3, Ct));
        await Should.ThrowAsync<PolicySearchUnavailableException>(() => store.EnsureReadyAsync(4, Ct));
    }
}
