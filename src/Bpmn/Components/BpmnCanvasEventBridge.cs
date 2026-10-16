using Hasseware.AspNetCore.Components.Bpmn.Interop;

using Microsoft.JSInterop;

namespace Hasseware.AspNetCore.Components.Bpmn;

/// <summary>
/// The single entry point for every TypeScript-to-.NET call.
/// </summary>
/// <remarks>
/// <para>
/// Not a component. It is created and owned by <see cref="BpmnCanvas"/>, which is also the only
/// type that wraps it in a <see cref="DotNetObjectReference{T}"/> and disposes it.
/// </para>
/// <para>
/// Every method maps 1:1 onto a method of the same name on the TypeScript <c>BpmnEventBridge</c>
/// class, and there is deliberately no generic dispatch method. That keeps both halves of the
/// contract enumerable — a test reads the members here and the methods there and asserts they
/// match, which a string-keyed dispatcher would make impossible (implementation plan section 5).
/// </para>
/// <para>
/// Each method does exactly one thing: re-raise a strongly typed event. Logic belongs in the
/// canvas, not on the interop boundary, so a JavaScript caller can never reach editor behaviour
/// other than through these payloads.
/// </para>
/// </remarks>
public sealed class BpmnCanvasEventBridge : IDisposable
{
    internal BpmnCanvasEventBridge()
    {
    }

    internal event Action<CanvasInitialized>? InitializedReceived;

    internal event Action<ConfigApplied>? ConfigAppliedReceived;

    internal event Action<ConfigRejected>? ConfigRejectedReceived;

    internal event Action<DocumentLoaded>? DocumentLoadedReceived;

    internal event Action<ViewportChanged>? ViewportChangedReceived;

    internal event Action<ShortcutInvoked>? ShortcutInvokedReceived;

    internal event Action<SelectionChanged>? SelectionChangedReceived;

    internal event Action<SelectionAnchorChanged>? SelectionAnchorChangedReceived;

    internal event Action<EditRequest>? EditRequestedReceived;

    internal event Action<EditRejected>? EditRejectedReceived;

    internal event Action<ContextMenuRequested>? ContextMenuRequestedReceived;

    internal event Action<LabelEditRequested>? LabelEditRequestedReceived;

    /// <summary>Raised when a BPMN file is dropped on the canvas.</summary>
    internal event Action<FileDropped>? FileDroppedReceived;

    /// <summary>Invoked once the canvas instance exists and is registered.</summary>
    [JSInvokable]
    public void Initialized(CanvasInitialized payload) => InitializedReceived?.Invoke(payload);

    /// <summary>Invoked after a configuration patch is accepted.</summary>
    [JSInvokable]
    public void ConfigApplied(ConfigApplied payload) => ConfigAppliedReceived?.Invoke(payload);

    /// <summary>Invoked when a configuration snapshot or patch is refused.</summary>
    [JSInvokable]
    public void ConfigRejected(ConfigRejected payload) => ConfigRejectedReceived?.Invoke(payload);

    /// <summary>Invoked after a document has been projected onto the canvas.</summary>
    [JSInvokable]
    public void DocumentLoaded(DocumentLoaded payload) => DocumentLoadedReceived?.Invoke(payload);

    /// <summary>Invoked (coalesced) whenever the canvas transform changes.</summary>
    [JSInvokable]
    public void ViewportChanged(ViewportChanged payload) => ViewportChangedReceived?.Invoke(payload);

    /// <summary>Invoked when a bound key fires a command the canvas does not handle itself.</summary>
    [JSInvokable]
    public void ShortcutInvoked(ShortcutInvoked payload) => ShortcutInvokedReceived?.Invoke(payload);

    /// <summary>Invoked (coalesced) whenever the selection changes.</summary>
    [JSInvokable]
    public void SelectionChanged(SelectionChanged payload) => SelectionChangedReceived?.Invoke(payload);

    /// <summary>Invoked when the selected element moved on screen while the selection stayed the same.</summary>
    [JSInvokable]
    public void SelectionAnchorChanged(SelectionAnchorChanged payload) =>
        SelectionAnchorChangedReceived?.Invoke(payload);

    /// <summary>Invoked when the native context menu was suppressed and the custom one should open.</summary>
    [JSInvokable]
    public void ContextMenuRequested(ContextMenuRequested payload) => ContextMenuRequestedReceived?.Invoke(payload);

    /// <summary>Invoked when a label should be edited in place (section 4.5).</summary>
    [JSInvokable]
    public void LabelEditRequested(LabelEditRequested payload) => LabelEditRequestedReceived?.Invoke(payload);

    /// <summary>Invoked when a BPMN file is dropped on the canvas.</summary>
    [JSInvokable]
    public void FileDropped(FileDropped payload) => FileDroppedReceived?.Invoke(payload);

    /// <summary>Invoked when the canvas asks for an edit to be applied.</summary>
    [JSInvokable]
    public void EditRequested(EditRequest payload) => EditRequestedReceived?.Invoke(payload);

    /// <summary>Invoked when the canvas refused a gesture and rolled it back.</summary>
    [JSInvokable]
    public void EditRejected(EditRejected payload) => EditRejectedReceived?.Invoke(payload);

    /// <summary>
    /// Detaches every handler. Called by <see cref="BpmnCanvas"/> during disposal so a late
    /// call from a torn-down circuit cannot reach a disposed component.
    /// </summary>
    public void Dispose()
    {
        InitializedReceived = null;
        ConfigAppliedReceived = null;
        ConfigRejectedReceived = null;
        DocumentLoadedReceived = null;
        ViewportChangedReceived = null;
        ShortcutInvokedReceived = null;
        SelectionChangedReceived = null;
        SelectionAnchorChangedReceived = null;
        EditRequestedReceived = null;
        EditRejectedReceived = null;
    }
}
