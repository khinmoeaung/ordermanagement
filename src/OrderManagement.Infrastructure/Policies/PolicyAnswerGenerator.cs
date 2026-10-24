using Microsoft.Extensions.AI;
using OrderManagement.Application.Assistant;
using OrderManagement.Application.Policies;
using OrderManagement.Infrastructure.Assistant;

namespace OrderManagement.Infrastructure.Policies;

/// <summary>
/// Asks the configured assistant provider (<c>Assistant:Provider</c>) one grounded question. No tools are offered:
/// policy answers come only from the excerpts in the prompt.
/// </summary>
public sealed class PolicyAnswerGenerator(Func<IChatClient?> clientFactory, int maxTokens) : IPolicyAnswerGenerator
{
    private readonly Lazy<IChatClient?> _client = new(clientFactory);

    public PolicyAnswerGenerator(ChatClientFactory factory, int maxTokens) : this(factory.Create, maxTokens)
    {
    }

    public async Task<string> GenerateAsync(string systemPrompt, string userMessage, CancellationToken ct)
    {
        IChatClient client = _client.Value ?? throw new AssistantNotConfiguredException();

        try
        {
            var response = await client.GetResponseAsync(
                [new ChatMessage(ChatRole.System, systemPrompt), new ChatMessage(ChatRole.User, userMessage)],
                new ChatOptions { MaxOutputTokens = maxTokens },
                ct);
            return response.Text;
        }
        catch (Exception ex) when (ProviderErrors.IsTransient(ex, ct))
        {
            throw new AssistantUnavailableException(ex);
        }
    }
}
