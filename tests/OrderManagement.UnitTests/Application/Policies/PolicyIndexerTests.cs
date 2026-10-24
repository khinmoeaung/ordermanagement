using Microsoft.Extensions.Logging.Abstractions;
using OrderManagement.Application.Policies;
using OrderManagement.UnitTests.Fakes;
using Shouldly;

namespace OrderManagement.UnitTests.Application.Policies;

public class PolicyIndexerTests
{
    private const string Refund = "# Refund\n## Amount\nFull refund within 30 days.\n## Method\nOriginal payment method.\n";
    private const string Shipping = "# Shipping\n## Time\nThree to five days.\n";

    private readonly FakePolicyLoader _loader = new();
    private readonly FakePolicyEmbedder _embedder = new();
    private readonly InMemoryPolicyVectorStore _store = new();
    private readonly PolicyKeywordIndex _keywords = new();

    public PolicyIndexerTests()
    {
        _loader.Files.Add(new PolicySourceFile("refund.md", Refund));
        _loader.Files.Add(new PolicySourceFile("shipping.md", Shipping));
    }

    private PolicyIndexer Indexer() =>
        new(_loader, _embedder, _store, _keywords, new PolicySettings(1500, 8, 4, 60), NullLogger<PolicyIndexer>.Instance);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task First_run_adds_every_chunk_and_embeds_once_per_batch()
    {
        var result = await Indexer().ReindexAsync(Ct);

        result.ShouldBe(new ReindexResult(3, 0, 0, 0, 3));
        _store.Points.Count.ShouldBe(3);
        _store.VectorSize.ShouldBe(4);
        _embedder.DocumentCalls.ShouldBe(1);
        _keywords.ChunkCount.ShouldBe(3);

        // 40 sections need two batches of at most 32.
        _loader.Files.Clear();
        _loader.Files.Add(new PolicySourceFile("big.md", "# Big\n" + string.Concat(Enumerable.Range(0, 40).Select(i => $"## S{i}\ntext {i}\n"))));
        int before = _embedder.DocumentCalls;
        await Indexer().ReindexAsync(Ct);
        (_embedder.DocumentCalls - before).ShouldBe(2);
    }

    [Fact]
    public async Task Second_run_with_no_changes_reports_all_unchanged_and_never_calls_the_embedder()
    {
        await Indexer().ReindexAsync(Ct);
        int calls = _embedder.DocumentCalls;

        var result = await Indexer().ReindexAsync(Ct);

        result.ShouldBe(new ReindexResult(0, 0, 3, 0, 3));
        _embedder.DocumentCalls.ShouldBe(calls);
    }

    [Fact]
    public async Task Editing_one_section_updates_only_that_chunk()
    {
        await Indexer().ReindexAsync(Ct);
        int embedded = _embedder.TextsEmbedded;
        _loader.Files[0] = new PolicySourceFile("refund.md", Refund.Replace("30 days", "14 days"));

        var result = await Indexer().ReindexAsync(Ct);

        result.ShouldBe(new ReindexResult(0, 1, 2, 0, 3));
        (_embedder.TextsEmbedded - embedded).ShouldBe(1);
        _store.Points.Values.Single(p => p.Chunk.HeadingPath == "Refund > Amount").Chunk.Text.ShouldContain("14 days");
    }

    [Fact]
    public async Task Removing_a_heading_deletes_its_chunk()
    {
        await Indexer().ReindexAsync(Ct);
        _loader.Files[0] = new PolicySourceFile("refund.md", "# Refund\n## Amount\nFull refund within 30 days.\n");

        var result = await Indexer().ReindexAsync(Ct);

        result.ShouldBe(new ReindexResult(0, 0, 2, 1, 2));
        _store.Points.Values.Select(p => p.Chunk.HeadingPath).ShouldNotContain("Refund > Method");
    }

    [Fact]
    public async Task Deleting_a_file_deletes_its_chunks()
    {
        await Indexer().ReindexAsync(Ct);
        _loader.Files.RemoveAt(1);

        var result = await Indexer().ReindexAsync(Ct);

        result.ShouldBe(new ReindexResult(0, 0, 2, 1, 2));
        _store.Points.Values.ShouldAllBe(p => p.Chunk.Source == "refund.md");
        _keywords.ChunkCount.ShouldBe(2);
    }

    [Fact]
    public async Task Renamed_file_with_identical_content_deletes_old_and_adds_new_without_duplicates()
    {
        await Indexer().ReindexAsync(Ct);
        _loader.Files[1] = new PolicySourceFile("delivery.md", Shipping);

        var result = await Indexer().ReindexAsync(Ct);

        result.ShouldBe(new ReindexResult(1, 0, 2, 1, 3));
        _store.Points.Count.ShouldBe(3);
        _store.Points.Values.Select(p => p.Chunk.Source).ShouldNotContain("shipping.md");
    }

    [Fact]
    public async Task Changing_the_embedding_model_reembeds_everything()
    {
        await Indexer().ReindexAsync(Ct);
        _embedder.ModelName = "other-model";

        var result = await Indexer().ReindexAsync(Ct);

        result.ShouldBe(new ReindexResult(0, 3, 0, 0, 3));
    }

    [Fact]
    public async Task Keyword_index_is_rebuilt_even_when_the_embedder_throws()
    {
        _embedder.Throw = new HttpRequestException("ollama down");

        await Should.ThrowAsync<HttpRequestException>(() => Indexer().ReindexAsync(Ct));

        _keywords.ChunkCount.ShouldBe(3);
        _store.Points.ShouldBeEmpty();
    }

    [Fact]
    public async Task Store_never_holds_two_points_with_the_same_id()
    {
        await Indexer().ReindexAsync(Ct);
        _loader.Files[0] = new PolicySourceFile("refund.md", Refund.Replace("30 days", "14 days"));
        await Indexer().ReindexAsync(Ct);

        _store.UpsertBatches.ShouldAllBe(b => b.Select(c => c.Chunk.Id).Distinct().Count() == b.Count);
        _store.Points.Count.ShouldBe(3);
    }

    [Fact]
    public async Task Concurrent_reindexes_do_not_overlap()
    {
        _embedder.Delay = TimeSpan.FromMilliseconds(50);
        var indexer = Indexer();

        var results = await Task.WhenAll(indexer.ReindexAsync(Ct), indexer.ReindexAsync(Ct));

        _embedder.MaxConcurrent.ShouldBe(1);
        results.OrderBy(r => r.Added).ToList().ShouldBe([new ReindexResult(0, 0, 3, 0, 3), new ReindexResult(3, 0, 0, 0, 3)]);
        _store.Points.Count.ShouldBe(3);
    }

    [Fact]
    public async Task No_policy_files_yields_an_empty_result_and_clears_stale_chunks()
    {
        await Indexer().ReindexAsync(Ct);
        _loader.Files.Clear();

        var result = await Indexer().ReindexAsync(Ct);

        result.ShouldBe(new ReindexResult(0, 0, 0, 3, 0));
        _store.Points.ShouldBeEmpty();
        _keywords.ChunkCount.ShouldBe(0);
    }
}
