using OrderManagement.Application.Policies;

namespace OrderManagement.UnitTests.Fakes;

/// <summary>Deterministic 4-dimensional vectors derived from the text. Counts calls and tracks overlap.</summary>
public sealed class FakePolicyEmbedder : IPolicyEmbedder
{
    private int _concurrent;

    public string ModelName { get; set; } = "fake-model";
    public int DocumentCalls { get; private set; }
    public int TextsEmbedded { get; private set; }
    public int MaxConcurrent { get; private set; }
    public TimeSpan Delay { get; set; } = TimeSpan.Zero;
    public Exception? Throw { get; set; }

    public async Task<IReadOnlyList<float[]>> EmbedDocumentsAsync(IReadOnlyList<string> texts, CancellationToken ct)
    {
        int now = Interlocked.Increment(ref _concurrent);
        MaxConcurrent = Math.Max(MaxConcurrent, now);
        try
        {
            DocumentCalls++;
            if (Delay > TimeSpan.Zero) await Task.Delay(Delay, ct);
            if (Throw is not null) throw Throw;

            TextsEmbedded += texts.Count;
            return texts.Select(VectorFor).ToList();
        }
        finally
        {
            Interlocked.Decrement(ref _concurrent);
        }
    }

    public Task<float[]> EmbedQueryAsync(string question, CancellationToken ct) => Task.FromResult(VectorFor(question));

    public static float[] VectorFor(string text)
    {
        int h = text.Sum(c => c);
        return [h & 0xFF, (h >> 8) & 0xFF, text.Length, 1];
    }
}
