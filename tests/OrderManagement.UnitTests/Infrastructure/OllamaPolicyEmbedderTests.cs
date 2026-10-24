using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.AI;
using OllamaSharp;
using OrderManagement.Application.Policies;
using OrderManagement.Infrastructure.Policies;
using Shouldly;

namespace OrderManagement.UnitTests.Infrastructure;

public sealed class OllamaPolicyEmbedderTests : IDisposable
{
    private const string Model = "qwen3-embedding:0.6b";
    private const string Instruction = "Given a question, retrieve the passages that answer it";

    private readonly StubApiServer _server = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _server.Dispose();

    private static OllamaPolicyEmbedder EmbedderAt(string endpoint) =>
        new(() => string.IsNullOrWhiteSpace(endpoint) ? null : Generator(endpoint), Model, Instruction);

    private static IEmbeddingGenerator<string, Embedding<float>> Generator(string endpoint) =>
        new OllamaApiClient(new HttpClient { BaseAddress = new Uri(endpoint), Timeout = TimeSpan.FromSeconds(10) }, Model);

    private static string Embeddings(params string[] vectors) =>
        $$"""{"model":"{{Model}}","embeddings":[{{string.Join(",", vectors)}}],"total_duration":1,"load_duration":1,"prompt_eval_count":1}""";

    [Fact]
    public async Task Documents_are_sent_without_a_prefix_and_vectors_come_back_in_order()
    {
        _server.Reply(Embeddings("[0.5,0.25]", "[1.0,2.0]"));

        var vectors = await EmbedderAt(_server.BaseUrl).EmbedDocumentsAsync(["first text", "second text"], Ct);

        vectors.Count.ShouldBe(2);
        vectors[0].ShouldBe([0.5f, 0.25f]);
        vectors[1].ShouldBe([1.0f, 2.0f]);
        var request = _server.Requests.Single();
        request.GetProperty("input").EnumerateArray().Select(e => e.GetString()).ShouldBe(["first text", "second text"]);
        request.GetProperty("model").GetString().ShouldBe(Model);
    }

    [Fact]
    public async Task Query_is_sent_with_the_instruct_prefix()
    {
        _server.Reply(Embeddings("[0.1,0.2]"));

        var vector = await EmbedderAt(_server.BaseUrl).EmbedQueryAsync("Can I cancel?", Ct);

        vector.ShouldBe([0.1f, 0.2f]);
        _server.Requests.Single().GetProperty("input").EnumerateArray().Single().GetString()
            .ShouldBe($"Instruct: {Instruction}\nQuery: Can I cancel?");
    }

    [Fact]
    public void Model_name_is_the_configured_model() => EmbedderAt(_server.BaseUrl).ModelName.ShouldBe(Model);

    [Fact]
    public async Task Blank_endpoint_throws_not_configured()
    {
        var embedder = EmbedderAt("");

        await Should.ThrowAsync<PolicySearchNotConfiguredException>(() => embedder.EmbedQueryAsync("q", Ct));
        await Should.ThrowAsync<PolicySearchNotConfiguredException>(() => embedder.EmbedDocumentsAsync(["t"], Ct));
    }

    [Fact]
    public async Task Unreachable_server_becomes_unavailable()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int closedPort = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        await Should.ThrowAsync<PolicySearchUnavailableException>(
            () => EmbedderAt($"http://127.0.0.1:{closedPort}").EmbedQueryAsync("q", Ct));
    }

    [Fact]
    public async Task Server_error_becomes_unavailable()
    {
        _server.FallbackStatus = 500;

        await Should.ThrowAsync<PolicySearchUnavailableException>(() => EmbedderAt(_server.BaseUrl).EmbedQueryAsync("q", Ct));
    }

    [Fact]
    public async Task Caller_cancellation_is_not_unavailable()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var ex = await Should.ThrowAsync<Exception>(() => EmbedderAt(_server.BaseUrl).EmbedQueryAsync("q", cts.Token));

        ex.ShouldBeAssignableTo<OperationCanceledException>();
    }

    [Fact]
    public async Task The_generator_is_created_once()
    {
        int created = 0;
        _server.Reply(Embeddings("[1.0]")).Reply(Embeddings("[2.0]"));
        var embedder = new OllamaPolicyEmbedder(() => { created++; return Generator(_server.BaseUrl); }, Model, Instruction);

        await embedder.EmbedQueryAsync("a", Ct);
        await embedder.EmbedQueryAsync("b", Ct);

        created.ShouldBe(1);
    }
}
