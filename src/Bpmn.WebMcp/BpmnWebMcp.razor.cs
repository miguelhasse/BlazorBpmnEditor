using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace Hasseware.AspNetCore.Components.Bpmn.WebMcp;

/// <summary>Where a <see cref="BpmnWebMcp"/> stands.</summary>
public enum BpmnWebMcpState
{
    /// <summary>Not yet rendered in a browser, or still registering.</summary>
    Pending,

    /// <summary>The browser has no WebMCP; nothing was registered and nothing is wrong.</summary>
    Unsupported,

    /// <summary>The tools are registered.</summary>
    Registered,

    /// <summary>The browser has WebMCP but no tool could be registered.</summary>
    Failed,
}

/// <summary>
/// Offers the editor it is placed in to the browser's agents through WebMCP: an agent can look at the
/// diagram, validate it and export it, and, when <see cref="BpmnWebMcpOptions.AllowMutations"/> is on, edit it.
/// </summary>
/// <remarks>
/// <para>
/// Place it inside the editor's scope; it draws nothing. Where the browser has no WebMCP it does
/// nothing, quietly: <see cref="Status"/> is <see cref="BpmnWebMcpState.Unsupported"/> and
/// <see cref="OnUnavailable"/> is raised, so a host can say so if it wants to.
/// </para>
/// <para>
/// Tools are registered after the first render in the browser, never while prerendering, and withdrawn when
/// the component is disposed. The editor must be bound to its stored document (if it is to have one) before
/// then, because the persistence tools are offered only to a bound editor. Preview: the tools, their names and this component's parameters may change.
/// </para>
/// </remarks>
public sealed partial class BpmnWebMcp : IAsyncDisposable
{
    private readonly CancellationTokenSource _disposal = new();
    private IJSObjectReference? _module;
    private DotNetObjectReference<BpmnWebMcpBridge>? _reference;
    private BpmnWebMcpBridge? _bridge;
    private int? _registrationId;
    private string? _suffix;
    private bool _disposed;
    private TaskCompletionSource? _registering;

    /// <summary>The prefix of every tool name; the option's, <c>bpmn_</c> by default, when not set. Read once, at registration.</summary>
    [Parameter]
    public string? ToolPrefix { get; set; }

    /// <summary>
    /// Tells this editor's tools from another's when a page holds several, by appending <c>_name</c>. An
    /// unnamed second editor is given <c>_2</c>, then <c>_3</c>, and a warning is logged.
    /// </summary>
    [Parameter]
    public string? InstanceName { get; set; }

    /// <summary>
    /// Whether this editor's agents may change the diagram, in place of <see cref="BpmnWebMcpOptions.AllowMutations"/>
    /// when set. Read once, at registration. Deleting and saving still ask the user.
    /// </summary>
    [Parameter]
    public bool? AllowMutations { get; set; }

    /// <summary>Raised when the tools will not be offered, because the browser has no WebMCP or refused them all.</summary>
    [Parameter]
    public EventCallback OnUnavailable { get; set; }

    /// <summary>Raised once the tools are registered.</summary>
    [Parameter]
    public EventCallback OnRegistered { get; set; }

    /// <summary>Where registration stands.</summary>
    public BpmnWebMcpState Status { get; private set; }

    /// <summary>The names the tools were registered under, once they are.</summary>
    public IReadOnlyList<string> RegisteredTools { get; private set; } = [];

    [Inject]
    private IJSRuntime JS { get; set; } = default!;

    [Inject]
    private BpmnWebMcpOptions Options { get; set; } = default!;

    [Inject]
    private BpmnWebMcpRegistry Registry { get; set; } = default!;

    [Inject]
    private ILogger<BpmnWebMcp> Logger { get; set; } = default!;

