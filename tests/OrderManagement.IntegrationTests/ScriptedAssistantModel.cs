using OrderManagement.Application.Assistant;

namespace OrderManagement.IntegrationTests;

/// <summary>
/// Stands in for Claude in API tests. Each scripted step receives the tool results sent back by the
/// service (null for the first turn) and returns the next model turn. Everything the service sends is recorded.
/// </summary>
public sealed class ScriptedAssistantModel : IAssistantModel
{
    private readonly Queue<Func<IReadOnlyList<ToolResult>?, ModelTurn>> _steps = new();
    private readonly List<IReadOnlyList<ToolResult>> _received = [];

    public IReadOnlyList<IReadOnlyList<ToolResult>> ReceivedResults
    {
        get { lock (_received) return [.. _received]; }
    }

    public void Script(params Func<IReadOnlyList<ToolResult>?, ModelTurn>[] steps)
    {
        lock (_steps)
            foreach (var step in steps) _steps.Enqueue(step);
    }

    public void Reset()
    {
        lock (_steps) _steps.Clear();
        lock (_received) _received.Clear();
    }

    public Task<(IAssistantConversation Conversation, ModelTurn FirstTurn)> StartAsync(
        string systemPrompt, IReadOnlyList<IAssistantTool> tools, string userMessage, CancellationToken ct) =>
        Task.FromResult<(IAssistantConversation, ModelTurn)>((new Conversation(this), Next(null)));

    private ModelTurn Next(IReadOnlyList<ToolResult>? results)
    {
        if (results is not null)
            lock (_received) _received.Add(results);
        lock (_steps) return _steps.Dequeue()(results);
    }

    private sealed class Conversation(ScriptedAssistantModel model) : IAssistantConversation
    {
        public Task<ModelTurn> ContinueAsync(IReadOnlyList<ToolResult> results, CancellationToken ct) =>
            Task.FromResult(model.Next(results));
    }
}
