using OrderManagement.Application.Policies;
using Shouldly;

namespace OrderManagement.UnitTests.Application.Policies;

public class Bm25Tests
{
    private static PolicyChunk Chunk(string path, string body, string source = "doc.md") =>
        new(PolicyChunker.ChunkId(source, path, 0), source, "T", path, 0, path + "\n\n" + body, PolicyChunker.Hash(body));

    private static PolicyChunk[] Sample() =>
    [
        Chunk("Refund > Damaged items", "If an item arrives damaged you get a full refund of the item price."),
        Chunk("Shipping > Delivery time", "Standard shipping takes three to five business days."),
        Chunk("Cancellation > Window", "An order can be cancelled before it ships."),
    ];

    [Fact]
    public void Tokenizer_lowercases_splits_and_drops_stop_words() =>
        Bm25Tokenizer.Tokenize("Can I cancel the Order?").ShouldBe(["can", "cancel", "order"]);

    [Theory]
    [InlineData("cancelling", "cancel")]
    [InlineData("cancelled", "cancel")]
    [InlineData("shipping", "ship")]
    [InlineData("refunds", "refund")]
    [InlineData("items", "item")]
    [InlineData("address", "address")]
    [InlineData("string", "string")]
    public void Tokenizer_stems_common_endings(string word, string stem) =>
        Bm25Tokenizer.Tokenize(word).ShouldBe([stem]);

    [Fact]
    public void Tokenizer_keeps_latin_words_inside_burmese_text()
    {
        var tokens = Bm25Tokenizer.Tokenize("ကျွန်တော့် order ကို cancel လို့ရလား");

        tokens.ShouldContain("order");
        tokens.ShouldContain("cancel");
    }

    [Fact]
    public void Search_ranks_the_chunk_with_the_query_terms_first()
    {
        var index = new Bm25Index(Sample());

        var hits = index.Search("refund damaged item", 3);

        hits.First().Chunk.HeadingPath.ShouldBe("Refund > Damaged items");
        hits.Select(h => h.Score).ShouldBeInOrder(SortDirection.Descending);
    }

    [Fact]
    public void Search_returns_nothing_for_unmatched_or_empty_query()
    {
        var index = new Bm25Index(Sample());

        index.Search("zebra", 3).ShouldBeEmpty();
        index.Search("", 3).ShouldBeEmpty();
        index.Search("the of", 3).ShouldBeEmpty();
    }

    [Fact]
    public void Search_respects_top_k() =>
        new Bm25Index(Sample()).Search("order item shipping", 1).Count.ShouldBe(1);

    [Fact]
    public void Burmese_query_returns_nothing_and_does_not_throw() =>
        new Bm25Index(Sample()).Search("အော်ဒါကို ပယ်ဖျက်လို့ရလား", 3).ShouldBeEmpty();

    [Fact]
    public void Empty_index_returns_nothing()
    {
        var index = new Bm25Index([]);

        index.ChunkCount.ShouldBe(0);
        index.Search("refund", 3).ShouldBeEmpty();
    }

    [Fact]
    public void KeywordIndex_before_rebuild_is_empty_and_after_rebuild_searches()
    {
        var index = new PolicyKeywordIndex();

        index.ChunkCount.ShouldBe(0);
        index.Search("refund", 3).ShouldBeEmpty();

        index.Rebuild(Sample());

        index.ChunkCount.ShouldBe(3);
        index.Search("refund", 3).Single().Chunk.HeadingPath.ShouldBe("Refund > Damaged items");
    }

    [Fact]
    public void KeywordIndex_rebuild_replaces_the_previous_chunks()
    {
        var index = new PolicyKeywordIndex();
        index.Rebuild(Sample());

        index.Rebuild([Chunk("Other > Topic", "gift wrapping available")]);

        index.ChunkCount.ShouldBe(1);
        index.Search("refund", 3).ShouldBeEmpty();
        index.Search("gift", 3).Count.ShouldBe(1);
    }
}
