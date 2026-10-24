namespace OrderManagement.Application.Policies;

/// <summary>Lower-cases, splits on non-alphanumerics, drops stop words, and applies a crude stemmer. Used for both documents and queries.</summary>
public static class Bm25Tokenizer
{
    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "a", "an", "and", "are", "as", "at", "be", "by", "for", "from", "has", "in", "is", "it", "of", "on", "or",
        "that", "the", "this", "to", "was", "were", "will", "with", "you", "your",
    };

    private static readonly string[] Suffixes = ["ing", "ed", "es", "s"];

    public static IReadOnlyList<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        int start = -1;
        string lower = text.ToLowerInvariant();

        for (int i = 0; i <= lower.Length; i++)
        {
            bool isWordChar = i < lower.Length && char.IsLetterOrDigit(lower[i]);
            if (isWordChar)
            {
                if (start < 0) start = i;
                continue;
            }

            if (start >= 0)
            {
                string word = lower[start..i];
                start = -1;
                if (word.Length >= 2 && !StopWords.Contains(word)) tokens.Add(Stem(word));
            }
        }

        return tokens;
    }

    private static string Stem(string word)
    {
        foreach (string suffix in Suffixes)
        {
            if (!word.EndsWith(suffix, StringComparison.Ordinal) || word.Length - suffix.Length < 4) continue;
            if (suffix == "s" && word.EndsWith("ss", StringComparison.Ordinal)) continue;

            string stem = word[..^suffix.Length];
            if ((suffix is "ing" or "ed") && stem[^1] == stem[^2]) stem = stem[..^1];
            return stem;
        }

        return word;
    }
}
