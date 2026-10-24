namespace OrderManagement.Application.Policies;

/// <summary>One searchable piece of a policy document. <c>Text</c> is the heading path, a blank line, then the body.</summary>
public sealed record PolicyChunk(
    Guid Id, string Source, string Title, string HeadingPath, int Part, string Text, string ContentHash)
{
    /// <summary>The text after the heading path and the blank line that follows it.</summary>
    public string Body => Text.Length > HeadingPath.Length + 2 ? Text[(HeadingPath.Length + 2)..] : "";
}

public sealed record PolicySourceFile(string Name, string Content);

public sealed record ScoredChunk(PolicyChunk Chunk, double Score);

/// <summary>A chunk ready to store: its vector, and the fingerprint (embedding model + text hash) that decides whether it needs re-embedding.</summary>
public sealed record IndexedChunk(PolicyChunk Chunk, string Fingerprint, float[] Vector);

public sealed record ReindexResult(int Added, int Updated, int Unchanged, int Deleted, int TotalChunks);

public sealed record PolicyCitation(string Source, string Title, string Section, string Excerpt);

public sealed record PolicyAnswer(string Answer, IReadOnlyList<PolicyCitation> Citations);

public sealed record AskPolicyRequest(string Question);