    /// <inheritdoc />
    protected override bool ShouldRenderOnStateChange(BpmnEditorState previous, BpmnEditorState current) => false;

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender || _disposed) return;

        try
        {
            var module = await JS.InvokeAsync<IJSObjectReference>("import", _disposal.Token, BpmnWebMcpInteropContract.ModulePath).ConfigureAwait(true);
            if (_disposed)
            {
                await InvokeSafelyAsync(() => module.DisposeAsync().AsTask()).ConfigureAwait(true);
                return;
            }

            _module = module;
            if (!await module.InvokeAsync<bool>("isSupported", _disposal.Token).ConfigureAwait(true))
            {
                LogUnsupported(Logger);
                await UnavailableAsync(BpmnWebMcpState.Unsupported).ConfigureAwait(true);
                return;
            }

            await RegisterAsync(module).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is JSException or JSDisconnectedException or OperationCanceledException
            or ObjectDisposedException or InvalidOperationException or ArgumentException)
        {
            // Interop that cannot run (a lost circuit, a missing module) is the same as no WebMCP, and so is a
            // prefix or instance name no tool name can carry: the editor is never taken down for it.
            if (!_disposed)
            {
                LogRegistrationFailed(Logger, exception);
                await UnavailableAsync(BpmnWebMcpState.Failed).ConfigureAwait(true);
            }
        }
    }

    private async Task RegisterAsync(IJSObjectReference module)
    {
        // Disposal waits for this, so a registration the browser has accepted is always withdrawn.
        _registering = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await RegisterCoreAsync(module).ConfigureAwait(true);
        }
        finally
        {
            _registering.TrySetResult();
        }
    }

    private async Task RegisterCoreAsync(IJSObjectReference module)
    {
        var (suffix, renamed) = await Registry.ClaimAsync(InstanceName, _disposal.Token).ConfigureAwait(true);
        _suffix = suffix;
        if (renamed)
        {
            LogRenamed(Logger, _suffix);
        }

        var host = new BpmnSessionToolHost(Session, Options.ToLimits());
        var tools = BpmnWebMcpToolSet.Create(ToolPrefix ?? Options.ToolPrefix, _suffix, Options.DisabledTools, AllowMutations ?? Options.AllowMutations, host.IsBound);
        var guard = new BpmnWebMcpGuard(
            Options.RequireConfirmationForConsequential,
            Options.RequireConfirmationForPersistence,
            Session.RequestConfirmationAsync,
            Text,
            () => Session.PendingConfirmation is not null);

        // The browser calls in from outside any render: every call joins the renderer's context first.
        _bridge = new BpmnWebMcpBridge(host, tools, RunOnRendererAsync, Logger, guard);
        _reference = DotNetObjectReference.Create(_bridge);

        var registration = await module.InvokeAsync<BpmnWebMcpRegistration>("registerAll", _reference, tools.Descriptors).ConfigureAwait(true);
        _registrationId = registration.Id;
        if (_disposed) return;
        RegisteredTools = registration.Registered;

        foreach (var failure in registration.Failures)
        {
            LogRefused(Logger, failure.Name, failure.Message);
        }

        if (registration.Registered.Length == 0)
        {
            await UnavailableAsync(BpmnWebMcpState.Failed).ConfigureAwait(true);
            return;
        }

        Status = BpmnWebMcpState.Registered;
        await OnRegistered.InvokeAsync().ConfigureAwait(true);
    }

    // Component.InvokeAsync has no overload that returns a value.
    private async Task<string> RunOnRendererAsync(Func<Task<string>> work)
    {
        var result = string.Empty;
        await InvokeAsync(async () => result = await work().ConfigureAwait(true)).ConfigureAwait(false);
        return result;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "This browser has no WebMCP, so the editor's tools are not offered.")]
    private static partial void LogUnsupported(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "The editor's WebMCP tools could not be registered.")]
    private static partial void LogRegistrationFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Another BpmnWebMcp is already registered on this page; this one's tools end in '{Suffix}'. Give each one an InstanceName.")]
    private static partial void LogRenamed(ILogger logger, string suffix);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The browser refused the WebMCP tool '{Tool}': {Message}")]
    private static partial void LogRefused(ILogger logger, string tool, string message);

    private async Task UnavailableAsync(BpmnWebMcpState state)
    {
        Status = state;
        await OnUnavailable.InvokeAsync().ConfigureAwait(true);
    }

    private static async Task InvokeSafelyAsync(Func<Task> call)
    {
        try
        {
            await call().ConfigureAwait(false);
        }
        catch (JSDisconnectedException)
        {
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        if (_suffix is not null) Registry.BeginRelease(_suffix);

        await _disposal.CancelAsync().ConfigureAwait(false);
        _bridge?.CancelAll();

        // The browser call to register cannot be cancelled, so let it finish and withdraw what it registered.
        if (_registering is { } registering) await registering.Task.ConfigureAwait(false);

        if (_module is not null)
        {
            if (_registrationId is { } id) await InvokeSafelyAsync(async () => await _module.InvokeVoidAsync("unregisterAll", id).ConfigureAwait(false)).ConfigureAwait(false);
            await InvokeSafelyAsync(() => _module.DisposeAsync().AsTask()).ConfigureAwait(false);
            _module = null;
        }

        _reference?.Dispose();
        _reference = null;
        if (_suffix is not null) Registry.Release(_suffix);

        _disposal.Dispose();
        GC.SuppressFinalize(this);
    }

    private sealed record BpmnWebMcpRegistration(int Id, string[] Registered, BpmnWebMcpRegistrationFailure[] Failures);

    private sealed record BpmnWebMcpRegistrationFailure(string Name, string Message);
}
