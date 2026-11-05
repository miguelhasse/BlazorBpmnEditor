using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
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
/// Every JS-invokable identifier maps 1:1 onto a method on the TypeScript <c>BpmnEventBridge</c>
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
    public void Initialized(CanvasInitialized payload) => InitializedReceived?.Invoke(payload);

    /// <summary>Invoked after a configuration patch is accepted.</summary>
    public void ConfigApplied(ConfigApplied payload) => ConfigAppliedReceived?.Invoke(payload);

    /// <summary>Invoked when a configuration snapshot or patch is refused.</summary>
    public void ConfigRejected(ConfigRejected payload) => ConfigRejectedReceived?.Invoke(payload);

    /// <summary>Invoked after a document has been projected onto the canvas.</summary>
    public void DocumentLoaded(DocumentLoaded payload) => DocumentLoadedReceived?.Invoke(payload);

    /// <summary>Invoked (coalesced) whenever the canvas transform changes.</summary>
    public void ViewportChanged(ViewportChanged payload) => ViewportChangedReceived?.Invoke(payload);

    /// <summary>Invoked when a bound key fires a command the canvas does not handle itself.</summary>
    public void ShortcutInvoked(ShortcutInvoked payload) => ShortcutInvokedReceived?.Invoke(payload);

    /// <summary>Invoked (coalesced) whenever the selection changes.</summary>
    public void SelectionChanged(SelectionChanged payload) => SelectionChangedReceived?.Invoke(payload);

    /// <summary>Invoked when the selected element moved on screen while the selection stayed the same.</summary>
    public void SelectionAnchorChanged(SelectionAnchorChanged payload) =>
        SelectionAnchorChangedReceived?.Invoke(payload);

    /// <summary>Invoked when the native context menu was suppressed and the custom one should open.</summary>
    public void ContextMenuRequested(ContextMenuRequested payload) => ContextMenuRequestedReceived?.Invoke(payload);

    /// <summary>Invoked when a label should be edited in place (section 4.5).</summary>
    public void LabelEditRequested(LabelEditRequested payload) => LabelEditRequestedReceived?.Invoke(payload);

    /// <summary>Invoked when a BPMN file is dropped on the canvas.</summary>
    public void FileDropped(FileDropped payload) => FileDroppedReceived?.Invoke(payload);

    /// <summary>Invoked when the canvas asks for an edit to be applied.</summary>
    public void EditRequested(EditRequest payload) => EditRequestedReceived?.Invoke(payload);

    /// <summary>Invoked when the canvas refused a gesture and rolled it back.</summary>
    public void EditRejected(EditRejected payload) => EditRejectedReceived?.Invoke(payload);

    /// <inheritdoc cref="Initialized"/>
    [JSInvokable(nameof(Initialized))]
    public void ReceiveInitialized(JsonElement payload) => Initialized(Read(payload, BpmnInteropJsonContext.Default.CanvasInitialized));

    /// <inheritdoc cref="ConfigApplied"/>
    [JSInvokable(nameof(ConfigApplied))]
    public void ReceiveConfigApplied(JsonElement payload) => ConfigApplied(Read(payload, BpmnInteropJsonContext.Default.ConfigApplied));

    /// <inheritdoc cref="ConfigRejected"/>
    [JSInvokable(nameof(ConfigRejected))]
    public void ReceiveConfigRejected(JsonElement payload) => ConfigRejected(Read(payload, BpmnInteropJsonContext.Default.ConfigRejected));

    /// <inheritdoc cref="DocumentLoaded"/>
    [JSInvokable(nameof(DocumentLoaded))]
    public void ReceiveDocumentLoaded(JsonElement payload) => DocumentLoaded(Read(payload, BpmnInteropJsonContext.Default.DocumentLoaded));

    /// <inheritdoc cref="ViewportChanged"/>
    [JSInvokable(nameof(ViewportChanged))]
    public void ReceiveViewportChanged(JsonElement payload) => ViewportChanged(Read(payload, BpmnInteropJsonContext.Default.ViewportChanged));

    /// <inheritdoc cref="ShortcutInvoked"/>
    [JSInvokable(nameof(ShortcutInvoked))]
    public void ReceiveShortcutInvoked(JsonElement payload) => ShortcutInvoked(Read(payload, BpmnInteropJsonContext.Default.ShortcutInvoked));

    /// <inheritdoc cref="SelectionChanged"/>
    [JSInvokable(nameof(SelectionChanged))]
    public void ReceiveSelectionChanged(JsonElement payload) => SelectionChanged(Read(payload, BpmnInteropJsonContext.Default.SelectionChanged));

    /// <inheritdoc cref="SelectionAnchorChanged"/>
    [JSInvokable(nameof(SelectionAnchorChanged))]
    public void ReceiveSelectionAnchorChanged(JsonElement payload) => SelectionAnchorChanged(Read(payload, BpmnInteropJsonContext.Default.SelectionAnchorChanged));

    /// <inheritdoc cref="ContextMenuRequested"/>
    [JSInvokable(nameof(ContextMenuRequested))]
    public void ReceiveContextMenuRequested(JsonElement payload) => ContextMenuRequested(Read(payload, BpmnInteropJsonContext.Default.ContextMenuRequested));

    /// <inheritdoc cref="LabelEditRequested"/>
    [JSInvokable(nameof(LabelEditRequested))]
    public void ReceiveLabelEditRequested(JsonElement payload) => LabelEditRequested(Read(payload, BpmnInteropJsonContext.Default.LabelEditRequested));

    /// <inheritdoc cref="FileDropped"/>
    [JSInvokable(nameof(FileDropped))]
    public void ReceiveFileDropped(JsonElement payload) => FileDropped(Read(payload, BpmnInteropJsonContext.Default.FileDropped));

    /// <inheritdoc cref="EditRequested"/>
    [JSInvokable(nameof(EditRequested))]
    public void ReceiveEditRequested(JsonElement payload) => EditRequested(Read(payload, BpmnInteropJsonContext.Default.EditRequest));

    /// <inheritdoc cref="EditRejected"/>
    [JSInvokable(nameof(EditRejected))]
    public void ReceiveEditRejected(JsonElement payload) => EditRejected(Read(payload, BpmnInteropJsonContext.Default.EditRejected));

    private static T Read<T>(JsonElement payload, JsonTypeInfo<T> typeInfo) where T : class =>
        payload.Deserialize(typeInfo) ?? throw new JsonException($"The canvas payload for '{typeof(T).Name}' must not be null.");

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
