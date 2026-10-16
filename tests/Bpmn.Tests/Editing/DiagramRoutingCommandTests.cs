using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;
using Hasseware.AspNetCore.Components.Bpmn.Model.Geometry;
using Hasseware.AspNetCore.Components.Bpmn.Serialization;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Editing;

public sealed class DiagramRoutingCommandTests
{
    [Theory]
    [InlineData("Diagram_1", 0, 1, 0)]
    [InlineData("Diagram_2", 1, 0, 1000)]
    public void CreateConnectorWritesOnlyRequestedViewAndPersistsRoute(
        string diagramId, int selected, int untouched, double offset)
    {
        var document = Views();
        var route = Detour(offset);

        var result = BpmnDocumentEditor.Apply(document, new CreateConnectorCommand(
            "New", BpmnConnectorKind.SequenceFlow, "A", "B", route)
        { DiagramId = diagramId });

        Assert.True(result.IsApplied);
        Assert.Equal("Process_1", Assert.Single(result.Document.SequenceFlows, flow => flow.Id == "New").ContainerId);
        Assert.Same(document.Diagrams[untouched], result.Document.Diagrams[untouched]);
        Assert.Equal(route, Edge(result.Document, selected, "New").Waypoints);
        var persisted = BpmnXmlReader.Read(BpmnXmlWriter.Write(result.Document).Xml).Document;
        Assert.Equal(route, Edge(persisted, selected, "New").Waypoints);
        Assert.DoesNotContain(persisted.Diagrams[untouched].Plane.Edges, edge => edge.BpmnElement == "New");
    }

    [Fact]
    public void CreateConnectorOnSubProcessPlaneLeavesParentPlaneUntouched()
    {
        var document = SubProcessViews();
        var route = Detour(0);

        var result = BpmnDocumentEditor.Apply(document, new CreateConnectorCommand(
            "New", BpmnConnectorKind.SequenceFlow, "A", "B", route)
        { DiagramId = "Content" });

        Assert.True(result.IsApplied);
        Assert.Equal("Sub", Assert.Single(result.Document.SequenceFlows).ContainerId);
        Assert.Same(document.Diagrams[0], result.Document.Diagrams[0]);
        Assert.Equal(route, Edge(result.Document, 1, "New").Waypoints);
        var persisted = BpmnXmlReader.Read(BpmnXmlWriter.Write(result.Document).Xml).Document;
        Assert.Equal(route, Edge(persisted, 1, "New").Waypoints);
    }

