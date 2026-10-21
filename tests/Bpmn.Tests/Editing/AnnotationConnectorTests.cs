using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;
using Hasseware.AspNetCore.Components.Bpmn.Model.Geometry;
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
        var bounds = result.Document.Diagrams[0].Plane.Shapes.Single(shape => shape.BpmnElement == "Note").Bounds;
        AssertPoint(new DiPoint(280, 400), new DiPoint(bounds.X, bounds.Y));
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
        Assert.Empty(history.Document.TextAnnotations);
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
            new CreateConnectorCommand("Association_1", BpmnConnectorKind.Association, "Flow", "Note", [new DiPoint(300, 140), new DiPoint(300, 300)])
            { DiagramId = "Diagram_1" });
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SecondAnnotationBindingIsRejectedInEitherOrientation(bool reversed)
    {
        var document = Connect(Document()).Document;
        var result = BpmnDocumentEditor.Apply(document, new CreateConnectorCommand(
            "Extra", BpmnConnectorKind.Association, reversed ? "A" : "Note", reversed ? "Note" : "A",
            [new DiPoint(200, 140), new DiPoint(280, 300)]));

        Assert.False(result.IsApplied);
        Assert.Same(document, result.Document);
        Assert.Equal("AN-001", Assert.Single(result.Rejections).Code);
    }

    [Fact]
    public void AttachedNoteFollowsShapeOnceAndResizeLeavesNoteInPlace()
    {
        var document = ShapeConnection();
        var result = BpmnDocumentEditor.Apply(document, new MoveShapesCommand(
            [new ShapeGeometry("A_di", new Bounds(120, 120, 100, 80))]));
        Assert.True(result.IsApplied);
        var note = result.Document.Diagrams[0].Plane.Shapes.Single(shape => shape.BpmnElement == "Note");
        Assert.Equal(new Bounds(300, 320, 100, 50), note.Bounds);
        Assert.Equal(2, Edge(result.Document, "Association_1").Waypoints.Count);

        var resized = BpmnDocumentEditor.Apply(result.Document, new ResizeShapeCommand("A_di", new Bounds(120, 120, 140, 80)));
        Assert.True(resized.IsApplied);
        Assert.Equal(note.Bounds, resized.Document.Diagrams[0].Plane.Shapes.Single(shape => shape.BpmnElement == "Note").Bounds);
    }

    [Fact]
    public void ExplicitAnnotationPositionWinsOverTargetMotion()
    {
        var result = BpmnDocumentEditor.Apply(ShapeConnection(), new MoveShapesCommand(
        [
            new ShapeGeometry("A_di", new Bounds(120, 120, 100, 80)),
            new ShapeGeometry("Note_di", new Bounds(300, 330, 100, 50)),
        ]));
        Assert.True(result.IsApplied);
        Assert.Equal(new Bounds(300, 330, 100, 50), result.Document.Diagrams[0].Plane.Shapes.Single(shape => shape.BpmnElement == "Note").Bounds);
    }

    [Fact]
    public void DeletingAssociationDetachesNoteWithoutDeletingIt()
    {
        var result = BpmnDocumentEditor.Apply(ShapeConnection(), new DeleteElementsCommand(["Association_1"]));
        Assert.True(result.IsApplied);
        Assert.Single(result.Document.TextAnnotations);
        Assert.Empty(result.Document.Associations);
    }

    [Theory]
    [InlineData("A")]
    [InlineData("B")]
    public void IndirectFlowDeletionRemovesDependentNote(string id)
    {
        var result = BpmnDocumentEditor.Apply(Connect(Document()).Document, new DeleteElementsCommand([id]));
        Assert.True(result.IsApplied);
        Assert.Empty(result.Document.TextAnnotations);
        Assert.Empty(result.Document.Associations);
        Assert.Empty(result.Document.SequenceFlows);
        Assert.NotNull(result.DeletedElementIds);
        Assert.Contains("Note", result.DeletedElementIds);
    }

    [Fact]
    public void AnnotationBendUpdatesAreExplicitlyRejected()
    {
        var document = ShapeConnection();
        var result = BpmnDocumentEditor.Apply(document, new UpdateWaypointsCommand("Association_1", Detour()));
        Assert.False(result.IsApplied);
        Assert.Same(document, result.Document);
        Assert.Equal("AN-002", Assert.Single(result.Rejections).Code);
    }

    [Fact]
    public void CreatedAnnotationRouteContainsOnlyEndpoints()
    {
        var result = BpmnDocumentEditor.Apply(Document(), new CreateConnectorCommand(
            "Association_1", BpmnConnectorKind.Association, "Note", "A", Detour()));
        Assert.True(result.IsApplied);
        Assert.Equal(2, Edge(result.Document, "Association_1").Waypoints.Count);
    }

    [Fact]
    public void AmbiguousHostAnnotationHasWarningsAndNoLifecycleOwner()
    {
        var document = ShapeConnection();
        document = document with
        {
            Associations = [.. document.Associations, new BpmnAssociation("Extra", "B", "Note", "Process_1")],
        };
        Assert.Contains(BpmnValidator.Validate(document).Diagnostics, diagnostic => diagnostic.RuleId == "AN-001");
        var moved = BpmnDocumentEditor.Apply(document, new MoveShapesCommand(
            [new ShapeGeometry("A_di", new Bounds(120, 120, 100, 80))]));
        Assert.True(moved.IsApplied);
        Assert.Equal(new Bounds(280, 300, 100, 50), moved.Document.Diagrams[0].Plane.Shapes.Single(shape => shape.BpmnElement == "Note").Bounds);
        var deleted = BpmnDocumentEditor.Apply(document, new DeleteElementsCommand(["A"]));
        Assert.True(deleted.IsApplied);
        Assert.Single(deleted.Document.TextAnnotations);
    }

    [Fact]
    public void IndependentNoteMoveChangesOffsetAndLaterTargetMovePreservesIt()
    {
        var document = ShapeConnection();
        var noteMove = BpmnDocumentEditor.Apply(document, new MoveShapesCommand(
            [new ShapeGeometry("Note_di", new Bounds(300, 380, 100, 50))]));
        Assert.True(noteMove.IsApplied);
        var targetMove = BpmnDocumentEditor.Apply(noteMove.Document, new MoveShapesCommand(
            [new ShapeGeometry("A_di", new Bounds(120, 120, 100, 80))]));
        Assert.True(targetMove.IsApplied);
        Assert.Equal(new Bounds(320, 400, 100, 50), targetMove.Document.Diagrams[0].Plane.Shapes.Single(shape => shape.BpmnElement == "Note").Bounds);
    }

    [Fact]
    public void ExplicitUnchangedNotePositionWinsWhenShapeMoves()
    {
        var result = BpmnDocumentEditor.Apply(ShapeConnection(), new MoveShapesCommand(
        [
            new ShapeGeometry("A_di", new Bounds(120, 120, 100, 80)),
            new ShapeGeometry("Note_di", new Bounds(280, 300, 100, 50)),
        ]));
        Assert.True(result.IsApplied);
        Assert.Equal(new Bounds(280, 300, 100, 50), result.Document.Diagrams[0].Plane.Shapes.Single(shape => shape.BpmnElement == "Note").Bounds);
    }

    [Fact]
    public void ExplicitUnchangedNotePositionWinsWhenFlowMoves()
    {
        var result = BpmnDocumentEditor.Apply(Connect(Document()).Document, new MoveShapesCommand(
        [
            new ShapeGeometry("A_di", new Bounds(100, 160, 100, 80)),
            new ShapeGeometry("B_di", new Bounds(400, 160, 100, 80)),
            new ShapeGeometry("Note_di", new Bounds(280, 300, 100, 50)),
        ],
        [new ConnectorRoute("Flow", [new DiPoint(200, 200), new DiPoint(400, 200)])]));
        Assert.True(result.IsApplied);
        Assert.Equal(new Bounds(280, 300, 100, 50), result.Document.Diagrams[0].Plane.Shapes.Single(shape => shape.BpmnElement == "Note").Bounds);
    }

    [Fact]
    public void FlowRerouteMovesOnlyAnnotationOnEditedDiagram()
    {
        var document = Connect(Document()).Document;
        var other = document.Diagrams[0] with { Id = "Other" };
        document = document with { Diagrams = [document.Diagrams[0], other] };
        var result = BpmnDocumentEditor.Apply(document,
            new UpdateWaypointsCommand("Flow", Detour()) { DiagramId = "Diagram_1" });
        Assert.True(result.IsApplied);
        Assert.Equal(new Bounds(280, 300, 100, 50), result.Document.Diagrams[1].Plane.Shapes.Single(shape => shape.BpmnElement == "Note").Bounds);
        Assert.Equal(other.Plane.Edges, result.Document.Diagrams[1].Plane.Edges);
    }

    [Fact]
    public void ShapeTranslationCarriesExternalAnnotationLabel()
    {
        var document = ShapeConnection();
        var diagram = document.Diagrams[0];
        document = document with
        {
            Diagrams = [diagram with
            {
                Plane = diagram.Plane with
                {
                    Shapes = [.. diagram.Plane.Shapes.Select(shape => shape.BpmnElement == "Note"
                        ? shape with { Label = new BpmnLabel(Bounds: new Bounds(280, 360, 100, 20)) } : shape)],
                },
            }],
        };
        var result = BpmnDocumentEditor.Apply(document, new MoveShapesCommand(
            [new ShapeGeometry("A_di", new Bounds(120, 120, 100, 80))]));
        Assert.True(result.IsApplied);
        Assert.Equal(new Bounds(300, 380, 100, 20), result.Document.Diagrams[0].Plane.Shapes.Single(shape => shape.BpmnElement == "Note").Label!.Bounds);
    }

    [Fact]
    public void AnnotationFreeFollowingReturnsAfterDocumentWithoutAllocating()
    {
        var document = Document() with { TextAnnotations = [] };
        for (var index = 0; index < 100; index++) ConnectorEndpoints.FollowAnnotationShapes(document, document);

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var followed = document;
        for (var index = 0; index < 100; index++) followed = ConnectorEndpoints.FollowAnnotationShapes(document, document);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Assert.Same(document, followed);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void UnchangedAnnotationFollowingPreservesDocumentIdentity()
    {
        var document = ShapeConnection();

        Assert.Same(document, ConnectorEndpoints.FollowAnnotationShapes(document, document));
    }

    [Fact]
    public void EdgeOnlyFollowingPreservesShapesAndUntouchedDiagram()
    {
        var document = ShapeConnection();
        var diagram = document.Diagrams[0];
        var other = diagram with { Id = "Other" };
        document = document with { Diagrams = [diagram, other] };
        var after = document with
        {
            Diagrams = [diagram with
            {
                Plane = diagram.Plane with
                {
                    Shapes = [.. diagram.Plane.Shapes.Select(shape => shape.BpmnElement == "A"
                        ? shape with { Bounds = shape.Bounds with { X = 120 } } : shape)],
                },
            }, other],
        };

        var followed = ConnectorEndpoints.FollowAnnotationShapes(document, after, new HashSet<string>(StringComparer.Ordinal) { "Note_di" });

        Assert.NotSame(after, followed);
        Assert.Same(after.Diagrams[0].Plane.Shapes, followed.Diagrams[0].Plane.Shapes);
        Assert.NotSame(after.Diagrams[0].Plane.Edges, followed.Diagrams[0].Plane.Edges);
        Assert.Same(other, followed.Diagrams[1]);
        Assert.Equal(new DiPoint(220, 140), Edge(followed, "Association_1").Waypoints[^1]);
    }

    [Fact]
    public void LabelOnlyFollowingPreservesEdgesAndUnchangedShapes()
    {
        var document = ShapeConnection();
        var diagram = document.Diagrams[0];
        document = document with
        {
            Diagrams = [diagram with
            {
                Plane = diagram.Plane with
                {
                    Shapes = [.. diagram.Plane.Shapes.Select(shape => shape.BpmnElement == "Note"
                        ? shape with { Label = new BpmnLabel(Bounds: new Bounds(280, 360, 100, 20)) } : shape)],
                },
            }],
        };
        diagram = document.Diagrams[0];
        var after = document with
        {
            Diagrams = [diagram with
            {
                Plane = diagram.Plane with
                {
                    Shapes = [.. diagram.Plane.Shapes.Select(shape => shape.BpmnElement == "Note"
                        ? shape with { Bounds = shape.Bounds with { X = 300 } } : shape)],
                    Edges = [.. diagram.Plane.Edges.Select(edge => edge.BpmnElement == "Association_1"
                        ? edge with { Waypoints = [edge.Waypoints[0] with { X = edge.Waypoints[0].X + 20 }, edge.Waypoints[^1]] }
                        : edge)],
                },
            }],
        };

        var followed = ConnectorEndpoints.FollowAnnotationShapes(document, after);

        Assert.NotSame(after.Diagrams[0].Plane.Shapes, followed.Diagrams[0].Plane.Shapes);
        Assert.Same(after.Diagrams[0].Plane.Edges, followed.Diagrams[0].Plane.Edges);
        Assert.Same(after.Diagrams[0].Plane.Shapes[0], followed.Diagrams[0].Plane.Shapes[0]);
        Assert.Equal(new Bounds(300, 360, 100, 20),
            followed.Diagrams[0].Plane.Shapes.Single(shape => shape.BpmnElement == "Note").Label!.Bounds);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void BentFlowReroutePreservesAnnotationOffsetInEitherOrientation(bool message, bool reversed)
    {
        var bent = BpmnDocumentEditor.Apply(Document(message), new UpdateWaypointsCommand("Flow", Detour())).Document;
        var created = BpmnDocumentEditor.Apply(bent, new CreateConnectorCommand(
            "Association_1", BpmnConnectorKind.Association, reversed ? "Flow" : "Note", reversed ? "Note" : "Flow",
            reversed ? [new DiPoint(300, 240), new DiPoint(300, 300)] : [new DiPoint(300, 300), new DiPoint(300, 240)]));
        Assert.True(created.IsApplied);
        var history = new BpmnEditHistory(created.Document);
        var route = Detour().Select(point => point.Y == 240 ? point with { Y = 340 } : point).ToArray();

        var result = history.Apply(new UpdateWaypointsCommand("Flow", route));

        Assert.True(result.IsApplied);
        var associationRoute = Edge(result.Document, "Association_1").Waypoints;
        Assert.Equal(2, associationRoute.Count);
        AssertPoint(new DiPoint(300, 340), associationRoute[reversed ? 0 : 1]);
        var note = result.Document.Diagrams[0].Plane.Shapes.Single(shape => shape.BpmnElement == "Note");
        AssertPoint(new DiPoint(280, 400), new DiPoint(note.Bounds.X, note.Bounds.Y));
        Assert.Same(created.Document, history.Undo());
        Assert.Same(result.Document, history.Redo());
    }

    private static BpmnDocument ShapeConnection() => BpmnDocumentEditor.Apply(Document(),
        new CreateConnectorCommand("Association_1", BpmnConnectorKind.Association, "Note", "A",
            [new DiPoint(280, 300), new DiPoint(200, 140)])).Document;

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
            [new DiPoint(300, 300), new DiPoint(300, 147)])
        { DiagramId = diagramId });

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
