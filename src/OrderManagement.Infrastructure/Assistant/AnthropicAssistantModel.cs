using System.Text.Json;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Beta.Messages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using OrderManagement.Application.Assistant;

namespace OrderManagement.Infrastructure.Assistant;

/// <summary>
/// <see cref="IAssistantModel"/> on the Anthropic Messages API (beta endpoint, for server-side fallbacks).
/// The SDK message list lives inside each conversation, so thinking blocks are echoed back unchanged.
/// </summary>
public sealed class AnthropicAssistantModel(IOptions<AssistantOptions> options, IConfiguration configuration) : IAssistantModel
{
    private const string FallbackBeta = "server-side-fallback-2026-07-01";

    private readonly AssistantOptions _options = options.Value;
    private readonly Lazy<AnthropicClient?> _client = new(() => CreateClient(configuration));

    public async Task<(IAssistantConversation Conversation, ModelTurn FirstTurn)> StartAsync(
        string systemPrompt, IReadOnlyList<IAssistantTool> tools, string userMessage, CancellationToken ct)
    {
        AnthropicClient client = _client.Value ?? throw new AssistantNotConfiguredException();
        var conversation = new Conversation(client, _options, systemPrompt, tools.Select(ToBetaTool).ToList());
        ModelTurn first = await conversation.SendAsync(new BetaUserMessageParam { Content = userMessage }, ct);
        return (conversation, first);
    }

    private static AnthropicClient? CreateClient(IConfiguration configuration)
    {
        string? apiKey = configuration["ANTHROPIC_API_KEY"];
        if (string.IsNullOrWhiteSpace(apiKey)) return null;

        string? baseUrl = configuration["ANTHROPIC_BASE_URL"];
        return string.IsNullOrWhiteSpace(baseUrl)
            ? new AnthropicClient { ApiKey = apiKey, Timeout = TimeSpan.FromSeconds(60) }
            : new AnthropicClient { ApiKey = apiKey, Timeout = TimeSpan.FromSeconds(60), BaseUrl = baseUrl };
    }

    // The tool's schema is sent verbatim, so additionalProperties: false reaches the API for strict mode.
    private static BetaToolUnion ToBetaTool(IAssistantTool tool) => new BetaTool
    {
        Name = tool.Name,
        Description = tool.Description,
        Strict = true,
        InputSchema = InputSchema.FromRawUnchecked(
            tool.InputSchema.EnumerateObject().ToDictionary(x => x.Name, x => x.Value.Clone())),
    };

    private sealed class Conversation(
        AnthropicClient client, AssistantOptions options, string systemPrompt, List<BetaToolUnion> tools) : IAssistantConversation
    {
        private readonly List<BetaMessageParam> _messages = [];

        public Task<ModelTurn> ContinueAsync(IReadOnlyList<ToolResult> results, CancellationToken ct)
        {
            List<BetaContentBlockParam> blocks = results
                .Select(res => (BetaContentBlockParam)new BetaToolResultBlockParam
                {
                    ToolUseID = res.CallId,
                    Content = res.Content,
                    IsError = res.IsError,
                })
                .ToList();
            return SendAsync(new BetaUserMessageParam { Content = blocks }, ct);
        }

        public async Task<ModelTurn> SendAsync(BetaMessageParam message, CancellationToken ct)
        {
            _messages.Add(message);

            BetaMessage response;
            try
            {
                response = await client.Beta.Messages.Create(BuildParams(), ct);
            }
            catch (Exception ex) when (IsTransient(ex, ct))
            {
                throw new AssistantUnavailableException(ex);
            }

            _messages.Add(new BetaAssistantMessageParam { Content = EchoContent(response) });
            return ToTurn(response);
        }

        private MessageCreateParams BuildParams() => options.EnableFallbacks
            ? new MessageCreateParams
            {
                Model = options.Model,
                MaxTokens = options.MaxTokens,
                System = systemPrompt,
                Messages = [.. _messages],
                Tools = tools,
                ToolChoice = new BetaToolChoiceAuto(),
                OutputConfig = new BetaOutputConfig { Effort = ParseEffort(options.Effort) },
                Betas = [FallbackBeta],
                Fallbacks = new Default(),
            }
            : new MessageCreateParams
            {
                Model = options.Model,
                MaxTokens = options.MaxTokens,
                System = systemPrompt,
                Messages = [.. _messages],
                Tools = tools,
                ToolChoice = new BetaToolChoiceAuto(),
                OutputConfig = new BetaOutputConfig { Effort = ParseEffort(options.Effort) },
            };

        private static bool IsTransient(Exception ex, CancellationToken ct) => ex switch
        {
            AnthropicRateLimitException or Anthropic5xxException or AnthropicIOException => true,
            TaskCanceledException or TimeoutException => !ct.IsCancellationRequested,
            _ => false,
        };

        // There is no ToParam(): rebuild each block, keeping thinking signatures intact.
        private static List<BetaContentBlockParam> EchoContent(BetaMessage response)
        {
            var echo = new List<BetaContentBlockParam>();
            foreach (BetaContentBlock block in response.Content)
            {
                if (block.TryPickText(out BetaTextBlock? text))
                    echo.Add(new BetaTextBlockParam { Text = text.Text });
                else if (block.TryPickThinking(out BetaThinkingBlock? thinking))
                    echo.Add(new BetaThinkingBlockParam { Thinking = thinking.Thinking, Signature = thinking.Signature });
                else if (block.TryPickRedactedThinking(out BetaRedactedThinkingBlock? redacted))
                    echo.Add(new BetaRedactedThinkingBlockParam { Data = redacted.Data });
                else if (block.TryPickToolUse(out BetaToolUseBlock? toolUse))
                    echo.Add(new BetaToolUseBlockParam { ID = toolUse.ID, Name = toolUse.Name, Input = toolUse.Input });
            }
            return echo;
        }

        private static ModelTurn ToTurn(BetaMessage response)
        {
            var texts = new List<string>();
            var calls = new List<ToolCall>();
            foreach (BetaContentBlock block in response.Content)
            {
                if (block.TryPickText(out BetaTextBlock? text))
                    texts.Add(text.Text);
                else if (block.TryPickToolUse(out BetaToolUseBlock? toolUse))
                    calls.Add(new ToolCall(toolUse.ID, toolUse.Name, JsonSerializer.SerializeToElement(toolUse.Input)));
            }

            ModelStopReason stop = response.StopReason?.Raw() switch
            {
                "end_turn" => ModelStopReason.EndTurn,
                "tool_use" => ModelStopReason.ToolUse,
                "refusal" => ModelStopReason.Refusal,
                "max_tokens" => ModelStopReason.MaxTokens,
                _ => ModelStopReason.Other,
            };
            return new ModelTurn(stop, string.Join("\n", texts), calls);
        }

        private static Effort ParseEffort(string effort) => effort switch
        {
            "low" => Effort.Low,
            "medium" => Effort.Medium,
            "high" => Effort.High,
            "xhigh" => Effort.Xhigh,
            _ => Effort.Max,
        };
    }
}
