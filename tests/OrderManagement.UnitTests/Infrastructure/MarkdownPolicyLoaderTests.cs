using OrderManagement.Infrastructure.Policies;
using Shouldly;

namespace OrderManagement.UnitTests.Infrastructure;

public sealed class MarkdownPolicyLoaderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "policies-" + Guid.NewGuid().ToString("N"));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public MarkdownPolicyLoaderTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public async Task Reads_only_markdown_files_sorted_by_name()
    {
        File.WriteAllText(Path.Combine(_dir, "shipping.md"), "# S");
        File.WriteAllText(Path.Combine(_dir, "cancellation.md"), "# C");
        File.WriteAllText(Path.Combine(_dir, "notes.txt"), "ignored");
        Directory.CreateDirectory(Path.Combine(_dir, "nested"));
        File.WriteAllText(Path.Combine(_dir, "nested", "deep.md"), "# D");

        var files = await new MarkdownPolicyLoader(_dir).LoadAsync(Ct);

        files.Select(f => f.Name).ShouldBe(["cancellation.md", "shipping.md"]);
        files[0].Content.ShouldBe("# C");
    }

    [Fact]
    public async Task Missing_directory_throws_instead_of_looking_empty() =>
        (await Should.ThrowAsync<DirectoryNotFoundException>(() => new MarkdownPolicyLoader(Path.Combine(_dir, "nope")).LoadAsync(Ct))).Message.ShouldContain("nope");

    [Fact]
    public async Task Empty_directory_returns_empty() =>
        (await new MarkdownPolicyLoader(_dir).LoadAsync(Ct)).ShouldBeEmpty();

    [Fact]
    public async Task Preserves_utf8_text()
    {
        const string text = "# Policy\nအော်ဒါကို ပယ်ဖျက်နိုင်သည် — café ✓\n";
        File.WriteAllText(Path.Combine(_dir, "mm.md"), text, new System.Text.UTF8Encoding(false));

        var files = await new MarkdownPolicyLoader(_dir).LoadAsync(Ct);

        files.Single().Content.ShouldBe(text);
    }

    [Fact]
    public async Task Relative_directory_resolves_against_the_working_directory()
    {
        string relative = Path.GetRelativePath(Directory.GetCurrentDirectory(), _dir);
        File.WriteAllText(Path.Combine(_dir, "a.md"), "# A");

        var files = await new MarkdownPolicyLoader(relative).LoadAsync(Ct);

        files.Single().Name.ShouldBe("a.md");
    }
}
