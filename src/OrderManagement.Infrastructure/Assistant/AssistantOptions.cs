namespace OrderManagement.Infrastructure.Assistant;

/// <summary>The "Assistant" configuration section. The API key is read separately from ANTHROPIC_API_KEY.</summary>
public sealed class AssistantOptions
{
    public const string SectionName = "Assistant";

    private static readonly string[] Efforts = ["low", "medium", "high", "xhigh", "max"];

    public string Model { get; set; } = "claude-opus-5-5";
    public int MaxTokens { get; set; } = 16000;
    public string Effort { get; set; } = "low";
    public int MaxToolRounds { get; set; } = 5;
    public bool EnableFallbacks { get; set; } = true;

    public static bool Validate(AssistantOptions o) =>
        !string.IsNullOrWhiteSpace(o.Model)
        && o.MaxTokens > 0
        && o.MaxToolRounds is >= 1 and <= 10
        && Efforts.Contains(o.Effort, StringComparer.Ordinal);
}
