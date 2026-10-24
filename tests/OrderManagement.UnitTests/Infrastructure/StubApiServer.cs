using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace OrderManagement.UnitTests.Infrastructure;

/// <summary>A local HTTP server standing in for a provider API: replies from a queue and records request bodies.</summary>
public sealed class StubApiServer : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly Queue<(int Status, string Body)> _responses = new();
    private readonly Task _loop;

    public StubApiServer()
    {
        int port = FreePort();
        BaseUrl = $"http://127.0.0.1:{port}";
        _listener.Prefixes.Add(BaseUrl + "/");
        _listener.Start();
        _loop = Task.Run(ServeAsync);
    }

    public string BaseUrl { get; }
    public List<JsonElement> Requests { get; } = [];
    public string? FallbackBody { get; set; }
    public int FallbackStatus { get; set; } = 500;

    public StubApiServer Reply(string body, int status = 200)
    {
        lock (_responses) _responses.Enqueue((status, body));
        return this;
    }

    public static string Message(string stopReason, params string[] contentBlocks) =>
        $$$"""
        {"id":"msg_1","type":"message","role":"assistant","model":"claude-opus-5-5",
         "content":[{{{string.Join(",", contentBlocks)}}}],
         "stop_reason":"{{{stopReason}}}","stop_sequence":null,
         "usage":{"input_tokens":10,"output_tokens":5}}
        """;

    public static string Text(string text) => $$"""{"type":"text","text":{{JsonSerializer.Serialize(text)}}}""";

    public static string ToolUse(string id, string name, string inputJson) =>
        $$"""{"type":"tool_use","id":"{{id}}","name":"{{name}}","input":{{inputJson}}}""";

    public static string Thinking(string thinking, string signature) =>
        $$"""{"type":"thinking","thinking":{{JsonSerializer.Serialize(thinking)}},"signature":"{{signature}}"}""";

    private async Task ServeAsync()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync(); }
            catch (Exception) { return; }

            using (var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8))
            {
                string body = await reader.ReadToEndAsync();
                lock (Requests) Requests.Add(JsonDocument.Parse(body).RootElement.Clone());
            }

            (int status, string reply) next;
            lock (_responses)
                next = _responses.Count > 0 ? _responses.Dequeue() : (FallbackStatus, FallbackBody ?? """{"type":"error","error":{"type":"api_error","message":"boom"}}""");

            byte[] bytes = Encoding.UTF8.GetBytes(next.reply);
            ctx.Response.StatusCode = next.status;
            ctx.Response.ContentType = "application/json";
            if (next.status >= 429) ctx.Response.Headers["retry-after-ms"] = "1";   // keeps SDK retries fast in tests
            ctx.Response.ContentLength64 = bytes.Length;
            await ctx.Response.OutputStream.WriteAsync(bytes);
            ctx.Response.Close();
        }
    }

    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    public void Dispose()
    {
        _listener.Stop();
        _listener.Close();
    }
}
