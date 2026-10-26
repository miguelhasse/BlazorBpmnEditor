using Hasseware.AspNetCore.Components.Bpmn.Interop;
using Hasseware.AspNetCore.Components.Bpmn.Resources;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Microsoft.JSInterop;

namespace Hasseware.AspNetCore.Components.Bpmn;

/// <summary>A native modal with a single header Close action.</summary>
public abstract class BpmnDialogBase : ComponentBase, IAsyncDisposable
{
    private IJSObjectReference? _module;
    private DotNetObjectReference<BpmnDialogBase>? _reference;
    private bool _shown;
    private bool _transitioning;
    private bool _disposed;

    [Inject] private IJSRuntime JS { get; set; } = default!;
    [Inject] private IStringLocalizer<BpmnStrings> Localizer { get; set; } = default!;

    /// <summary>Whether the owning editor needs the modal.</summary>
    [Parameter] public bool Visible { get; set; } = true;
    /// <summary>Heading content.</summary>
    [Parameter] public RenderFragment? TitleTemplate { get; set; }
    /// <summary>Dialog body.</summary>
    [Parameter] public RenderFragment? ChildContent { get; set; }
    /// <summary>Footer actions, excluding Close.</summary>
    [Parameter] public RenderFragment? ActionTemplate { get; set; }
    /// <summary>Localized header Close label.</summary>
    [Parameter] public string? CloseLabel { get; set; }
    /// <summary>Additional CSS classes.</summary>
    [Parameter] public string? Class { get; set; }
    /// <summary>Whether clicking the backdrop dismisses this dialog.</summary>
    [Parameter] public bool DismissOnBackdrop { get; set; }
    /// <summary>Requests the owner's non-accepting dismissal action.</summary>
    [Parameter] public EventCallback OnDismiss { get; set; }
    /// <summary>Raised after the native modal releases focus.</summary>
    [Parameter] public EventCallback OnClosed { get; set; }
    /// <summary>HTML and accessibility attributes.</summary>
    [Parameter(CaptureUnmatchedValues = true)] public IReadOnlyDictionary<string, object>? AdditionalAttributes { get; set; }

    /// <summary>Native dialog element supplied by the renderer.</summary>
    protected ElementReference DialogElement { get; set; }
    /// <summary>The dialog's content region, used when focused content is replaced.</summary>
    protected ElementReference BodyElement { get; set; }
    /// <summary>Whether the native host must remain mounted.</summary>
    protected bool RenderDialog => Visible || _shown || _transitioning;
    /// <summary>Effective localized Close label.</summary>
    protected string CloseText => CloseLabel ?? Localizer["ShortcutHelp.Close"];

    /// <summary>Restores focus inside a modal after its focused body content is replaced.</summary>
    public ValueTask FocusAsync() => _shown && !_disposed
        ? BodyElement.FocusAsync(preventScroll: true) : ValueTask.CompletedTask;

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_disposed || _transitioning || Visible == _shown) return;

        _transitioning = true;
        _shown = Visible;
        try
        {
            if (_module is null)
            {
                var module = await JS.InvokeAsync<IJSObjectReference>("import", BpmnInteropContract.ModulePath);
                if (_disposed)
                {
                    await module.DisposeAsync();
                    return;
                }
                _module = module;
            }
            if (_disposed) return;
            if (_shown)
            {
                _reference ??= DotNetObjectReference.Create(this);
                await _module.InvokeVoidAsync("showDialog", DialogElement, _reference, DismissOnBackdrop);
            }
            else
            {
                await _module.InvokeVoidAsync("closeDialog", DialogElement);
                await OnClosed.InvokeAsync();
            }
        }
        finally
        {
            _transitioning = false;
        }
        if (!_disposed) StateHasChanged();
    }

    /// <summary>Receives a native Escape or backdrop dismissal after modal closure.</summary>
    [JSInvokable]
    public async Task DismissAsync()
    {
        if (_disposed || !_shown) return;
        _shown = false;
        await OnDismiss.InvokeAsync();
        await OnClosed.InvokeAsync();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        try
        {
            if (_module is not null)
            {
                if (_shown) await _module.InvokeVoidAsync("closeDialog", DialogElement);
                await _module.DisposeAsync();
            }
        }
        catch (JSDisconnectedException)
        {
            // The circuit no longer has a browser to release.
        }
        finally
        {
            _reference?.Dispose();
        }
        GC.SuppressFinalize(this);
    }
}
