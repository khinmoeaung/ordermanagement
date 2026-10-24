using OrderManagement.Application.Assistant;

namespace OrderManagement.UnitTests.Fakes;

/// <summary>Returns queued turns in order and records everything the service sends.</summary>
public sealed class FakeAssistantModel : IAssistantModel, IAssistantConversation
{
    private readonly Queue<ModelTurn> _turns = new();

    public string? SystemPrompt { get; private set; }
    public IReadOnlyList<string> ToolNames { get; private set; } = [];
    public string? UserMessage { get; private set; }
    public List<IReadOnlyList<ToolResult>> ResultBatches { get; } = [];

    public FakeAssistantModel Then(ModelTurn turn)
    {
        _turns.Enqueue(turn);
        return this;
    }

    public static ModelTurn Answer(string text) => new(ModelStopReason.EndTurn, text, []);

    public static ModelTurn Calls(params ToolCall[] calls) => new(ModelStopReason.ToolUse, "", calls);

    public Task<(IAssistantConversation Conversation, ModelTurn FirstTurn)> StartAsync(
        string systemPrompt, IReadOnlyList<IAssistantTool> tools, string userMessage, CancellationToken ct)
    {
        SystemPrompt = systemPrompt;
        ToolNames = tools.Select(t => t.Name).ToList();
        UserMessage = userMessage;
        return Task.FromResult<(IAssistantConversation, ModelTurn)>((this, _turns.Dequeue()));
    }

    public Task<ModelTurn> ContinueAsync(IReadOnlyList<ToolResult> results, CancellationToken ct)
    {
        ResultBatches.Add(results);
        return Task.FromResult(_turns.Dequeue());
    }
}
