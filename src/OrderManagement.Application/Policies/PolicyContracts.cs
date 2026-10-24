namespace OrderManagement.Application.Policies;

public sealed record PolicySettings(int MaxChunkChars, int TopK, int FinalCount, int RrfK);

public interface IPolicyLoader
{
    Task<IReadOnlyList<PolicySourceFile>> LoadAsync(CancellationToken ct);
}

public interface IPolicyEmbedder
{
    /// <summary>The embedding model name; part of every chunk's fingerprint, so a model change re-embeds everything.</summary>
    string ModelName { get; }

    Task<IReadOnlyList<float[]>> EmbedDocumentsAsync(IReadOnlyList<string> texts, CancellationToken ct);

    /// <summary>Embeds a question with the model's query instruction prefix.</summary>
    Task<float[]> EmbedQueryAsync(string question, CancellationToken ct);
}

public interface IPolicyVectorStore
{
    Task EnsureReadyAsync(int vectorSize, CancellationToken ct);

    /// <summary>Stored chunk ID to fingerprint. Empty when the collection does not exist.</summary>
    Task<IReadOnlyDictionary<Guid, string>> GetFingerprintsAsync(CancellationToken ct);

    Task UpsertAsync(IReadOnlyList<IndexedChunk> chunks, CancellationToken ct);

    Task DeleteAsync(IReadOnlyList<Guid> ids, CancellationToken ct);

    /// <summary>Nearest chunks by vector. Empty when the collection does not exist.</summary>
    Task<IReadOnlyList<ScoredChunk>> SearchAsync(float[] vector, int topK, CancellationToken ct);
}

public interface IPolicyAnswerGenerator
{
    Task<string> GenerateAsync(string systemPrompt, string userMessage, CancellationToken ct);
}

public interface IPolicyIndexer
{
    Task<ReindexResult> ReindexAsync(CancellationToken ct);
}

public interface IPolicyRetriever
{
    Task<IReadOnlyList<PolicyChunk>> RetrieveAsync(string question, CancellationToken ct);
}

public interface IPolicyQuestionService
{
    Task<PolicyAnswer> AskAsync(string question, CancellationToken ct);
}

public sealed class PolicySearchNotConfiguredException() : Exception("Policy search is not configured.");

public sealed class PolicySearchUnavailableException(Exception inner) : Exception("Policy search is temporarily unavailable.", inner);

public sealed class PoliciesNotIndexedException() : Exception("Policies have not been indexed yet.");
