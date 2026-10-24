using Microsoft.Extensions.AI;

namespace OrderManagement.UnitTests.Fakes;

/// <summary>
/// A fake <see cref="IChatClient"/> that replays queued responses (or throws queued exceptions) and records a
/// snapshot of the messages and options of every call. The snapshot matters: the model mutates its history list.
/// </summary>
public sealed class ScriptedChatClient : IChatClient
{
    private readonly Queue<object> _script = new();

    public List<IReadOnlyList<ChatMessage>> Calls { get; } = [];
    public List<ChatOptions?> Options { get; } = [];

    public ScriptedChatClient Reply(ChatResponse response)
    {
        _script.Enqueue(response);
        return this;
    }

    public ScriptedChatClient Throw(Exception exception)
    {
        _script.Enqueue(exception);
        return this;
    }

    public static ChatResponse Text(string text, ChatFinishReason? finish = null) =>
        new(new ChatMessage(ChatRole.Assistant, text)) { FinishReason = finish ?? ChatFinishReason.Stop };

    public static ChatResponse CallsTool(ChatFinishReason? finish, params FunctionCallContent[] calls) =>
        new(new ChatMessage(ChatRole.Assistant, calls.Cast<AIContent>().ToList())) { FinishReason = finish };

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        Calls.Add(messages.ToList());
        Options.Add(options);
        object next = _script.Dequeue();
        return next is Exception ex ? Task.FromException<ChatResponse>(ex) : Task.FromResult((ChatResponse)next);
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}
