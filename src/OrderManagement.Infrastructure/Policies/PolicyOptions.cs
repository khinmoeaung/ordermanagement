using OrderManagement.Application.Policies;

namespace OrderManagement.Infrastructure.Policies;

public sealed class EmbeddingSettings
{
    /// <summary>The Ollama base URL (Laptop B). Empty means policy search is not configured.</summary>
    public string Endpoint { get; set; } = "";

    public string Model { get; set; } = "qwen3-embedding:0.6b";

    public string QueryInstruction { get; set; } =
        "Given a customer question about shop policies, retrieve the policy passages that answer it";
}

public sealed class QdrantSettings
{
    public string Host { get; set; } = "qdrant";
    public int GrpcPort { get; set; } = 6334;
    public string Collection { get; set; } = "policy_chunks";

    /// <summary>Optional. Comes from the environment, not from source.</summary>
    public string ApiKey { get; set; } = "";
}

public sealed class ChunkingSettings
{
    public int MaxChars { get; set; } = 1500;
}

public sealed class RetrievalSettings
{
    public int TopK { get; set; } = 8;
    public int FinalCount { get; set; } = 4;
    public int RrfK { get; set; } = 60;
}

/// <summary>The "Policies" configuration section.</summary>
public sealed class PolicyOptions
{
    public const string SectionName = "Policies";

    public string Directory { get; set; } = "policies";
    public EmbeddingSettings Embedding { get; set; } = new();
    public QdrantSettings Qdrant { get; set; } = new();
    public ChunkingSettings Chunking { get; set; } = new();
    public RetrievalSettings Retrieval { get; set; } = new();

    public PolicySettings ToSettings() => new(Chunking.MaxChars, Retrieval.TopK, Retrieval.FinalCount, Retrieval.RrfK);

    public static bool Validate(PolicyOptions o) =>
        !string.IsNullOrWhiteSpace(o.Directory)
        && o.Retrieval.TopK > 0
        && o.Retrieval.FinalCount > 0
        && o.Retrieval.FinalCount <= o.Retrieval.TopK
        && o.Retrieval.RrfK > 0
        && o.Chunking.MaxChars > 0
        && !string.IsNullOrWhiteSpace(o.Qdrant.Host)
        && !string.IsNullOrWhiteSpace(o.Qdrant.Collection)
        && o.Qdrant.GrpcPort is >= 1 and <= 65535
        && !string.IsNullOrWhiteSpace(o.Embedding.Model)
        && IsBlankOrAbsoluteHttpUrl(o.Embedding.Endpoint);

    private static bool IsBlankOrAbsoluteHttpUrl(string value) =>
        string.IsNullOrWhiteSpace(value)
        || (Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) && uri.Scheme is "http" or "https");
}
