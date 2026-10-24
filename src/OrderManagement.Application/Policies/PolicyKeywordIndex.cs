namespace OrderManagement.Application.Policies;

/// <summary>Holds the current <see cref="Bm25Index"/> and swaps it atomically on re-index, so searches never see a half-built index.</summary>
public sealed class PolicyKeywordIndex
{
    private volatile Bm25Index _index = new([]);

    public int ChunkCount => _index.ChunkCount;

    public void Rebuild(IEnumerable<PolicyChunk> chunks) => _index = new Bm25Index(chunks);

    public IReadOnlyList<ScoredChunk> Search(string query, int topK) => _index.Search(query, topK);
}