    [Theory]
    [InlineData("Ghost", "A", "B", "CT-001")]
    [InlineData("Other", "A", "B", "CT-001")]
    [InlineData("Diagram_2", "A", "Missing", "EDIT-031")]
    [InlineData("Diagram_2", "A", "Hidden", "EDIT-031")]
    [InlineData("Diagram_2", "A", "A", "EDIT-032")]
    public void InvalidConnectorDestinationOrEndpointsRejectAtomically(
        string diagramId, string source, string target, string code)
    {
        var document = Views();

        var result = BpmnDocumentEditor.Apply(document, new CreateConnectorCommand(
            "New", BpmnConnectorKind.SequenceFlow, source, target, Detour(1000))
        { DiagramId = diagramId });

        Assert.False(result.IsApplied);
        Assert.Same(document, result.Document);
        Assert.Equal(code, Assert.Single(result.Rejections).Code);
        Assert.Null(result.Document.Find("New"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MoveOrResizeWithRouteChangesOnlyRequestedView(bool resize)
    {
        var document = Views();
        var bounds = new Bounds(400, 100, resize ? 120 : 100, 80);
        IReadOnlyList<DiPoint> route = [new(bounds.X + bounds.Width, 140), new(1400, 140)];
        BpmnEditCommand command = resize
            ? new ResizeShapeCommand("A_di_2", bounds, [new ConnectorRoute("Flow", route)]) { DiagramId = "Diagram_2" }
            : new MoveShapesCommand([new ShapeGeometry("A_di_2", bounds)], [new ConnectorRoute("Flow", route)])
            { DiagramId = "Diagram_2" };

        var result = BpmnDocumentEditor.Apply(document, command);

        Assert.True(result.IsApplied);
        Assert.Same(document.Diagrams[0], result.Document.Diagrams[0]);
        Assert.Equal(bounds, Shape(result.Document, 1, "A").Bounds);
        Assert.Equal(route, Edge(result.Document, 1, "Flow").Waypoints);
        Assert.Same(document.SequenceFlows, result.Document.SequenceFlows);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MoveOrResizeIntoPeerOnRequestedPlaneRejectsCollision(bool resize)
    {
        var document = Views();
        var bounds = new Bounds(1400, 100, 100, 80);
        BpmnEditCommand command = resize
            ? new ResizeShapeCommand("A_di_2", bounds) { DiagramId = "Diagram_2" }
            : new MoveShapesCommand([new ShapeGeometry("A_di_2", bounds)]) { DiagramId = "Diagram_2" };

        var result = BpmnDocumentEditor.Apply(document, command);

        Assert.False(result.IsApplied);
        Assert.Same(document, result.Document);
        Assert.Equal("DI-011", Assert.Single(result.Rejections).Code);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ShapeIdFromAnotherViewRejectsAtomically(bool resize)
    {
        var document = Views();
        BpmnEditCommand command = resize
            ? new ResizeShapeCommand("A_di", new Bounds(1100, 100, 160, 80)) { DiagramId = "Diagram_2" }
            : new MoveShapesCommand([new ShapeGeometry("A_di", new Bounds(1100, 100, 100, 80))])
            { DiagramId = "Diagram_2" };

        var result = BpmnDocumentEditor.Apply(document, command);

        Assert.False(result.IsApplied);
        Assert.Same(document, result.Document);
        Assert.Equal("EDIT-011", Assert.Single(result.Rejections).Code);
    }

    [Fact]
    public void UpdateWaypointsPreservesOtherViewOfSameConnector()
    {
        var document = Views();
        var route = Detour(1000);

        var result = BpmnDocumentEditor.Apply(document, new UpdateWaypointsCommand("Flow", route)
        { DiagramId = "Diagram_2" });

        Assert.True(result.IsApplied);
        Assert.Same(document.Diagrams[0], result.Document.Diagrams[0]);
        Assert.Equal(route, Edge(result.Document, 1, "Flow").Waypoints);
        Assert.Same(document.SequenceFlows, result.Document.SequenceFlows);
    }

    [Fact]
    public void ReconnectChangesGlobalEndpointButDoesNotCopyRouteToOtherView()
    {
        var document = Views();
        var expectedOtherView = ConnectorEndpoints.Normalize(document with
        {
            SequenceFlows = [document.SequenceFlows[0] with { TargetRef = "C" }],
        }).Diagrams[0];
        IReadOnlyList<DiPoint> route =
        [
            new(1200, 140), new(1350, 140), new(1350, 40),
            new(1500, 40), new(1500, 90), new(1500, 180),
        ];

        var result = BpmnDocumentEditor.Apply(document, new ReconnectConnectorCommand("Flow", null, "C", route)
        { DiagramId = "Diagram_2" });

        Assert.True(result.IsApplied);
        Assert.Equal("C", Assert.Single(result.Document.SequenceFlows).TargetRef);
        Assert.Equal(route, Edge(result.Document, 1, "Flow").Waypoints);
        var otherRoute = Edge(result.Document, 0, "Flow").Waypoints;
        Assert.Equal(new DiPoint(200, 140), otherRoute[0]);
        Assert.Equal(Assert.Single(expectedOtherView.Plane.Edges).Waypoints, otherRoute);
        Assert.NotEqual(route, otherRoute);
        Assert.Same(document.Diagrams[0].Plane.Shapes, result.Document.Diagrams[0].Plane.Shapes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConnectorNotDrawnOnRequestedViewRejectsAtomically(bool reconnect)
    {
        var document = Views();
        document = document with
        {
            Diagrams = [document.Diagrams[0], document.Diagrams[1] with
            {
                Plane = document.Diagrams[1].Plane with { Edges = [] },
            }, document.Diagrams[2]],
        };
        BpmnEditCommand command = reconnect
            ? new ReconnectConnectorCommand("Flow", null, "C", Detour(1000)) { DiagramId = "Diagram_2" }
            : new UpdateWaypointsCommand("Flow", Detour(1000)) { DiagramId = "Diagram_2" };

        var result = BpmnDocumentEditor.Apply(document, command);

        Assert.False(result.IsApplied);
        Assert.Same(document, result.Document);
        Assert.Equal("EDIT-064", Assert.Single(result.Rejections).Code);
    }

    [Fact]
    public void ReconnectToEndpointAbsentFromRequestedViewRejectsAtomically()
    {
        var document = Views();

        var result = BpmnDocumentEditor.Apply(document,
            new ReconnectConnectorCommand("Flow", null, "Hidden", Detour(1000)) { DiagramId = "Diagram_2" });

        Assert.False(result.IsApplied);
        Assert.Same(document, result.Document);
        Assert.Equal("EDIT-062", Assert.Single(result.Rejections).Code);
    }

    [Theory]
    [InlineData("move")]
    [InlineData("resize")]
    [InlineData("reconnect")]
    [InlineData("waypoints")]
    public void MissingDiagramRejectsWithoutHistoryEntry(string kind)
    {
        var document = Views();
        var history = new BpmnEditHistory(document);

        var result = history.Apply(Command(kind, "Ghost"));

        Assert.False(result.IsApplied);
        Assert.Same(document, history.Document);
        Assert.Same(document, result.Document);
        Assert.Equal("CT-001", Assert.Single(result.Rejections).Code);
        Assert.False(history.CanUndo);
    }

    [Theory]
    [InlineData("create")]
    [InlineData("move")]
    [InlineData("resize")]
    [InlineData("reconnect")]
    [InlineData("waypoints")]
    public void RequestedPlaneEditUndoAndRedoRestoreExactSnapshots(string kind)
    {
        var document = Views();
        var history = new BpmnEditHistory(document);

        var result = history.Apply(Command(kind, "Diagram_2"));

        Assert.True(result.IsApplied);
        Assert.Same(document, history.Undo());
        Assert.Same(result.Document, history.Redo());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OmittedWaypointDestinationKeepsAllViewBehavior(bool reconnect)
    {
        var document = Views(0);
        var route = Detour(0);
        BpmnEditCommand command = reconnect
            ? new ReconnectConnectorCommand("Flow", null, null, route)
            : new UpdateWaypointsCommand("Flow", route);

        var result = BpmnDocumentEditor.Apply(document, command);

        Assert.True(result.IsApplied);
        Assert.Equal(route, Edge(result.Document, 0, "Flow").Waypoints);
        Assert.Equal(route, Edge(result.Document, 1, "Flow").Waypoints);
    }

    [Fact]
    public void OmittedCreationDestinationKeepsFirstViewBehavior()
    {
        var document = Views();

        var result = BpmnDocumentEditor.Apply(document,
            new CreateConnectorCommand("New", BpmnConnectorKind.SequenceFlow, "A", "B", Detour(0)));

        Assert.True(result.IsApplied);
        Assert.Equal(Detour(0), Edge(result.Document, 0, "New").Waypoints);
        Assert.Same(document.Diagrams[1], result.Document.Diagrams[1]);
    }

    [Fact]
    public void PoolResizeTilesOnlyLanesOnRequestedPlane()
    {
        var document = PoolViews();

        var result = BpmnDocumentEditor.Apply(document,
            new ResizeShapeCommand("Pool_di_2", new Bounds(1000, 0, 800, 600)) { DiagramId = "Diagram_2" });

        Assert.True(result.IsApplied);
        Assert.Same(document.Diagrams[0], result.Document.Diagrams[0]);
        Assert.Equal(new Bounds(1030, 0, 770, 300), Shape(result.Document, 1, "Lane_1").Bounds);
        Assert.Equal(new Bounds(1030, 300, 770, 300), Shape(result.Document, 1, "Lane_2").Bounds);
    }

    [Fact]
    public void PoolShrinkIgnoresContentGeometryOnOtherView()
    {
        var document = PoolViews();

        var result = BpmnDocumentEditor.Apply(document,
            new ResizeShapeCommand("Pool_di_2", new Bounds(1000, 0, 600, 250)) { DiagramId = "Diagram_2" });

        Assert.True(result.IsApplied);
        Assert.Same(document.Diagrams[0], result.Document.Diagrams[0]);
        Assert.Equal(125, Shape(result.Document, 1, "Lane_1").Bounds.Height);
    }

    [Fact]
    public void PoolShrinkPastContentOnRequestedPlaneRejectsContainment()
    {
        var document = PoolViews();

        var result = BpmnDocumentEditor.Apply(document,
            new ResizeShapeCommand("Pool_di_2", new Bounds(1000, 0, 100, 250)) { DiagramId = "Diagram_2" });

        Assert.False(result.IsApplied);
        Assert.Same(document, result.Document);
        Assert.Equal("EDIT-052", Assert.Single(result.Rejections).Code);
    }

    [Fact]
    public void MoveChildOutsidePoolGrowsOnlyRequestedViewAndRetilesItsLanes()
    {
        var document = PoolViews();

        var result = BpmnDocumentEditor.Apply(document,
            new MoveShapesCommand([new ShapeGeometry("A_di_2", new Bounds(1650, 100, 100, 80))])
            { DiagramId = "Diagram_2" });

        Assert.True(result.IsApplied);
        Assert.Same(document.Diagrams[0], result.Document.Diagrams[0]);
        Assert.Equal(770, Shape(result.Document, 1, "Pool").Bounds.Width);
        Assert.Equal(740, Shape(result.Document, 1, "Lane_1").Bounds.Width);
        Assert.Equal(740, Shape(result.Document, 1, "Lane_2").Bounds.Width);
    }

    [Fact]
    public void MovePoolWithLanesResolvesOwningPoolOnRequestedPlane()
    {
        var document = PoolViews();
        var moves = document.Diagrams[1].Plane.Shapes
            .Select(shape => new ShapeGeometry(shape.Id, shape.Bounds with { Y = shape.Bounds.Y + 50 })).ToArray();

        var result = BpmnDocumentEditor.Apply(document, new MoveShapesCommand(moves) { DiagramId = "Diagram_2" });

        Assert.True(result.IsApplied);
        Assert.Same(document.Diagrams[0], result.Document.Diagrams[0]);
        Assert.Equal(50, Shape(result.Document, 1, "Pool").Bounds.Y);
        Assert.Equal(50, Shape(result.Document, 1, "Lane_1").Bounds.Y);
    }

    private static BpmnEditCommand Command(string kind, string? diagramId) => kind switch
    {
        "create" => new CreateConnectorCommand("New", BpmnConnectorKind.SequenceFlow, "A", "B", Detour(1000))
        { DiagramId = diagramId },
        "move" => new MoveShapesCommand([new ShapeGeometry("A_di_2", new Bounds(1050, 100, 100, 80))])
        { DiagramId = diagramId },
        "resize" => new ResizeShapeCommand("A_di_2", new Bounds(1100, 100, 120, 80)) { DiagramId = diagramId },
        "reconnect" => new ReconnectConnectorCommand("Flow", null, "C", Detour(1000)) { DiagramId = diagramId },
        _ => new UpdateWaypointsCommand("Flow", Detour(1000)) { DiagramId = diagramId },
    };

    private static IReadOnlyList<DiPoint> Detour(double offset) =>
    [
        new(200 + offset, 140), new(300 + offset, 140), new(300 + offset, 20),
        new(350 + offset, 20), new(350 + offset, 140), new(400 + offset, 140),
    ];

    private static BpmnShape Shape(BpmnDocument document, int view, string elementId) =>
        Assert.Single(document.Diagrams[view].Plane.Shapes, shape => shape.BpmnElement == elementId);

    private static BpmnEdge Edge(BpmnDocument document, int view, string elementId) =>
        Assert.Single(document.Diagrams[view].Plane.Edges, edge => edge.BpmnElement == elementId);

    private static BpmnDocument Views(double offset = 1000)
    {
        var document = new BpmnDocumentBuilder()
            .Process("Process_2")
            .Node("A", BpmnFlowNodeType.Task, new Bounds(100, 100, 100, 80))
            .Node("B", BpmnFlowNodeType.Task, new Bounds(400, 100, 100, 80))
            .Node("C", BpmnFlowNodeType.Task, new Bounds(460, 180, 100, 80))
            .Node("Hidden", BpmnFlowNodeType.Task, new Bounds(800, 100, 100, 80))
            .Flow("Flow", "A", "B", [new(200, 140), new(400, 140)])
            .Build();
        return document with
        {
            Diagrams =
            [
                document.Diagrams[0],
                new BpmnDiagram("Diagram_2", document.Diagrams[0].Plane with
                {
                    Id = "Plane_2",
                    Shapes = [.. document.Diagrams[0].Plane.Shapes.Where(shape => shape.BpmnElement != "Hidden")
                        .Select(shape => shape with
                        {
                            Id = shape.Id + "_2",
                            Bounds = shape.Bounds with { X = shape.Bounds.X + offset },
                        })],
                    Edges = [new BpmnEdge("Flow_di_2", "Flow", [new(200 + offset, 140), new(400 + offset, 140)])],
                }),
                new BpmnDiagram("Other", new BpmnPlane("OtherPlane", "Process_2", [], [])),
            ],
        };
    }

    private static BpmnDocument PoolViews()
    {
        var document = new BpmnDocumentBuilder()
            .Pool("Pool", null, new Bounds(0, 0, 600, 400))
            .Lane("Lane_1", null, new Bounds(30, 0, 570, 200), ["A"])
            .Lane("Lane_2", null, new Bounds(30, 200, 570, 200), [])
            .Node("A", BpmnFlowNodeType.Task, new Bounds(100, 100, 100, 80))
            .Build();
        return document with
        {
            Diagrams = [document.Diagrams[0], new BpmnDiagram("Diagram_2", document.Diagrams[0].Plane with
            {
                Id = "Plane_2",
                Shapes = [.. document.Diagrams[0].Plane.Shapes.Select(shape => shape with
                {
                    Id = shape.Id + "_2",
                    Bounds = shape.Bounds with { X = shape.Bounds.X + 1000 },
                })],
            })],
        };
    }

    private static BpmnDocument SubProcessViews()
    {
        var document = new BpmnDocumentBuilder()
            .Node("Sub", BpmnFlowNodeType.SubProcess, new Bounds(50, 50, 100, 80), isExpanded: false)
            .Node("A", BpmnFlowNodeType.Task, new Bounds(100, 100, 100, 80), containerId: "Sub")
            .Node("B", BpmnFlowNodeType.Task, new Bounds(400, 100, 100, 80), containerId: "Sub")
            .Build();
        return document with
        {
            Diagrams = [document.Diagrams[0] with
            {
                Plane = document.Diagrams[0].Plane with
                {
                    Shapes = [.. document.Diagrams[0].Plane.Shapes.Where(shape => shape.BpmnElement == "Sub")],
                },
            }, new BpmnDiagram("Content", new BpmnPlane("ContentPlane", "Sub",
                [.. document.Diagrams[0].Plane.Shapes.Where(shape => shape.BpmnElement != "Sub")], []))],
        };
    }
}
