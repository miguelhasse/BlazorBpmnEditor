using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;
using Hasseware.AspNetCore.Components.Bpmn.Serialization;
using Hasseware.AspNetCore.Components.Bpmn.Validation;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Editing;

public sealed class AnnotationConnectorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CreatesAnUndirectedAssociationWithInferredAnnotationOwnership(bool message)
    {
        var result = Connect(Document(message));

        Assert.True(result.IsApplied, string.Join(", ", result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        var association = Assert.Single(result.Document.Associations);
        Assert.Equal("Process_1", association.ContainerId);
        Assert.Equal(BpmnAssociationDirection.None, association.Direction);
        Assert.Equal(new DiPoint(300, 140), Edge(result.Document, "Association_1").Waypoints[^1]);
        Assert.True(BpmnConnectionRules.Check(result.Document, BpmnConnectorKind.Association, "Note", "Flow").IsValid);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReroutingAFlowCarriesItsAnnotationAndUndoRestoresBoth(bool message)
    {
        var initial = Connect(Document(message)).Document;
        var history = new BpmnEditHistory(initial);
        var result = history.Apply(new UpdateWaypointsCommand("Flow", Detour()) { DiagramId = "Diagram_1" });

        Assert.True(result.IsApplied);
        AssertPoint(new DiPoint(300, 240), Edge(result.Document, "Association_1").Waypoints[^1]);
        Assert.Equal(Detour(), Edge(result.Document, "Flow").Waypoints);
        Assert.Same(initial, history.Undo());
        Assert.Same(result.Document, history.Redo());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AttachmentSurvivesCanonicalAndFidelityReload(bool message)
    {
        var document = Connect(Document(message)).Document;
        foreach (var mode in new[] { BpmnWriteMode.Canonical, BpmnWriteMode.Fidelity })
        {
            var exported = BpmnXmlWriter.Write(document, new BpmnWriteOptions { Mode = mode });
            var reloaded = BpmnXmlReader.Read(exported.Xml).Document;
            var association = Assert.Single(reloaded.Associations);
            Assert.Equal("Flow", association.TargetRef);
            Assert.Equal(Edge(document, "Association_1").Waypoints, Edge(reloaded, "Association_1").Waypoints);
            var rerouted = BpmnDocumentEditor.Apply(reloaded, new UpdateWaypointsCommand("Flow", Detour()));
            Assert.True(rerouted.IsApplied);
            AssertPoint(new DiPoint(300, 240), Edge(rerouted.Document, "Association_1").Waypoints[^1]);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MovingTheFlowEndpointsCarriesTheAttachmentAtomically(bool message)
    {
        var initial = Connect(Document(message)).Document;
        var result = BpmnDocumentEditor.Apply(initial, new MoveShapesCommand(
            [
                new ShapeGeometry("A_di", new Bounds(100, 160, 100, 80)),
                new ShapeGeometry("B_di", new Bounds(400, 160, 100, 80)),
            ],
            [new ConnectorRoute("Flow", [new DiPoint(200, 200), new DiPoint(400, 200)])])
        { DiagramId = "Diagram_1" });

        Assert.True(result.IsApplied);
        Assert.Equal(new DiPoint(300, 200), Edge(result.Document, "Association_1").Waypoints[^1]);
        Assert.Equal(new DiPoint(300, 140), Edge(initial, "Association_1").Waypoints[^1]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RemovingAFlowRemovesItsAssociationAndUndoRestoresBoth(bool message)
    {
        var initial = Connect(Document(message)).Document;
        var history = new BpmnEditHistory(initial);
        Assert.True(history.Apply(new DeleteElementsCommand(["Flow"])).IsApplied);
        Assert.Empty(history.Document.Associations);
        Assert.DoesNotContain(history.Document.Diagrams[0].Plane.Edges, edge => edge.BpmnElement is "Flow" or "Association_1");
        Assert.Single(history.Document.TextAnnotations);
        Assert.Same(initial, history.Undo());
        Assert.Empty(history.Redo().Associations);
    }

    [Fact]
    public void ReconnectsBetweenShapeAndFlowWithoutReplacingTheAssociation()
    {
        var document = Connect(Document()).Document;
        var toShape = BpmnDocumentEditor.Apply(document,
            new ReconnectConnectorCommand("Association_1", null, "B", [new DiPoint(300, 300), new DiPoint(400, 140)])
            { DiagramId = "Diagram_1" });
        Assert.True(toShape.IsApplied);
        var toFlow = BpmnDocumentEditor.Apply(toShape.Document,
            new ReconnectConnectorCommand("Association_1", null, "Flow", [new DiPoint(300, 300), new DiPoint(300, 140)])
            { DiagramId = "Diagram_1" });
        Assert.True(toFlow.IsApplied);
        Assert.Equal(Assert.Single(document.Associations), Assert.Single(toFlow.Document.Associations));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SupportsAFlowAtTheSourceAndFollowsItsNewRoute(bool message)
    {
        var initial = Document(message);
        var created = BpmnDocumentEditor.Apply(initial,
            new CreateConnectorCommand("Association_1", BpmnConnectorKind.Association, "Flow", "Note",
                [new DiPoint(300, 140), new DiPoint(300, 300)]) { DiagramId = "Diagram_1" });
        Assert.True(created.IsApplied);
        var rerouted = BpmnDocumentEditor.Apply(created.Document, new UpdateWaypointsCommand("Flow", Detour()));
        AssertPoint(new DiPoint(300, 240), Edge(rerouted.Document, "Association_1").Waypoints[0]);
    }

    [Theory]
    [InlineData(BpmnConnectorKind.SequenceFlow, "SF-001")]
    [InlineData(BpmnConnectorKind.DataAssociation, "DA-003")]
    public void DoesNotAllowOtherConnectorKindsFromAnnotations(BpmnConnectorKind kind, string rule)
    {
        var result = BpmnDocumentEditor.Apply(Document(), new CreateConnectorCommand(
            "Invalid", kind, "Note", "A", [new DiPoint(300, 300), new DiPoint(200, 140)]));
        Assert.False(result.IsApplied);
        Assert.Equal(rule, Assert.Single(result.Rejections).Code);
    }

    [Fact]
    public void ResizingAnEndpointCarriesTheFlowAttachment()
    {
        var document = Connect(Document()).Document;
        var result = BpmnDocumentEditor.Apply(document,
            new ResizeShapeCommand("A_di", new Bounds(100, 100, 140, 80),
                [new ConnectorRoute("Flow", [new DiPoint(240, 140), new DiPoint(400, 140)])])
            { DiagramId = "Diagram_1" });
        Assert.True(result.IsApplied);
        Assert.Equal(new DiPoint(320, 140), Edge(result.Document, "Association_1").Waypoints[^1]);
    }

    [Fact]
    public void ReconnectingAFlowCarriesItsAnnotation()
    {
        var document = new BpmnDocumentBuilder()
            .Node("A", BpmnFlowNodeType.Task, new Bounds(100, 100, 100, 80))
            .Node("B", BpmnFlowNodeType.Task, new Bounds(400, 100, 100, 80))
            .Node("C", BpmnFlowNodeType.Task, new Bounds(400, 220, 100, 80))
            .TextAnnotation("Note", "Explanation", new Bounds(280, 300, 100, 50))
            .Flow("Flow", "A", "B", [new DiPoint(200, 140), new DiPoint(400, 140)])
            .Build();
        document = Connect(document).Document;
        var result = BpmnDocumentEditor.Apply(document,
            new ReconnectConnectorCommand("Flow", null, "C",
                [new DiPoint(200, 140), new DiPoint(300, 140), new DiPoint(300, 260), new DiPoint(400, 260)])
            { DiagramId = "Diagram_1" });
        Assert.True(result.IsApplied);
        AssertPoint(new DiPoint(300, 200), Edge(result.Document, "Association_1").Waypoints[^1]);
    }

    [Fact]
    public void RejectsAssociationChainsAtCreationAndReconnect()
    {
        var document = Connect(Document()).Document;
        var create = BpmnDocumentEditor.Apply(document, new CreateConnectorCommand(
            "Invalid", BpmnConnectorKind.Association, "Note", "Association_1",
            [new DiPoint(300, 300), new DiPoint(300, 200)]));
        Assert.False(create.IsApplied);
        Assert.Equal("A-002", Assert.Single(create.Rejections).Code);
        var reconnect = BpmnDocumentEditor.Apply(document, new ReconnectConnectorCommand(
            "Association_1", null, "Association_1", [new DiPoint(300, 300), new DiPoint(300, 200)]));
        Assert.False(reconnect.IsApplied);
    }

    [Fact]
    public void RequiresTheFlowToBeDrawnOnTheActiveDiagram()
    {
        var document = Document();
        var diagram = document.Diagrams[0];
        document = document with
        {
            Diagrams = [diagram, diagram with { Id = "Other", Plane = diagram.Plane with { Id = "OtherPlane", Edges = [] } }],
        };
        var result = Connect(document, "Other");
        Assert.False(result.IsApplied);
        Assert.Equal("EDIT-031", Assert.Single(result.Rejections).Code);
    }

    private static BpmnDocument Document(bool message = false)
    {
        var builder = new BpmnDocumentBuilder()
            .Node("A", BpmnFlowNodeType.Task, new Bounds(100, 100, 100, 80))
            .Node("B", BpmnFlowNodeType.Task, new Bounds(400, 100, 100, 80), containerId: message ? "Process_2" : "Process_1")
            .TextAnnotation("Note", "Explanation", new Bounds(280, 300, 100, 50));
        if (message)
        {
            builder.Process("Process_2")
                .Pool("Pool_1", "Sender", new Bounds(60, 60, 180, 200))
                .Pool("Pool_2", "Recipient", new Bounds(360, 60, 180, 200), processRef: "Process_2");
        }
        return (message
            ? builder.MessageFlow("Flow", "A", "B", [new DiPoint(200, 140), new DiPoint(400, 140)])
            : builder.Flow("Flow", "A", "B", [new DiPoint(200, 140), new DiPoint(400, 140)])).Build();
    }

    private static BpmnEditResult Connect(BpmnDocument document, string diagramId = "Diagram_1") =>
        BpmnDocumentEditor.Apply(document, new CreateConnectorCommand(
            "Association_1", BpmnConnectorKind.Association, "Note", "Flow",
            [new DiPoint(300, 300), new DiPoint(300, 147)]) { DiagramId = diagramId });

    private static BpmnEdge Edge(BpmnDocument document, string id) =>
        document.Diagrams[0].Plane.Edges.Single(edge => edge.BpmnElement == id);

    private static void AssertPoint(DiPoint expected, DiPoint actual)
    {
        Assert.Equal(expected.X, actual.X, 8);
        Assert.Equal(expected.Y, actual.Y, 8);
    }

    private static IReadOnlyList<DiPoint> Detour() =>
        [new DiPoint(200, 140), new DiPoint(250, 140), new DiPoint(250, 240),
         new DiPoint(350, 240), new DiPoint(350, 140), new DiPoint(400, 140)];
}
