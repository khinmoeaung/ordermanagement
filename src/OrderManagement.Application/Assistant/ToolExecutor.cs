using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using OrderManagement.Domain.Common;

namespace OrderManagement.Application.Assistant;

/// <summary>
/// The only path from a model tool call to data. A call runs only if its name is registered,
/// its input passes the tool's strict validation, and the tool's service call authorizes the
/// signed-in user. Failures become error results for the model, never HTTP errors.
/// </summary>
public sealed class ToolExecutor
{
    private static readonly JsonSerializerOptions ResultJson =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private readonly Dictionary<string, IAssistantTool> _tools;
    private readonly ILogger<ToolExecutor> _logger;

    public ToolExecutor(IEnumerable<IAssistantTool> tools, ILogger<ToolExecutor> logger)
    {
        _tools = new Dictionary<string, IAssistantTool>(StringComparer.Ordinal);
        foreach (IAssistantTool tool in tools)
            if (!_tools.TryAdd(tool.Name, tool))
                throw new InvalidOperationException($"Duplicate assistant tool name '{tool.Name}'.");
        Tools = [.. _tools.Values];
        _logger = logger;
    }

    public IReadOnlyList<IAssistantTool> Tools { get; }

    public async Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken ct)
    {
        long started = Stopwatch.GetTimestamp();

        if (!_tools.TryGetValue(call.Name, out IAssistantTool? tool))
            return Finish(call, "unknown_tool", "Unknown tool.", isError: true, started);

        try
        {
            object result = await tool.ExecuteAsync(call.Input, ct);
            return Finish(call, "ok", JsonSerializer.Serialize(result, result.GetType(), ResultJson), isError: false, started);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ToolInputException ex)
        {
            return Finish(call, "invalid_input", "Invalid arguments: " + ex.Message, isError: true, started);
        }
        catch (NotFoundException)
        {
            return Finish(call, "not_found", "Order not found.", isError: true, started);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Assistant tool {Tool} threw an unexpected exception", call.Name);
            return Finish(call, "failed", "Tool failed.", isError: true, started);
        }
    }

    private ToolResult Finish(ToolCall call, string outcome, string content, bool isError, long started)
    {
        // Never log argument values or result content: they may hold customer data.
        _logger.LogInformation("Assistant tool {Tool} {Outcome} in {ElapsedMs}ms",
            call.Name, outcome, (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        return new ToolResult(call.Id, content, isError);
    }
}
