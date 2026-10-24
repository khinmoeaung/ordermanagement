using Grpc.Core;
using OrderManagement.Application.Policies;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace OrderManagement.Infrastructure.Policies;

/// <summary>
/// Policy chunks as Qdrant points: the point ID is the deterministic chunk ID, so upserting a chunk replaces it.
/// The payload keeps everything needed to rebuild a chunk without another lookup.
/// </summary>
public sealed class QdrantPolicyVectorStore(QdrantClient client, string collection) : IPolicyVectorStore
{
    private const int ScrollPageSize = 256;
    private const int UpsertBatchSize = 64;

    public Task EnsureReadyAsync(int vectorSize, CancellationToken ct) => GuardAsync(async () =>
    {
        if (!await client.CollectionExistsAsync(collection, ct))
        {
            await client.CreateCollectionAsync(
                collection, new VectorParams { Size = (ulong)vectorSize, Distance = Distance.Cosine }, cancellationToken: ct);
            return;
        }

        var info = await client.GetCollectionInfoAsync(collection, ct);
        ulong? existing = info.Config?.Params?.VectorsConfig?.Params?.Size;
        if (existing != (ulong)vectorSize)
            throw new InvalidOperationException(
                $"Qdrant collection '{collection}' holds vectors of size {existing?.ToString() ?? "unknown"} but the embedding model produces size {vectorSize}. " +
                "Delete the collection (or change Policies:Qdrant:Collection) and re-index.");
    });

    public Task<IReadOnlyDictionary<Guid, string>> GetFingerprintsAsync(CancellationToken ct) => GuardAsync<IReadOnlyDictionary<Guid, string>>(async () =>
    {
        var fingerprints = new Dictionary<Guid, string>();
        var payload = new WithPayloadSelector { Include = new PayloadIncludeSelector { Fields = { "content_hash" } } };
        PointId? offset = null;

        try
        {
            do
            {
                var page = await client.ScrollAsync(
                    collection, limit: ScrollPageSize, offset: offset, payloadSelector: payload, cancellationToken: ct);
                foreach (RetrievedPoint point in page.Result)
                    fingerprints[Guid.Parse(point.Id.Uuid)] = point.Payload["content_hash"].StringValue;
                offset = page.NextPageOffset;
            }
            while (offset is not null);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            return new Dictionary<Guid, string>();   // the collection does not exist yet
        }

        return fingerprints;
    });

    public Task UpsertAsync(IReadOnlyList<IndexedChunk> chunks, CancellationToken ct) => GuardAsync(async () =>
    {
        foreach (var batch in chunks.Chunk(UpsertBatchSize))
            await client.UpsertAsync(collection, batch.Select(ToPoint).ToList(), cancellationToken: ct);
    });

    public Task DeleteAsync(IReadOnlyList<Guid> ids, CancellationToken ct) =>
        GuardAsync(() => client.DeleteAsync(collection, ids, cancellationToken: ct));

    public Task<IReadOnlyList<ScoredChunk>> SearchAsync(float[] vector, int topK, CancellationToken ct) => GuardAsync<IReadOnlyList<ScoredChunk>>(async () =>
    {
        try
        {
            var hits = await client.QueryAsync(collection, query: vector, limit: (ulong)topK, payloadSelector: true, cancellationToken: ct);
            return hits.Select(h => new ScoredChunk(ToChunk(h), h.Score)).ToList();
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            return [];   // the collection does not exist yet
        }
    });

    private static PointStruct ToPoint(IndexedChunk indexed)
    {
        PolicyChunk chunk = indexed.Chunk;
        return new PointStruct
        {
            Id = chunk.Id,
            Vectors = indexed.Vector,
            Payload =
            {
                ["source"] = chunk.Source,
                ["title"] = chunk.Title,
                ["heading_path"] = chunk.HeadingPath,
                ["part"] = chunk.Part,
                ["text"] = chunk.Text,
                ["text_hash"] = chunk.ContentHash,
                ["content_hash"] = indexed.Fingerprint,
            },
        };
    }

    private static PolicyChunk ToChunk(ScoredPoint point)
    {
        var p = point.Payload;
        return new PolicyChunk(
            Guid.Parse(point.Id.Uuid), p["source"].StringValue, p["title"].StringValue, p["heading_path"].StringValue,
            (int)p["part"].IntegerValue, p["text"].StringValue, p["text_hash"].StringValue);
    }

    // Qdrant being down or overloaded is an outage the API reports as 503, not a bug.
    private static async Task GuardAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (RpcException ex) when (IsTransient(ex))
        {
            throw new PolicySearchUnavailableException(ex);
        }
    }

    private static async Task<T> GuardAsync<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (RpcException ex) when (IsTransient(ex))
        {
            throw new PolicySearchUnavailableException(ex);
        }
    }

    private static bool IsTransient(RpcException ex) =>
        ex.StatusCode is StatusCode.Unavailable or StatusCode.DeadlineExceeded or StatusCode.ResourceExhausted;
}
