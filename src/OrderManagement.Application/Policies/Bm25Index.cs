namespace OrderManagement.Application.Policies;

/// <summary>An immutable in-memory BM25 index over policy chunks (k1 = 1.5, b = 0.75).</summary>
public sealed class Bm25Index
{
    private const double K1 = 1.5;
    private const double B = 0.75;

    private readonly List<(PolicyChunk Chunk, Dictionary<string, int> Terms, int Length)> _docs = [];
    private readonly Dictionary<string, int> _docFrequency = new(StringComparer.Ordinal);
    private readonly double _averageLength;

    public Bm25Index(IEnumerable<PolicyChunk> chunks)
    {
        foreach (PolicyChunk chunk in chunks)
        {
            var tokens = Bm25Tokenizer.Tokenize(chunk.Text);
            var terms = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (string token in tokens) terms[token] = terms.GetValueOrDefault(token) + 1;
            foreach (string term in terms.Keys) _docFrequency[term] = _docFrequency.GetValueOrDefault(term) + 1;
            _docs.Add((chunk, terms, tokens.Count));
        }

        _averageLength = _docs.Count == 0 ? 0 : _docs.Average(d => d.Length);
    }

    public int ChunkCount => _docs.Count;

    public IReadOnlyList<ScoredChunk> Search(string query, int topK)
    {
        var queryTerms = Bm25Tokenizer.Tokenize(query).Distinct().ToList();
        if (_docs.Count == 0 || queryTerms.Count == 0 || topK <= 0) return [];

        var results = new List<ScoredChunk>();
        foreach (var (chunk, terms, length) in _docs)
        {
            double score = 0;
            foreach (string term in queryTerms)
            {
                if (!terms.TryGetValue(term, out int frequency)) continue;

                int n = _docFrequency[term];
                double idf = Math.Log(1 + (_docs.Count - n + 0.5) / (n + 0.5));
                score += idf * frequency * (K1 + 1) / (frequency + K1 * (1 - B + B * length / _averageLength));
            }

            if (score > 0) results.Add(new ScoredChunk(chunk, score));
        }

        return results.OrderByDescending(r => r.Score).ThenBy(r => r.Chunk.Id).Take(topK).ToList();
    }
}
