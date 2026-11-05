using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

using Hasseware.AspNetCore.Components.Bpmn.Interop;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Components;

public sealed class BpmnCanvasEventBridgeTests
{
    [Fact]
    public void GeneratedJsonAdaptersForwardEveryTypedCallback()
    {
        using var bridge = new BpmnCanvasEventBridge();
        var metadata = BpmnInteropJsonContext.Default;
        var anchor = new Anchor(new Bounds(10, 20, 100, 80), new Bounds(30, 40, 100, 80));

        Check(new CanvasInitialized("canvas", 1), metadata.CanvasInitialized,
            handler => bridge.InitializedReceived += handler, bridge.ReceiveInitialized);
        Check(new ConfigApplied(2, true), metadata.ConfigApplied,
            handler => bridge.ConfigAppliedReceived += handler, bridge.ReceiveConfigApplied);
        Check(new ConfigRejected(2, "VERSION", "version", "Unsupported version"), metadata.ConfigRejected,
            handler => bridge.ConfigRejectedReceived += handler, bridge.ReceiveConfigRejected);
        Check(new DocumentLoaded("diagram", 3, 2, ["missing"]), metadata.DocumentLoaded,
            handler => bridge.DocumentLoadedReceived += handler, bridge.ReceiveDocumentLoaded);
        Check(new ViewportChanged(1.5, 10, 20), metadata.ViewportChanged,
            handler => bridge.ViewportChangedReceived += handler, bridge.ReceiveViewportChanged);
        Check(new ShortcutInvoked("undo"), metadata.ShortcutInvoked,
            handler => bridge.ShortcutInvokedReceived += handler, bridge.ReceiveShortcutInvoked);
        Check(new SelectionChanged(["task"], anchor), metadata.SelectionChanged,
            handler => bridge.SelectionChangedReceived += handler, bridge.ReceiveSelectionChanged);
        Check(new SelectionAnchorChanged("task", anchor), metadata.SelectionAnchorChanged,
            handler => bridge.SelectionAnchorChangedReceived += handler, bridge.ReceiveSelectionAnchorChanged);
        Check(new ContextMenuRequested("element", "task", anchor, "pointer", ["task"]), metadata.ContextMenuRequested,
            handler => bridge.ContextMenuRequestedReceived += handler, bridge.ReceiveContextMenuRequested);
        Check(new LabelEditRequested("task", anchor, "Review", "Arial", 12, 1.5, "left", 1), metadata.LabelEditRequested,
            handler => bridge.LabelEditRequestedReceived += handler, bridge.ReceiveLabelEditRequested);
        Check(new FileDropped("test.bpmn", "<definitions/>"), metadata.FileDropped,
            handler => bridge.FileDroppedReceived += handler, bridge.ReceiveFileDropped);
        Check(new EditRequest("move", [new ShapeGeometryPayload("shape", anchor.Bounds)])
        {
            DiagramId = "diagram",
        }, metadata.EditRequest, handler => bridge.EditRequestedReceived += handler, bridge.ReceiveEditRequested);
        Check(new EditRejected("DI-011", "Invalid move", "task"), metadata.EditRejected,
            handler => bridge.EditRejectedReceived += handler, bridge.ReceiveEditRejected);
    }

    [Fact]
    public void ReadsNestedEditRequestsFromBrowserJson()
    {
        using var bridge = new BpmnCanvasEventBridge();
        EditRequest? received = null;
        bridge.EditRequestedReceived += payload => received = payload;

        bridge.ReceiveEditRequested(JsonElement.Parse("""
            {"kind":"move","diagramId":"diagram",
             "moves":[{"shapeId":"shape","bounds":{"x":10,"y":20,"width":100,"height":80}}],
             "routes":[{"elementId":"flow","waypoints":[{"x":110,"y":60},{"x":200,"y":60}]}]}
            """));

        Assert.NotNull(received);
        Assert.Equal("diagram", received.DiagramId);
        Assert.Equal(new Bounds(10, 20, 100, 80), Assert.Single(received.Moves!).Bounds);
        Assert.Equal(new DiPoint(200, 60), Assert.Single(received.Routes!).Waypoints[1]);
    }

    [Fact]
    public void RejectsNullPayloadsInsteadOfRaisingNullEvents()
    {
        using var bridge = new BpmnCanvasEventBridge();
        Action<JsonElement>[] receivers =
        [
            bridge.ReceiveInitialized, bridge.ReceiveConfigApplied, bridge.ReceiveConfigRejected,
            bridge.ReceiveDocumentLoaded, bridge.ReceiveViewportChanged, bridge.ReceiveShortcutInvoked,
            bridge.ReceiveSelectionChanged, bridge.ReceiveSelectionAnchorChanged, bridge.ReceiveContextMenuRequested,
            bridge.ReceiveLabelEditRequested, bridge.ReceiveFileDropped, bridge.ReceiveEditRequested, bridge.ReceiveEditRejected,
        ];

        foreach (var receive in receivers)
        {
            Assert.Throws<JsonException>(() => receive(JsonElement.Parse("null")));
        }
    }

    private static void Check<T>(T expected, JsonTypeInfo<T> metadata, Action<Action<T>> subscribe, Action<JsonElement> receive)
        where T : class
    {
        T? received = null;
        subscribe(payload => received = payload);

        receive(JsonSerializer.SerializeToElement(expected, metadata));

        Assert.Equivalent(expected, received, strict: true);
    }
}
