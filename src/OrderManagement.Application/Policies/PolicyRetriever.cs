using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace OrderManagement.Application.Policies;

/// <summary>Hybrid retrieval: semantic (vector store) and keyword (BM25) results fused with reciprocal rank fusion.</summary>
public sealed class PolicyRetriever(
    IPolicyEmbedder embedder,
    IPolicyVectorStore store,
    PolicyKeywordIndex keywordIndex,
    PolicySettings settings,
    ILogger<PolicyRetriever> logger) : IPolicyRetriever
{
    public async Task<IReadOnlyList<PolicyChunk>> RetrieveAsync(string question, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        float[] vector = await embedder.EmbedQueryAsync(question, ct);
        long embedMs = stopwatch.ElapsedMilliseconds;

        var semantic = await store.SearchAsync(vector, settings.TopK, ct);
        long semanticMs = stopwatch.ElapsedMilliseconds - embedMs;
        if (semantic.Count == 0) throw new PoliciesNotIndexedException();

        var keyword = keywordIndex.Search(question, settings.TopK);

        var byId = new Dictionary<Guid, PolicyChunk>();
        foreach (ScoredChunk hit in semantic.Concat(keyword)) byId.TryAdd(hit.Chunk.Id, hit.Chunk);

        var fused = ReciprocalRankFusion.Fuse(
            [semantic.Select(h => h.Chunk.Id).ToList(), keyword.Select(h => h.Chunk.Id).ToList()],
            settings.RrfK);
        var result = fused.Take(settings.FinalCount).Select(f => byId[f.Id]).ToList();

        logger.LogInformation(
            "Policy retrieval: embed {EmbedMs} ms, semantic {SemanticMs} ms; {SemanticHits} semantic and {KeywordHits} keyword hits; selected {ChunkIds}",
            embedMs, semanticMs, semantic.Count, keyword.Count, result.Select(c => c.Id));
        return result;
    }
}
