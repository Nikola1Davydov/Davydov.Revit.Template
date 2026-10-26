using System.Text.Json;
using Davydov.Revit.Hosting.Commands;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Davydov.Revit.Hosting.Mcp.Tools;

/// <summary>
///     The <c>tools/list</c> and <c>tools/call</c> handlers: a tool call is a command call through the pipeline, with
///     <see cref="CommandSources.Mcp" /> as its source.
/// </summary>
/// <remarks>
///     A failed command is a tool result with <c>isError</c>, not a protocol error, so the AI client sees the code and the
///     message — <c>no_document</c>, <c>document_changed</c>, … — and can react. The call carries no <c>Document</c> header:
///     the document is the one active at the command's first access.
/// </remarks>
internal sealed partial class McpTools(
    McpToolCatalog catalog,
    ICommandExecutor executor,
    IOptions<JsonSerializerOptions> serializerOptions,
    ILogger<McpTools> logger)
{
    private static readonly Dictionary<string, JsonElement> NoArguments = [];

    public ValueTask<ListToolsResult> ListToolsAsync(RequestContext<ListToolsRequestParams> request, CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(new ListToolsResult { Tools = catalog.Tools });
    }

    public async ValueTask<CallToolResult> CallToolAsync(RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken)
    {
        var parameters = request.Params ?? throw new McpProtocolException("The tool call has no parameters.", McpErrorCode.InvalidParams);
        var entry = catalog.Find(parameters.Name) ?? throw new McpProtocolException($"Unknown tool '{parameters.Name}'.", McpErrorCode.InvalidParams);

        var payload = entry.Command.RequestType is null
            ? default
            : JsonSerializer.SerializeToElement(parameters.Arguments ?? NoArguments, serializerOptions.Value);

        var commandRequest = new CommandRequest(entry.Command.Name, payload, CommandSources.Mcp)
        {
            Progress = parameters.ProgressToken is { } token ? new ProgressSink(request.Server, token, logger) : null,
            CancellationToken = cancellationToken
        };

        var result = await executor.ExecuteAsync(commandRequest).ConfigureAwait(false);
        if (result.Error is { } error)
        {
            return new CallToolResult
            {
                IsError = true,
                Content = [new TextContentBlock { Text = $"{error.Code}: {error.Message}" }]
            };
        }

        var value = JsonSerializer.SerializeToElement(result.Value, result.Value?.GetType() ?? typeof(object), serializerOptions.Value);
        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = value.GetRawText() }],
            // Structured content must be an object; a list or a scalar stays text only
            StructuredContent = value.ValueKind == JsonValueKind.Object ? value : null
        };
    }

    [LoggerMessage(LogLevel.Debug, "A progress notification could not be sent")]
    private static partial void LogProgressFailed(ILogger logger, Exception exception);

    /// <summary>
    ///     Progress of one tool call as MCP progress notifications. The command reports a fraction, so <c>total</c> is 1.
    /// </summary>
    private sealed class ProgressSink(McpServer server, ProgressToken token, ILogger logger) : IProgress<ProgressInfo>
    {
        public void Report(ProgressInfo value)
        {
            _ = NotifyAsync(value);
        }

        private async Task NotifyAsync(ProgressInfo value)
        {
            try
            {
                var progress = new ProgressNotificationValue { Progress = (float)value.Fraction, Total = 1, Message = value.Message };
                await server.NotifyProgressAsync(token, progress).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                // The client may be gone; progress is a courtesy, the result still goes out
                LogProgressFailed(logger, exception);
            }
        }
    }
}
