using System.Text;
using OrderManagement.Application.Policies;

namespace OrderManagement.Infrastructure.Policies;

/// <summary>Reads the <c>*.md</c> files directly inside a folder. A relative folder is resolved against the working directory.</summary>
public sealed class MarkdownPolicyLoader(string directory) : IPolicyLoader
{
    private readonly string _directory = Path.GetFullPath(directory);

    public async Task<IReadOnlyList<PolicySourceFile>> LoadAsync(CancellationToken ct)
    {
        if (!Directory.Exists(_directory)) return [];

        var files = new List<PolicySourceFile>();
        foreach (string path in Directory.EnumerateFiles(_directory, "*.md", SearchOption.TopDirectoryOnly)
                     .OrderBy(Path.GetFileName, StringComparer.Ordinal))
            files.Add(new PolicySourceFile(Path.GetFileName(path), await File.ReadAllTextAsync(path, Encoding.UTF8, ct)));

        return files;
    }
}
