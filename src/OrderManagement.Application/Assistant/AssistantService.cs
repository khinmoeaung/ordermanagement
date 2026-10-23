namespace OrderManagement.Application.Assistant;

public sealed class AssistantService(IAssistantModel model, ToolExecutor executor, int maxToolRounds = 5) : IAssistantService
{
    public const string FallbackReply = "Sorry, I couldn't complete that request. Please try again.";
    public const string RefusalReply = "Sorry, I can't help with that request.";

    public async Task<ChatResponse> ChatAsync(ChatRequest request, CancellationToken ct)
    {
        var (conversation, turn) = await model.StartAsync(AssistantPrompt.System, executor.Tools, request.Message, ct);

        int rounds = 0;
        while (turn.StopReason == ModelStopReason.ToolUse)
        {
            if (++rounds > maxToolRounds) return new ChatResponse(FallbackReply);

            var results = new List<ToolResult>(turn.ToolCalls.Count);
            foreach (ToolCall call in turn.ToolCalls)
                results.Add(await executor.ExecuteAsync(call, ct));

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
