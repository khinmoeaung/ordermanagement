using OrderManagement.Application.Policies;

namespace OrderManagement.IntegrationTests;

/// <summary>
/// A scriptable stand-in for the policy question service and indexer, so API tests need neither Qdrant nor Ollama.
/// Each test sets <see cref="OnAsk"/> / <see cref="OnReindex"/> (return a value or throw); <see cref="Reset"/> restores the defaults.
/// </summary>
public sealed class FakePolicyServices : IPolicyQuestionService, IPolicyIndexer
{
    private static readonly PolicyAnswer DefaultAnswer = new("Default answer.", []);
    private static readonly ReindexResult DefaultResult = new(0, 0, 0, 0, 0);

    public Func<string, PolicyAnswer> OnAsk { get; set; } = _ => DefaultAnswer;
    public Func<ReindexResult> OnReindex { get; set; } = () => DefaultResult;
    public List<string> Questions { get; } = [];

    public void Reset()
    {
        OnAsk = _ => DefaultAnswer;
        OnReindex = () => DefaultResult;
        Questions.Clear();
    }

    public Task<PolicyAnswer> AskAsync(string question, CancellationToken ct)
    {
        Questions.Add(question);
        return Task.FromResult(OnAsk(question));
    }

    public Task<ReindexResult> ReindexAsync(CancellationToken ct) => Task.FromResult(OnReindex());
}
