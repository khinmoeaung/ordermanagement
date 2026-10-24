using OrderManagement.Application.Policies;
using Shouldly;

namespace OrderManagement.UnitTests.Application.Policies;

public class PolicyChunkerTests
{
    private const int Max = 1500;

    private static IReadOnlyList<PolicyChunk> Chunk(string markdown, int max = Max, string source = "doc.md") =>
        PolicyChunker.Chunk(source, markdown, max);

    [Fact]
    public void Splits_on_headings_and_builds_heading_paths()
    {
        var chunks = Chunk("# T\n## A\nbody a\n## B\nbody b\n");

        chunks.Select(c => c.HeadingPath).ShouldBe(["T > A", "T > B"]);   // T has no body, so it is skipped
    }

    [Fact]
    public void Chunk_text_starts_with_heading_path_then_body()
    {
        var chunk = Chunk("# T\n## A\nbody a\n").Single();

        chunk.Text.ShouldBe("T > A\n\nbody a");
        chunk.Body.ShouldBe("body a");
        chunk.Title.ShouldBe("T");
        chunk.Source.ShouldBe("doc.md");
        chunk.Part.ShouldBe(0);
    }

    [Fact]
    public void Level_four_headings_are_part_of_the_body()
    {
        var chunk = Chunk("# T\n## A\nintro\n#### Detail\nmore\n").Single();

        chunk.HeadingPath.ShouldBe("T > A");
        chunk.Body.ShouldContain("#### Detail");
        chunk.Body.ShouldContain("more");
    }

    [Fact]
    public void Empty_sections_are_skipped() =>
        Chunk("# T\n## Empty\n\n## Full\ntext\n").Select(c => c.HeadingPath).ShouldBe(["T > Full"]);

    [Fact]
    public void Oversize_section_is_split_into_numbered_parts()
    {
        string para(char c) => new string(c, 40);
        var chunks = Chunk($"# T\n## A\n{para('a')}\n\n{para('b')}\n\n{para('c')}\n", max: 50);

        chunks.Select(c => c.Part).ShouldBe([0, 1, 2]);
        chunks.ShouldAllBe(c => c.HeadingPath == "T > A");
        chunks.Select(c => c.Body).ShouldBe([para('a'), para('b'), para('c')]);
        chunks.Select(c => c.Id).Distinct().Count().ShouldBe(3);
    }

    [Fact]
    public void Short_paragraphs_are_packed_into_one_part_up_to_the_limit()
    {
        var chunks = Chunk("# T\n## A\none\n\ntwo\n\nthree\n", max: 14);   // "one\n\ntwo" is 8 characters; adding "three" would exceed 14

        chunks.Select(c => c.Body).ShouldBe(["one\n\ntwo", "three"]);
    }

    [Fact]
    public void A_single_paragraph_longer_than_the_limit_becomes_its_own_part()
    {
        var chunk = Chunk("# T\n## A\n" + new string('x', 200) + "\n", max: 50).Single();

        chunk.Body.Length.ShouldBe(200);
    }

    [Fact]
    public void Ids_are_stable_and_depend_on_source_path_and_part()
    {
        var a1 = PolicyChunker.ChunkId("a.md", "T > A", 0);

        PolicyChunker.ChunkId("a.md", "T > A", 0).ShouldBe(a1);
        PolicyChunker.ChunkId("b.md", "T > A", 0).ShouldNotBe(a1);
        PolicyChunker.ChunkId("a.md", "T > B", 0).ShouldNotBe(a1);
        PolicyChunker.ChunkId("a.md", "T > A", 1).ShouldNotBe(a1);
        Chunk("# T\n## A\nbody\n").Single().Id.ShouldBe(PolicyChunker.ChunkId("doc.md", "T > A", 0));
    }

    [Fact]
    public void Hash_changes_when_text_changes()
    {
        PolicyChunker.Hash("same").ShouldBe(PolicyChunker.Hash("same"));
        PolicyChunker.Hash("same").ShouldNotBe(PolicyChunker.Hash("other"));
        PolicyChunker.Hash("same").Length.ShouldBe(64);
        Chunk("# T\n## A\nbody\n").Single().ContentHash.ShouldBe(PolicyChunker.Hash("T > A\n\nbody"));
    }

    [Fact]
    public void Handles_crlf_and_bom()
    {
        var chunk = Chunk("﻿# T\r\n## A\r\nbody\r\n").Single();

        chunk.HeadingPath.ShouldBe("T > A");
        chunk.Text.ShouldNotContain("\r");
        chunk.Text.ShouldNotContain("﻿");
    }

    [Fact]
    public void Heading_inside_code_fence_is_not_a_heading()
    {
        var chunk = Chunk("# T\n## A\n```\n# not a heading\n```\nafter\n").Single();

        chunk.HeadingPath.ShouldBe("T > A");
        chunk.Body.ShouldContain("# not a heading");
        chunk.Body.ShouldContain("after");
    }

    [Fact]
    public void Title_falls_back_to_file_name_without_extension()
    {
        var chunk = Chunk("## A\nbody\n", source: "refund-rules.md").Single();

        chunk.Title.ShouldBe("refund-rules");
        chunk.HeadingPath.ShouldBe("A");
    }

    [Fact]
    public void Repeated_heading_paths_in_one_file_get_distinct_stable_ids()
    {
        const string md = "# T\n## Notes\nfirst\n## Notes\nsecond\n";

        var chunks = Chunk(md);

        chunks.Select(c => c.Body).ShouldBe(["first", "second"]);
        chunks.Select(c => c.HeadingPath).ShouldBe(["T > Notes", "T > Notes"]);
        chunks.Select(c => c.Id).Distinct().Count().ShouldBe(2);
        Chunk(md).Select(c => c.Id).ShouldBe(chunks.Select(c => c.Id));   // stable across runs
        chunks[0].Id.ShouldBe(PolicyChunker.ChunkId("doc.md", "T > Notes", 0));   // the first keeps the plain ID
    }

    [Fact]
    public void Sibling_headings_do_not_leak_into_each_others_path() =>
        Chunk("# T\n## A\n### A1\nx\n## B\ny\n").Select(c => c.HeadingPath).ShouldBe(["T > A > A1", "T > B"]);

    [Fact]
    public void Sample_policy_files_chunk_with_expected_sections()
    {
        string dir = FindPoliciesDirectory();
        var paths = new Dictionary<string, string>
        {
            ["cancellation.md"] = "Cancellation Policy > When an order can be cancelled",
            ["refund.md"] = "Refund Policy > Refund amount",
            ["shipping.md"] = "Shipping Policy > Processing time",
        };

        foreach (var (file, expected) in paths)
        {
            var chunks = PolicyChunker.Chunk(file, File.ReadAllText(Path.Combine(dir, file)), Max);

            chunks.Select(c => c.HeadingPath).ShouldContain(expected);
            chunks.ShouldAllBe(c => c.Body.Length > 0 && c.Source == file);
        }
    }

    private static string FindPoliciesDirectory()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "policies");
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "refund.md"))) return candidate;
        }

        throw new DirectoryNotFoundException("policies/ not found above " + AppContext.BaseDirectory);
    }
}
