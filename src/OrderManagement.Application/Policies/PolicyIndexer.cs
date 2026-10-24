using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace OrderManagement.Application.Policies;

/// <summary>
/// Brings the vector store in line with the policy files. Chunk IDs are deterministic and each chunk carries a
/// fingerprint (embedding model + text hash), so only new or changed chunks are embedded and nothing is duplicated.
/// </summary>
public sealed class PolicyIndexer(
    IPolicyLoader loader,
    IPolicyEmbedder embedder,
    IPolicyVectorStore store,
    PolicyKeywordIndex keywordIndex,
    PolicySettings settings,
    ILogger<PolicyIndexer> logger) : IPolicyIndexer
{
    private const int EmbedBatchSize = 32;

    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<ReindexResult> ReindexAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            return await ReindexCoreAsync(ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<ReindexResult> ReindexCoreAsync(CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();

        var files = await loader.LoadAsync(ct);
        var chunks = files.SelectMany(f => PolicyChunker.Chunk(f.Name, f.Content, settings.MaxChunkChars)).ToList();

        // Keyword search must keep working even if Qdrant or Ollama fail below.
        keywordIndex.Rebuild(chunks);

        var stored = await store.GetFingerprintsAsync(ct);
        var toEmbed = new List<(PolicyChunk Chunk, string Fingerprint)>();
        int added = 0, updated = 0, unchanged = 0;

        foreach (PolicyChunk chunk in chunks)
        {
            string fingerprint = PolicyChunker.Hash(embedder.ModelName + "|" + chunk.ContentHash);
            if (!stored.TryGetValue(chunk.Id, out string? existing))
            {
                added++;
                toEmbed.Add((chunk, fingerprint));
            }
            else if (existing != fingerprint)
            {
                updated++;
                toEmbed.Add((chunk, fingerprint));
            }
            else
            {
                unchanged++;
            }
        }

        if (toEmbed.Count > 0)
        {
            var indexed = new List<IndexedChunk>(toEmbed.Count);
            foreach (var batch in toEmbed.Chunk(EmbedBatchSize))
            {
                var vectors = await embedder.EmbedDocumentsAsync(batch.Select(b => b.Chunk.Text).ToList(), ct);
                for (int i = 0; i < batch.Length; i++)
                    indexed.Add(new IndexedChunk(batch[i].Chunk, batch[i].Fingerprint, vectors[i]));
            }

            await store.EnsureReadyAsync(indexed[0].Vector.Length, ct);
            await store.UpsertAsync(indexed, ct);
        }

        var currentIds = chunks.Select(c => c.Id).ToHashSet();
        var staleIds = stored.Keys.Where(id => !currentIds.Contains(id)).ToList();
        if (staleIds.Count > 0) await store.DeleteAsync(staleIds, ct);

        var result = new ReindexResult(added, updated, unchanged, staleIds.Count, chunks.Count);
        logger.LogInformation(
            "Policy re-index finished in {ElapsedMs} ms: {Added} added, {Updated} updated, {Unchanged} unchanged, {Deleted} deleted, {TotalChunks} chunks",
            stopwatch.ElapsedMilliseconds, result.Added, result.Updated, result.Unchanged, result.Deleted, result.TotalChunks);
        return result;
    }
}
