using System.Text.Json;
using Microsoft.Extensions.AI;
using OrderManagement.Application.Assistant;
using AiChatResponse = Microsoft.Extensions.AI.ChatResponse;

namespace OrderManagement.Infrastructure.Assistant;

/// <summary>
/// <see cref="IAssistantModel"/> on <see cref="IChatClient"/>, so one implementation serves every provider.
/// Tools go out as declaration-only functions and the client is never wrapped in
/// <c>FunctionInvokingChatClient</c>: a model's tool request comes back as <see cref="FunctionCallContent"/>
/// and only the backend gate runs it.
/// </summary>
public sealed class ChatClientAssistantModel(Func<IChatClient?> clientFactory, int maxTokens) : IAssistantModel
{
    private readonly Lazy<IChatClient?> _client = new(clientFactory);

    public async Task<(IAssistantConversation Conversation, ModelTurn FirstTurn)> StartAsync(
        string systemPrompt, IReadOnlyList<IAssistantTool> tools, string userMessage, CancellationToken ct)
    {
        IChatClient client = _client.Value ?? throw new AssistantNotConfiguredException();

        var options = new ChatOptions
        {
            MaxOutputTokens = maxTokens,
            ToolMode = ChatToolMode.Auto,
            Tools = [.. tools.Select(t => (AITool)AIFunctionFactory.CreateDeclaration(t.Name, t.Description, t.InputSchema, null))],
        };
        var conversation = new Conversation(client, options, systemPrompt, userMessage);
        return (conversation, await conversation.SendAsync(ct));
    }

    private sealed class Conversation : IAssistantConversation
    {
        private readonly IChatClient _client;
        private readonly ChatOptions _options;
        private readonly List<ChatMessage> _history;

        public Conversation(IChatClient client, ChatOptions options, string systemPrompt, string userMessage)
        {
            _client = client;
            _options = options;
            _history = [new ChatMessage(ChatRole.System, systemPrompt), new ChatMessage(ChatRole.User, userMessage)];
        }

        public Task<ModelTurn> ContinueAsync(IReadOnlyList<ToolResult> results, CancellationToken ct)
        {
            // One tool message holding every result, in the order the calls were made.
            _history.Add(new ChatMessage(ChatRole.Tool,
                results.Select(r => (AIContent)new FunctionResultContent(r.CallId, r.Content)).ToList()));
            return SendAsync(ct);
        }

        public async Task<ModelTurn> SendAsync(CancellationToken ct)
        {
            AiChatResponse response;
            try
            {
                response = await _client.GetResponseAsync(_history, _options, ct);
            }
            catch (Exception ex) when (ProviderErrors.IsTransient(ex, ct))
            {
                throw new AssistantUnavailableException(ex);
            }

            EnsureCallIds(response);
            _history.AddRange(response.Messages);   // unchanged: reasoning and signatures must go back as they came
            return ToTurn(response);
        }

        // Some providers (Ollama) send tool calls without IDs. Give each a distinct one in the history itself,
        // so the result we send back carries the same ID as the call it answers.
        private static void EnsureCallIds(AiChatResponse response)
        {
            // CallId is read-only, so a call without one is replaced by an equivalent call that has one.
            foreach (ChatMessage message in response.Messages)
                for (int i = 0; i < message.Contents.Count; i++)
                {
                    if (message.Contents[i] is not FunctionCallContent call || !string.IsNullOrWhiteSpace(call.CallId)) continue;

                    message.Contents[i] = new FunctionCallContent("call_" + Guid.NewGuid().ToString("N"), call.Name, call.Arguments)
                    {
                        RawRepresentation = call.RawRepresentation,
                        AdditionalProperties = call.AdditionalProperties,
                    };
                }
        }

        private static ModelTurn ToTurn(AiChatResponse response)
        {
            List<AIContent> contents = response.Messages.SelectMany(m => m.Contents).ToList();

            List<ToolCall> calls = contents.OfType<FunctionCallContent>()
                .Select(c => new ToolCall(
                    c.CallId, c.Name,
                    JsonSerializer.SerializeToElement(c.Arguments ?? new Dictionary<string, object?>())))
                .ToList();

            string text = string.Join("\n", contents.OfType<TextContent>().Select(t => t.Text).Where(t => !string.IsNullOrWhiteSpace(t)));

            // Tool calls decide the turn, whatever the finish reason says: Ollama and Gemini don't always report ToolCalls.
            ModelStopReason stop =
                calls.Count > 0 ? ModelStopReason.ToolUse
                : response.FinishReason == ChatFinishReason.Stop ? ModelStopReason.EndTurn
                : response.FinishReason == ChatFinishReason.Length ? ModelStopReason.MaxTokens
                : response.FinishReason == ChatFinishReason.ContentFilter ? ModelStopReason.Refusal
                : ModelStopReason.Other;

            return new ModelTurn(stop, text, calls);
        }
    }
}
