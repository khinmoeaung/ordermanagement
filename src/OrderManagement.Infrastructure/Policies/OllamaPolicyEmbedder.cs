using Microsoft.Extensions.AI;
using OrderManagement.Application.Policies;
using OrderManagement.Infrastructure.Assistant;

namespace OrderManagement.Infrastructure.Policies;

/// <summary>
/// Embeds policy text and questions with one model, so index and query vectors share a space.
/// Qwen3-Embedding expects documents as-is and queries behind an instruction prefix.
/// </summary>
public sealed class OllamaPolicyEmbedder(
    Func<IEmbeddingGenerator<string, Embedding<float>>?> factory, string model, string queryInstruction) : IPolicyEmbedder
{
    private readonly Lazy<IEmbeddingGenerator<string, Embedding<float>>?> _generator = new(factory);

    public string ModelName => model;

    public Task<IReadOnlyList<float[]>> EmbedDocumentsAsync(IReadOnlyList<string> texts, CancellationToken ct) =>
        GenerateAsync(texts, ct);

    public async Task<float[]> EmbedQueryAsync(string question, CancellationToken ct) =>
        (await GenerateAsync([$"Instruct: {queryInstruction}\nQuery: {question}"], ct))[0];

    private async Task<IReadOnlyList<float[]>> GenerateAsync(IReadOnlyList<string> inputs, CancellationToken ct)
    {
        var generator = _generator.Value ?? throw new PolicySearchNotConfiguredException();

        try
        {
            var embeddings = await generator.GenerateAsync(inputs, cancellationToken: ct);
            if (embeddings.Count != inputs.Count)
                throw new InvalidOperationException($"The embedding model returned {embeddings.Count} vectors for {inputs.Count} texts.");

            return embeddings.Select(e => e.Vector.ToArray()).ToList();
        }
        catch (Exception ex) when (ProviderErrors.IsTransient(ex, ct))
        {
            throw new PolicySearchUnavailableException(ex);
        }
    }
}
