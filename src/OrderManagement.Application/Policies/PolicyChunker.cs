using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace OrderManagement.Application.Policies;

/// <summary>Splits a Markdown policy into chunks by heading (levels 1 to 3). Pure: no I/O.</summary>
public static partial class PolicyChunker
{
    public static IReadOnlyList<PolicyChunk> Chunk(string source, string markdown, int maxChars)
    {
        string[] lines = markdown.TrimStart('﻿').Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        string fallbackTitle = Path.GetFileNameWithoutExtension(source);

        var sections = new List<(string Path, string Body)>();
        var headings = new List<(int Level, string Text)>();
        string? title = null;
        string currentPath = fallbackTitle;
        var body = new StringBuilder();
        bool inFence = false;

        void CloseSection() => sections.Add((currentPath, body.ToString().Trim('\n', ' ', '\t')));

        foreach (string line in lines)
        {
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                inFence = !inFence;
                body.Append(line).Append('\n');
                continue;
            }

            Match heading = inFence ? Match.Empty : HeadingPattern().Match(line);
            if (!heading.Success)
            {
                body.Append(line).Append('\n');
                continue;
            }

            CloseSection();
            body.Clear();

            int level = heading.Groups[1].Length;
            string text = heading.Groups[2].Value.Trim();
            headings.RemoveAll(h => h.Level >= level);
            headings.Add((level, text));
            if (level == 1) title ??= text;
            currentPath = string.Join(" > ", headings.Select(h => h.Text));
        }

        CloseSection();

        string documentTitle = title ?? fallbackTitle;
        var chunks = new List<PolicyChunk>();
        foreach ((string path, string sectionBody) in sections)
        {
            if (sectionBody.Length == 0) continue;

            int part = 0;
            foreach (string partBody in SplitBody(sectionBody, maxChars))
            {
                string chunkText = path + "\n\n" + partBody;
                chunks.Add(new PolicyChunk(ChunkId(source, path, part), source, documentTitle, path, part, chunkText, Hash(chunkText)));
                part++;
            }
        }

        return chunks;
    }

    /// <summary>A deterministic ID from the source file, heading path and part, so re-indexing replaces instead of duplicating.</summary>
    public static Guid ChunkId(string source, string headingPath, int part)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{source}|{headingPath}|{part}"));
        return new Guid(hash.AsSpan(0, 16));
    }

    public static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    // Packs whole paragraphs into parts up to maxChars. A paragraph longer than the limit stays whole as its own part.
    private static IEnumerable<string> SplitBody(string body, int maxChars)
    {
        if (body.Length <= maxChars)
        {
            yield return body;
            yield break;
        }

        string? current = null;
        foreach (string paragraph in BlankLine().Split(body).Where(p => p.Trim().Length > 0))
        {
            if (current is null)
                current = paragraph;
            else if (current.Length + 2 + paragraph.Length <= maxChars)
                current += "\n\n" + paragraph;
            else
            {
                yield return current;
                current = paragraph;
            }
        }

        if (current is not null) yield return current;
    }

    [GeneratedRegex(@"^(#{1,3})[ \t]+(\S.*)$")]
    private static partial Regex HeadingPattern();

    [GeneratedRegex(@"\n[ \t]*\n")]
    private static partial Regex BlankLine();
}
