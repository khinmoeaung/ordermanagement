using OrderManagement.Application.Policies;

namespace OrderManagement.UnitTests.Fakes;

public sealed class InMemoryPolicyVectorStore : IPolicyVectorStore
{
    public Dictionary<Guid, IndexedChunk> Points { get; } = [];
    public List<IReadOnlyList<IndexedChunk>> UpsertBatches { get; } = [];
    public List<Guid> Deleted { get; } = [];
    public int? VectorSize { get; private set; }

    public Task EnsureReadyAsync(int vectorSize, CancellationToken ct)
    {
        VectorSize = vectorSize;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyDictionary<Guid, string>> GetFingerprintsAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyDictionary<Guid, string>>(Points.ToDictionary(p => p.Key, p => p.Value.Fingerprint));

    public Task UpsertAsync(IReadOnlyList<IndexedChunk> chunks, CancellationToken ct)
    {
        UpsertBatches.Add(chunks);
        foreach (IndexedChunk chunk in chunks) Points[chunk.Chunk.Id] = chunk;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(IReadOnlyList<Guid> ids, CancellationToken ct)
    {
        foreach (Guid id in ids)
        {
            Points.Remove(id);
            Deleted.Add(id);
        }

        return Task.CompletedTask;
    }

    /// <summary>Nearest by squared Euclidean distance, as a stand-in for the real similarity search.</summary>
    public Task<IReadOnlyList<ScoredChunk>> SearchAsync(float[] vector, int topK, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ScoredChunk>>(Points.Values
            .Select(p => new ScoredChunk(p.Chunk, -p.Vector.Zip(vector, (a, b) => (double)(a - b) * (a - b)).Sum()))
            .OrderByDescending(s => s.Score)
            .Take(topK)
            .ToList());
}
