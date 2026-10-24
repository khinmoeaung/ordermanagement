using Microsoft.Extensions.Logging.Abstractions;
using OrderManagement.Application.Policies;
using OrderManagement.Infrastructure.Policies;
using Shouldly;

namespace OrderManagement.IntegrationTests.Qdrant;

[Collection("Qdrant")]
public sealed class PolicyIndexerWithQdrantTests(QdrantFixture qdrant) : IAsyncLifetime
{
    private const string Refund = "# Refund\n## Amount\nFull refund within 30 days.\n## Method\nOriginal payment method.\n";
    private const string Shipping = "# Shipping\n## Time\nThree to five days.\n";

    private readonly string _collection = QdrantFixture.NewCollectionName();
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "policies-" + Guid.NewGuid().ToString("N"));
    private readonly CountingEmbedder _embedder = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "refund.md"), Refund);
        File.WriteAllText(Path.Combine(_dir, "shipping.md"), Shipping);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        Directory.Delete(_dir, recursive: true);
        if (await qdrant.Client.CollectionExistsAsync(_collection, CancellationToken.None))
            await qdrant.Client.DeleteCollectionAsync(_collection);
    }

    private PolicyIndexer Indexer() => new(
        new MarkdownPolicyLoader(_dir), _embedder, new QdrantPolicyVectorStore(qdrant.Client, _collection),
        new PolicyKeywordIndex(), new PolicySettings(1500, 8, 4, 60), NullLogger<PolicyIndexer>.Instance);

    private async Task<ulong> CountAsync() => await qdrant.Client.CountAsync(_collection, cancellationToken: Ct);

    [Fact]
    public async Task Second_run_changes_nothing_and_embeds_nothing()
    {
        var first = await Indexer().ReindexAsync(Ct);
        int embedded = _embedder.TextsEmbedded;

        var second = await Indexer().ReindexAsync(Ct);

        first.ShouldBe(new ReindexResult(3, 0, 0, 0, 3));
        second.ShouldBe(new ReindexResult(0, 0, 3, 0, 3));
        _embedder.TextsEmbedded.ShouldBe(embedded);
        (await CountAsync()).ShouldBe(3UL);
    }

    [Fact]
    public async Task Editing_one_file_updates_only_its_chunks()
    {
        await Indexer().ReindexAsync(Ct);
        int embedded = _embedder.TextsEmbedded;
        File.WriteAllText(Path.Combine(_dir, "refund.md"), Refund.Replace("30 days", "14 days"));

        var result = await Indexer().ReindexAsync(Ct);

        result.ShouldBe(new ReindexResult(0, 1, 2, 0, 3));
        (_embedder.TextsEmbedded - embedded).ShouldBe(1);
        (await CountAsync()).ShouldBe(3UL);
    }

    [Fact]
    public async Task Renaming_a_file_leaves_no_duplicates()
    {
        await Indexer().ReindexAsync(Ct);
        File.Move(Path.Combine(_dir, "shipping.md"), Path.Combine(_dir, "delivery.md"));

        var result = await Indexer().ReindexAsync(Ct);

        result.ShouldBe(new ReindexResult(1, 0, 2, 1, 3));
        (await CountAsync()).ShouldBe(3UL);
    }

    private sealed class CountingEmbedder : IPolicyEmbedder
    {
        public string ModelName => "fake-model";
        public int TextsEmbedded { get; private set; }

        public Task<IReadOnlyList<float[]>> EmbedDocumentsAsync(IReadOnlyList<string> texts, CancellationToken ct)
        {
            TextsEmbedded += texts.Count;
            return Task.FromResult<IReadOnlyList<float[]>>(texts.Select(VectorFor).ToList());
        }

        public Task<float[]> EmbedQueryAsync(string question, CancellationToken ct) => Task.FromResult(VectorFor(question));

        private static float[] VectorFor(string text)
        {
            int h = text.Sum(c => c);
            return [h % 97 + 1, h % 89 + 1, text.Length, 1];
        }
    }
}
