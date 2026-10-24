namespace OrderManagement.Infrastructure.Assistant;

public enum AssistantProvider { Claude, OpenAI, Gemini, Ollama }

public sealed class ModelSettings
{
    public string Model { get; set; } = "";
}

public sealed class OpenAISettings
{
    public string Model { get; set; } = "gpt-5-mini";   // unverified default: confirm against OpenAI's model list

    /// <summary>Optional. For tests and OpenAI-compatible servers.</summary>
    public string Endpoint { get; set; } = "";
}

public sealed class OllamaSettings
{
    public string Endpoint { get; set; } = "";
    public string Model { get; set; } = "qwen2.5:7b";
}

/// <summary>The "Assistant" configuration section. API keys are read from the environment, not from this section.</summary>
public sealed class AssistantOptions
{
    public const string SectionName = "Assistant";

    public AssistantProvider Provider { get; set; } = AssistantProvider.Claude;
    public int MaxTokens { get; set; } = 4096;
    public int MaxToolRounds { get; set; } = 5;

    public ModelSettings Claude { get; set; } = new() { Model = "claude-haiku-5-5" };
    public OpenAISettings OpenAI { get; set; } = new();
    public ModelSettings Gemini { get; set; } = new() { Model = "gemini-3.5-flash" };   // unverified default
    public OllamaSettings Ollama { get; set; } = new();

    public string SelectedModel => Provider switch
    {
        AssistantProvider.Claude => Claude.Model,
        AssistantProvider.OpenAI => OpenAI.Model,
        AssistantProvider.Gemini => Gemini.Model,
        _ => Ollama.Model,
    };

    public static bool Validate(AssistantOptions o) =>
        o.MaxTokens > 0
        && o.MaxToolRounds is >= 1 and <= 10
        && !string.IsNullOrWhiteSpace(o.SelectedModel)
        && IsBlankOrAbsoluteHttpUrl(o.Ollama.Endpoint)
        && IsBlankOrAbsoluteHttpUrl(o.OpenAI.Endpoint);

    private static bool IsBlankOrAbsoluteHttpUrl(string value) =>
        string.IsNullOrWhiteSpace(value)
        || (Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) && uri.Scheme is "http" or "https");
}
