using Microsoft.Extensions.Logging.Abstractions;
using OrderManagement.Application.Policies;
using OrderManagement.UnitTests.Fakes;
using Shouldly;

namespace OrderManagement.UnitTests.Application.Policies;

public class PolicyRetrieverTests
{
    private readonly FakePolicyEmbedder _embedder = new();
    private readonly InMemoryPolicyVectorStore _store = new();
    private readonly PolicyKeywordIndex _keywords = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<PolicyRetriever> RetrieverAsync(int finalCount = 4, bool index = true)
    {
        var settings = new PolicySettings(1500, 8, finalCount, 60);
        if (index)
        {
            var loader = new FakePolicyLoader();
            loader.Files.Add(new PolicySourceFile("refund.md", "# Refund\n## Amount\nFull refund for a damaged item.\n"));
            loader.Files.Add(new PolicySourceFile("shipping.md", "# Shipping\n## Time\nThree to five business days.\n"));
            loader.Files.Add(new PolicySourceFile("cancellation.md", "# Cancellation\n## Window\nAn order can be cancelled before dispatch.\n"));
            await new PolicyIndexer(loader, _embedder, _store, _keywords, settings, NullLogger<PolicyIndexer>.Instance).ReindexAsync(Ct);
        }

        return new PolicyRetriever(_embedder, _store, _keywords, settings, NullLogger<PolicyRetriever>.Instance);
    }

    [Fact]
    public async Task Fuses_semantic_and_keyword_hits_and_returns_FinalCount_chunks()
    {
        var retriever = await RetrieverAsync(finalCount: 2);

        var chunks = await retriever.RetrieveAsync("refund damaged item", Ct);

        chunks.Count.ShouldBe(2);
        chunks.Select(c => c.Id).Distinct().Count().ShouldBe(2);
    }

    [Fact]
    public async Task A_chunk_found_by_both_searches_ranks_above_one_found_by_one()
    {
        var retriever = await RetrieverAsync();

        var chunks = await retriever.RetrieveAsync("refund", Ct);   // keyword matches only the refund chunk; semantic returns all three

        chunks.First().HeadingPath.ShouldBe("Refund > Amount");
    }

    [Fact]
    public async Task Burmese_question_still_returns_the_semantic_hits()
    {
        var retriever = await RetrieverAsync();

        var chunks = await retriever.RetrieveAsync("အော်ဒါကို ပယ်ဖျက်လို့ရလား", Ct);

        chunks.Count.ShouldBe(3);
    }

    [Fact]
    public async Task Empty_semantic_result_throws_not_indexed()
    {
        var retriever = await RetrieverAsync(index: false);

        await Should.ThrowAsync<PoliciesNotIndexedException>(() => retriever.RetrieveAsync("refund", Ct));
    }

    [Fact]
    public async Task Embedder_receives_the_question_as_a_query()
    {
        var retriever = await RetrieverAsync();

        await retriever.RetrieveAsync("Can I get a refund?", Ct);

        _embedder.Queries.ShouldBe(["Can I get a refund?"]);
    }
}
