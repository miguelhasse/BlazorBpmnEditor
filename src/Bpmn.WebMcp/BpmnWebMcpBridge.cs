using System.Collections.Concurrent;
using System.Text.Json;

using Hasseware.AspNetCore.Components.Bpmn.Tools;

using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace Hasseware.AspNetCore.Components.Bpmn.WebMcp;

/// <summary>Runs the tool calls the browser module sends, on the editor's behalf.</summary>
/// <remarks>
/// <para>
/// Every call comes back as JSON text for a result the module parses, and none throws: whatever goes wrong
/// is a result with <c>ok</c> false, because a rejected promise reaches an agent as an opaque error.
/// </para>
/// <para>
/// Calls arrive from JavaScript outside any render, so each runs through the dispatcher the component
/// supplies, which puts it on the renderer's synchronization context before it touches the session.
/// </para>
/// </remarks>
public sealed partial class BpmnWebMcpBridge
{
    /// <summary>The code of a call the agent cancelled.</summary>
    public const string Cancelled = "CANCELLED";

    private const int MaxRememberedCancellations = 64;

    private readonly IBpmnToolHost _host;
    private readonly BpmnWebMcpToolSet _tools;
    private readonly Func<Func<Task<string>>, Task<string>> _dispatch;
    private readonly ILogger? _logger;
    private readonly BpmnWebMcpGuard? _guard;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _running = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, bool> _cancelledEarly = new(StringComparer.Ordinal);
    private volatile bool _closed;

    /// <summary>Creates a bridge.</summary>
    /// <param name="host">The document the tools work on.</param>
    /// <param name="tools">The tools on offer and their names.</param>
    /// <param name="dispatch">Runs work on the renderer's context; <c>work => work()</c> where there is none.</param>
    /// <param name="logger">Receives unexpected failures.</param>
    /// <param name="guard">Checks every tool that changes the diagram; without one such a tool is refused.</param>
    public BpmnWebMcpBridge(
        IBpmnToolHost host,
        BpmnWebMcpToolSet tools,
        Func<Func<Task<string>>, Task<string>> dispatch,
        ILogger? logger = null,
        BpmnWebMcpGuard? guard = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(dispatch);

        _host = host;
        _tools = tools;
        _dispatch = dispatch;
        _logger = logger;
        _guard = guard;
    }

    /// <summary>Runs one tool call.</summary>
    /// <param name="callId">The module's id for the call, which <see cref="Cancel"/> names.</param>
    /// <param name="tool">The registered name the agent called.</param>
    /// <param name="argumentsJson">The agent's arguments as JSON text.</param>
    /// <returns>The result as JSON text; never an exception.</returns>
    [JSInvokable]
    public async Task<string> ExecuteAsync(string callId, string tool, string argumentsJson)
    {
        // A call that arrives once the editor has gone is told so, not run against a torn-down session.
        if (_closed) return Failure(Cancelled, "The editor was closed.");

        var cancellation = new CancellationTokenSource();
        _running[callId] = cancellation;

        // The module sends a cancellation first when the agent had already given up.
        if (_cancelledEarly.TryRemove(callId, out _)) await cancellation.CancelAsync().ConfigureAwait(false);

        try
        {
            return await _dispatch(() => RunAsync(tool, argumentsJson, cancellation.Token)).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return Failure(Cancelled, "The call was cancelled.");
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (_logger is not null) LogCallFailed(_logger, exception);
            return Failure(BpmnToolCodes.InternalError, "The tool failed unexpectedly.");
        }
        finally
        {
            _running.TryRemove(callId, out _);
            cancellation.Dispose();
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "A WebMCP tool call failed.")]
    private static partial void LogCallFailed(ILogger logger, Exception exception);

    /// <summary>Cancels a call the agent no longer wants.</summary>
    /// <param name="callId">The id the module gave the call.</param>
    [JSInvokable]
    public void Cancel(string callId)
    {
        if (_running.TryGetValue(callId, out var running))
        {
            SafeCancel(running);
            return;
        }

        // Not started yet: remember it, within bounds, for the call that is about to arrive. Past the
        // bound the entry given up is one arbitrary old one, never everyone's.
        while (_cancelledEarly.Count >= MaxRememberedCancellations)
        {
            if (_cancelledEarly.Keys.FirstOrDefault() is not { } evicted) break;
            _cancelledEarly.TryRemove(evicted, out _);
        }

        _cancelledEarly[callId] = true;
    }

    /// <summary>Cancels every call in flight, as the editor goes away.</summary>
    public void CancelAll()
    {
        _closed = true;
        foreach (var running in _running.Values) SafeCancel(running);
    }

    // A call that finishes disposes its source at the moment it is cancelled; the cancellation then has nothing to stop.
    private static void SafeCancel(CancellationTokenSource source)
    {
        try
        {
            source.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async Task<string> RunAsync(string tool, string argumentsJson, CancellationToken cancellationToken)
    {
        // An agent that gave up before the call reached the renderer is not answered.
        cancellationToken.ThrowIfCancellationRequested();

        if (_tools.CatalogNameOf(tool) is not { } catalogName)
        {
            return Failure(BpmnToolCodes.NotFound, "This editor offers no tool with that name.", _host.Revision);
        }

        JsonDocument arguments;
        try
        {
            arguments = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
        }
        catch (JsonException)
        {
            return Failure(BpmnToolCodes.InvalidArgument, "The arguments are not valid JSON.", _host.Revision);
        }

        using (arguments)
        {
            if (arguments.RootElement.ValueKind != JsonValueKind.Object)
            {
                return Failure(BpmnToolCodes.InvalidArgument, "The arguments must be a JSON object.", _host.Revision);
            }

            if (BpmnToolCatalog.Find(catalogName) is { Annotations.ReadOnly: false } changing)
            {
                var refusal = _guard is null
                    ? new BpmnToolResult { Code = BpmnToolCodes.Denied, Message = "There is no way to ask the user, so this was not done.", Revision = _host.Revision }
                    : await _guard.CheckAsync(_host, changing, arguments.RootElement, _host.Origin, cancellationToken).ConfigureAwait(true);
                if (refusal is not null) return BpmnToolJson.Serialize(refusal);
            }

            var result = await BpmnToolCatalog.InvokeAsync(_host, catalogName, arguments.RootElement, cancellationToken).ConfigureAwait(true);
            return BpmnToolJson.Serialize(result);
        }
    }

    // Only code that runs on the renderer's context reads the revision, which the session owns.
    private static string Failure(string code, string message, string? revision = null) => BpmnToolJson.Serialize(new BpmnToolResult
    {
        Code = code,
        Message = message,
        Revision = revision,
    });
}
