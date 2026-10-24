using Microsoft.Extensions.Logging.Abstractions;
using OrderManagement.Application.Policies;
using Shouldly;

namespace OrderManagement.UnitTests.Application.Policies;

public class PolicyQuestionServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static PolicyChunk Chunk(string source, string path, string body) =>
        new(PolicyChunker.ChunkId(source, path, 0), source, source == "refund.md" ? "Refund Policy" : "Shipping Policy", path, 0, path + "\n\n" + body, PolicyChunker.Hash(body));

    private sealed class FakeRetriever(IReadOnlyList<PolicyChunk> chunks, Exception? error = null) : IPolicyRetriever
    {
        public Task<IReadOnlyList<PolicyChunk>> RetrieveAsync(string question, CancellationToken ct) =>
            error is null ? Task.FromResult(chunks) : Task.FromException<IReadOnlyList<PolicyChunk>>(error);
    }

    private sealed class FakeGenerator(string answer) : IPolicyAnswerGenerator
    {
        public int Calls { get; private set; }
        public string? SystemPrompt { get; private set; }
        public string? UserMessage { get; private set; }

        public Task<string> GenerateAsync(string systemPrompt, string userMessage, CancellationToken ct)
        {
            Calls++;
            SystemPrompt = systemPrompt;
            UserMessage = userMessage;
            return Task.FromResult(answer);
        }
    }

    private static readonly PolicyChunk[] Two =
    [
        Chunk("refund.md", "Refund > Amount", "Full refund within 30 days."),
        Chunk("shipping.md", "Shipping > Time", "Three to five days."),
    ];

    private static PolicyQuestionService Service(FakeGenerator generator, IReadOnlyList<PolicyChunk>? chunks = null, Exception? error = null) =>
        new(new FakeRetriever(chunks ?? Two, error), generator, NullLogger<PolicyQuestionService>.Instance);

    [Fact]
    public async Task Returns_the_answer_with_only_the_cited_excerpts()
    {
        var generator = new FakeGenerator("Yes [2].");

        var answer = await Service(generator).AskAsync("How long is delivery?", Ct);

        answer.Answer.ShouldBe("Yes [2].");
        var citation = answer.Citations.Single();
        citation.Number.ShouldBe(2);   // the [n] in the answer text, so a client can match them
        citation.Source.ShouldBe("shipping.md");
        citation.Title.ShouldBe("Shipping Policy");
        citation.Section.ShouldBe("Shipping > Time");
        citation.Excerpt.ShouldBe("Three to five days.");
        generator.SystemPrompt.ShouldBe(PolicyPrompt.System);
        generator.UserMessage.ShouldBe(PolicyPrompt.BuildUserMessage("How long is delivery?", Two));
    }

    [Fact]
    public async Task Citations_are_distinct_and_in_ascending_order()
    {
        var answer = await Service(new FakeGenerator("[2] and [1] and [2]")).AskAsync("q", Ct);

        answer.Citations.Select(c => c.Source).ShouldBe(["refund.md", "shipping.md"]);
    }

    [Fact]
    public async Task Out_of_range_or_non_numeric_markers_produce_no_citation()
    {
        var answer = await Service(new FakeGenerator("See [0], [9] and [a].")).AskAsync("q", Ct);

        answer.Citations.ShouldBeEmpty();
    }

    [Fact]
    public async Task Phantom_markers_are_removed_from_the_answer_text_and_real_ones_kept()
    {
        var answer = await Service(new FakeGenerator("Refunds take 5 days [2] and more [9] or [0].")).AskAsync("q", Ct);

        answer.Answer.ShouldBe("Refunds take 5 days [2] and more or.");
        answer.Citations.Single().Number.ShouldBe(2);
    }

    [Fact]
    public async Task Answer_without_markers_has_no_citations()
    {
        var answer = await Service(new FakeGenerator("The policies do not cover that. Please contact support.")).AskAsync("q", Ct);

        answer.Citations.ShouldBeEmpty();
        answer.Answer.ShouldContain("contact support");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Blank_answer_returns_the_fallback_text(string generated)
    {
        var answer = await Service(new FakeGenerator(generated)).AskAsync("q", Ct);

        answer.Answer.ShouldBe("Sorry, I couldn't complete that request. Please try again.");
        answer.Citations.ShouldBeEmpty();
    }

    [Fact]
    public async Task Not_indexed_exception_propagates_and_the_generator_is_not_called()
    {
        var generator = new FakeGenerator("unused");

        await Should.ThrowAsync<PoliciesNotIndexedException>(
            () => Service(generator, error: new PoliciesNotIndexedException()).AskAsync("q", Ct));

        generator.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task Excerpt_is_truncated_to_300_characters()
    {
        var long300 = new[] { Chunk("refund.md", "Refund > Amount", new string('x', 500)) };

        var answer = await Service(new FakeGenerator("[1]"), long300).AskAsync("q", Ct);

        answer.Citations.Single().Excerpt.ShouldBe(new string('x', 300));
    }
}
