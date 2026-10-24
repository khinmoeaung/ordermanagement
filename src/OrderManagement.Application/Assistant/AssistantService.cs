namespace OrderManagement.Application.Assistant;

public sealed class AssistantService(IAssistantModel model, ToolExecutor executor, int maxToolRounds = 5) : IAssistantService
{
    public const string FallbackReply = "Sorry, I couldn't complete that request. Please try again.";
    public const string RefusalReply = "Sorry, I can't help with that request.";
    public const string TooManyCalls = "Too many tool calls.";

    // Bounds the work one request can trigger: at most this many tool executions per model turn.
    private const int MaxCallsPerTurn = 10;

    public async Task<ChatResponse> ChatAsync(ChatRequest request, CancellationToken ct)
    {
        var (conversation, turn) = await model.StartAsync(AssistantPrompt.System, executor.Tools, request.Message, ct);

        int rounds = 0;
        while (turn.StopReason == ModelStopReason.ToolUse)
        {
            if (++rounds > maxToolRounds || turn.ToolCalls.Count == 0) return new ChatResponse(FallbackReply);

            // Every tool_use needs a result, so calls over the cap get an error result instead of running.
            var results = new List<ToolResult>(turn.ToolCalls.Count);
            foreach (ToolCall call in turn.ToolCalls)
                results.Add(results.Count < MaxCallsPerTurn
                    ? await executor.ExecuteAsync(call, ct)
                    : new ToolResult(call.Id, TooManyCalls, IsError: true));

            turn = await conversation.ContinueAsync(results, ct);
        }

        return turn.StopReason switch
        {
            ModelStopReason.EndTurn when !string.IsNullOrWhiteSpace(turn.Text) => new ChatResponse(turn.Text.Trim()),
            ModelStopReason.Refusal => new ChatResponse(RefusalReply),
            _ => new ChatResponse(FallbackReply),
        };
    }
}
