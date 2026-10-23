using System.Text.Json;

namespace OrderManagement.Application.Assistant;

public record ChatRequest(string Message);

public record ChatResponse(string Reply);

public interface IAssistantService
{
    Task<ChatResponse> ChatAsync(ChatRequest request, CancellationToken ct);
}

/// <summary>The application's view of the LLM. Implementations keep provider message state inside the conversation.</summary>
public interface IAssistantModel
{
    Task<(IAssistantConversation Conversation, ModelTurn FirstTurn)> StartAsync(
        string systemPrompt, IReadOnlyList<IAssistantTool> tools, string userMessage, CancellationToken ct);
}

public interface IAssistantConversation
{
    Task<ModelTurn> ContinueAsync(IReadOnlyList<ToolResult> results, CancellationToken ct);
}

public enum ModelStopReason { EndTurn, ToolUse, Refusal, MaxTokens, Other }

public record ModelTurn(ModelStopReason StopReason, string Text, IReadOnlyList<ToolCall> ToolCalls);

/// <summary>A tool call proposed by the model. Everything in it is untrusted input.</summary>
public record ToolCall(string Id, string Name, JsonElement Input);

public record ToolResult(string CallId, string Content, bool IsError);

/// <summary>
/// A tool the model may call. Tools take no identity from model input: the services they call
/// read the caller from <c>ICurrentUser</c>.
/// </summary>
public interface IAssistantTool
{
    string Name { get; }
    string Description { get; }

    /// <summary>JSON Schema for the input, with <c>additionalProperties: false</c>.</summary>
    JsonElement InputSchema { get; }

    /// <summary>Validates the input strictly (throws <see cref="ToolInputException"/>) and returns a restricted DTO.</summary>
    Task<object> ExecuteAsync(JsonElement input, CancellationToken ct);
}

public sealed class ToolInputException(string reason) : Exception(reason);

public sealed class AssistantNotConfiguredException() : Exception("Assistant is not configured.");

public sealed class AssistantUnavailableException(Exception inner)
    : Exception("Assistant is temporarily unavailable.", inner);
