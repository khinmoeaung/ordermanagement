using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace OrderManagement.Application.Policies;

public sealed partial class PolicyQuestionService(
    IPolicyRetriever retriever,
    IPolicyAnswerGenerator generator,
    ILogger<PolicyQuestionService> logger) : IPolicyQuestionService
{
    private const string Fallback = "Sorry, I couldn't complete that request. Please try again.";
    private const int ExcerptLength = 300;

    public async Task<PolicyAnswer> AskAsync(string question, CancellationToken ct)
    {
        var excerpts = await retriever.RetrieveAsync(question, ct);
        string answer = await generator.GenerateAsync(PolicyPrompt.System, PolicyPrompt.BuildUserMessage(question, excerpts), ct);

        if (string.IsNullOrWhiteSpace(answer))
        {
            logger.LogWarning("Policy answer generator returned an empty answer for {ExcerptCount} excerpts", excerpts.Count);
            return new PolicyAnswer(Fallback, []);
        }

        // Citations come from the [n] markers the model used, never from what it claims; unknown numbers are dropped.
        var cited = CitationMarker().Matches(answer)
            .Select(m => int.TryParse(m.Groups[1].ValueSpan, out int n) ? n : 0)
            .Where(n => n >= 1 && n <= excerpts.Count)
            .Distinct()
            .Order()
            .Select(n => excerpts[n - 1])
            .Select(c => new PolicyCitation(c.Source, c.Title, c.HeadingPath, c.Body.Length > ExcerptLength ? c.Body[..ExcerptLength] : c.Body))
            .ToList();

        logger.LogInformation("Policy question answered with {CitationCount} citations from {ExcerptCount} excerpts", cited.Count, excerpts.Count);
        return new PolicyAnswer(answer, cited);
    }

    [GeneratedRegex(@"\[(\d+)\]")]
    private static partial Regex CitationMarker();
}
