using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Editing;

/// <summary>
/// Reconnecting a connector and changing its route.
/// </summary>
/// <remarks>
/// Reconnecting exists as a command rather than as delete-then-create because the connector has to
/// survive it: its id, its name, its condition, its extensions and every reference to it — a
/// Gateway's <c>default</c> most obviously — all stay valid only if the same element is edited in
/// place. Most of what is asserted here is that survival.
/// </remarks>
public sealed class ConnectorEditTests
{
    [Fact]
    public void MovesTheTargetToADifferentElement()
    {
        var result = BpmnDocumentEditor.Apply(
            Branching(),
            new ReconnectConnectorCommand("Flow_1", null, "Task_B", Route()));

        Assert.True(result.IsApplied);

        var flow = result.Document.SequenceFlows.Single(candidate => candidate.Id == "Flow_1");
        Assert.Equal("Event_1", flow.SourceRef);
        Assert.Equal("Task_B", flow.TargetRef);
    }

    [Fact]
    public void MovesTheSourceToADifferentElement()
    {
        var result = BpmnDocumentEditor.Apply(
            Branching(),
            new ReconnectConnectorCommand("Flow_1", "Task_B", null, Route()));

        var flow = result.Document.SequenceFlows.Single(candidate => candidate.Id == "Flow_1");
        Assert.Equal("Task_B", flow.SourceRef);
        Assert.Equal("Task_A", flow.TargetRef);
    }

    [Fact]
    public void KeepsEverythingElseAboutTheConnector()
    {
        // The whole reason this is not delete-then-create.
        var document = Branching();
        var before = document.SequenceFlows.Single(flow => flow.Id == "Flow_1");

        var result = BpmnDocumentEditor.Apply(
            document,
            new ReconnectConnectorCommand("Flow_1", null, "Task_B", Route()));

        var after = result.Document.SequenceFlows.Single(flow => flow.Id == "Flow_1");

        Assert.Equal(before.Id, after.Id);
        Assert.Equal(before.Name, after.Name);
        Assert.Equal(before.ConditionExpression, after.ConditionExpression);
        Assert.Equal(before.Extras, after.Extras);
        Assert.Equal(before.ContainerId, after.ContainerId);
    }

    [Fact]
    public void LeavesAGatewayDefaultFlowReferenceIntact()
    {
        // Deleting and recreating the flow would silently clear this.
        var document = WithDefaultFlow();

        var result = BpmnDocumentEditor.Apply(
            document,
            new ReconnectConnectorCommand("Flow_A", null, "Task_B", Route()));

        Assert.True(result.IsApplied);
        Assert.Equal(
            "Flow_A",
            result.Document.FlowNodes.Single(node => node.Id == "Gateway_1").DefaultFlowRef);
    }

    [Fact]
    public void WritesTheNewRouteOntoTheEdge()
    {
        var route = (IReadOnlyList<DiPoint>)[new DiPoint(1, 2), new DiPoint(3, 4), new DiPoint(5, 6)];

        var result = BpmnDocumentEditor.Apply(
            Branching(),
            new ReconnectConnectorCommand("Flow_1", null, "Task_B", route));

        // A diagonal route is not stored: a Sequence Flow is always right-angled, so it is replaced
        // by one between the same shapes.
        var written = EdgeOf(result.Document, "Flow_1").Waypoints;
        Assert.True(written.Count >= 2);
        Assert.All(written.Zip(written.Skip(1)), pair => Assert.True(
            Math.Abs(pair.First.X - pair.Second.X) < 0.5 || Math.Abs(pair.First.Y - pair.Second.Y) < 0.5));
    }

    [Fact]
    public void RefusesAConnectorThatDoesNotExist()
    {
        var result = BpmnDocumentEditor.Apply(
            Branching(),
            new ReconnectConnectorCommand("Nowhere", null, "Task_B", Route()));

        Assert.False(result.IsApplied);
        Assert.Equal("EDIT-060", Assert.Single(result.Rejections).Code);
    }

    [Fact]
    public void RefusesAnEndpointThatDoesNotExist()
    {
        var document = Branching();

        var result = BpmnDocumentEditor.Apply(
            document,
            new ReconnectConnectorCommand("Flow_1", null, "Nowhere", Route()));

        Assert.False(result.IsApplied);
        Assert.Equal("EDIT-062", Assert.Single(result.Rejections).Code);
        Assert.Same(document, result.Document);
    }

    [Fact]
    public void RefusesAConnectorToItself()
    {
        var result = BpmnDocumentEditor.Apply(
            Branching(),
            new ReconnectConnectorCommand("Flow_1", "Task_A", "Task_A", Route()));

        Assert.False(result.IsApplied);
        Assert.Equal("EDIT-063", Assert.Single(result.Rejections).Code);
    }

    [Fact]
    public void RefusesARouteWithFewerThanTwoWaypoints()
    {
        // DI-001 is the schema's own minimum, so a one-point edge does not round-trip.
        var result = BpmnDocumentEditor.Apply(
            Branching(),
            new ReconnectConnectorCommand("Flow_1", null, "Task_B", [new DiPoint(1, 2)]));

        Assert.False(result.IsApplied);
        Assert.Equal("DI-001", Assert.Single(result.Rejections).Code);
    }

    [Fact]
    public void ReconnectsAMessageFlowAcrossPools()
    {
        var document = TwoPools();

        var result = BpmnDocumentEditor.Apply(
            document,
            new ReconnectConnectorCommand("Message_1", null, "B_Event", Route()));

        Assert.True(result.IsApplied);
        Assert.Equal("B_Event", result.Document.MessageFlows.Single().TargetRef);
    }

    // --- rerouting ------------------------------------------------------------

    [Fact]
    public void ReplacesTheRouteWithoutTouchingTheEnds()
    {
        var document = Branching();
        var route = (IReadOnlyList<DiPoint>)[new DiPoint(0, 0), new DiPoint(40, 90), new DiPoint(100, 100)];

        var result = BpmnDocumentEditor.Apply(document, new UpdateWaypointsCommand("Flow_1", route));

        Assert.True(result.IsApplied);

        var written = EdgeOf(result.Document, "Flow_1").Waypoints;
        Assert.All(written.Zip(written.Skip(1)), pair => Assert.True(
            Math.Abs(pair.First.X - pair.Second.X) < 0.5 || Math.Abs(pair.First.Y - pair.Second.Y) < 0.5));

        var flow = result.Document.SequenceFlows.Single(candidate => candidate.Id == "Flow_1");
        Assert.Equal("Event_1", flow.SourceRef);
        Assert.Equal("Task_A", flow.TargetRef);
    }

    [Fact]
    public void KeepsARightAngledRouteTheUserShapedRatherThanRederivingIt()
    {
        // Bends that already make a right-angled route survive exactly, including ones that lie
        // nowhere near the shortest path. Only a diagonal is replaced.
        var route = (IReadOnlyList<DiPoint>)
        [
            new DiPoint(116, 138), new DiPoint(190, 138), new DiPoint(190, 60), new DiPoint(160, 60),
            new DiPoint(160, 120), new DiPoint(220, 120),
        ];

        var result = BpmnDocumentEditor.Apply(Branching(), new UpdateWaypointsCommand("Flow_1", route));

        Assert.Equal(route, EdgeOf(result.Document, "Flow_1").Waypoints);
    }

    [Fact]
    public void RefusesARerouteOfSomethingWithNoEdge()
    {
        var result = BpmnDocumentEditor.Apply(
            Branching(),
            new UpdateWaypointsCommand("Task_A", Route()));

        Assert.False(result.IsApplied);
        Assert.Equal("EDIT-064", Assert.Single(result.Rejections).Code);
    }

    [Fact]
    public void RefusesANonFiniteWaypoint()
    {
        var result = BpmnDocumentEditor.Apply(
            Branching(),
            new UpdateWaypointsCommand("Flow_1", [new DiPoint(0, 0), new DiPoint(double.NaN, 5)]));

        Assert.False(result.IsApplied);
        Assert.Equal("DI-003", Assert.Single(result.Rejections).Code);
    }

    [Fact]
    public void ARoundTripThroughTheWriterSurvivesAReconnect()
    {
        var document = Branching();
        var result = BpmnDocumentEditor.Apply(
            document,
            new ReconnectConnectorCommand("Flow_1", null, "Task_B", Route()));

        var xml = Hasseware.AspNetCore.Components.Bpmn.Serialization.BpmnXmlWriter.Write(result.Document).Xml;
        var reread = Hasseware.AspNetCore.Components.Bpmn.Serialization.BpmnXmlReader.Read(xml);

        var flow = reread.Document.SequenceFlows.Single(candidate => candidate.Id == "Flow_1");
        Assert.Equal("Task_B", flow.TargetRef);
        Assert.True(reread.Document.Diagrams[0].Plane.Edges
            .Single(edge => edge.BpmnElement == "Flow_1").Waypoints.Count >= 2);
    }

    // --- fixtures -------------------------------------------------------------

    private static IReadOnlyList<DiPoint> Route() => [new DiPoint(116, 138), new DiPoint(220, 138)];

    private static BpmnEdge EdgeOf(BpmnDocument document, string connectorId) =>
        document.Diagrams.SelectMany(diagram => diagram.Plane.Edges)
            .Single(edge => edge.BpmnElement == connectorId);

    private static BpmnDocument Branching() => new BpmnDocumentBuilder("Process_1")
        .Node("Event_1", BpmnFlowNodeType.StartEvent, new Bounds(80, 120, 36, 36), "Start")
        .Node("Task_A", BpmnFlowNodeType.UserTask, new Bounds(220, 100, 100, 80), "A")
        .Node("Task_B", BpmnFlowNodeType.UserTask, new Bounds(220, 260, 100, 80), "B")
        .Flow("Flow_1", "Event_1", "Task_A", [new DiPoint(116, 138), new DiPoint(220, 138)], "Go")
        .Build();

    private static BpmnDocument WithDefaultFlow()
    {
        var document = new BpmnDocumentBuilder("Process_1")
            .Node("Gateway_1", BpmnFlowNodeType.ExclusiveGateway, new Bounds(100, 100, 50, 50), "Choose")
            .Node("Task_A", BpmnFlowNodeType.UserTask, new Bounds(240, 80, 100, 80), "A")
            .Node("Task_B", BpmnFlowNodeType.UserTask, new Bounds(240, 240, 100, 80), "B")
            .Flow("Flow_A", "Gateway_1", "Task_A", [new DiPoint(150, 125), new DiPoint(240, 120)])
            .Build();

        return document with
        {
            FlowNodes =
            [
                document.FlowNodes[0] with { DefaultFlowRef = "Flow_A" },
                .. document.FlowNodes.Skip(1),
            ],
        };
    }

    private static BpmnDocument TwoPools()
    {
        var document = new BpmnDocumentBuilder("Process_A")
            .Pool("Participant_A", "A", new Bounds(0, 0, 600, 250))
            .Node("A_Task", BpmnFlowNodeType.SendTask, new Bounds(220, 60, 100, 80), "Send")
            .Node("B_Task", BpmnFlowNodeType.ReceiveTask, new Bounds(220, 360, 100, 80), "Receive", containerId: "Process_B")
            .Node(
                "B_Event",
                BpmnFlowNodeType.IntermediateCatchEvent,
                new Bounds(400, 380, 36, 36),
                "Heard",
                BpmnEventDefinitionType.Message,
                containerId: "Process_B")
            .Build();

        return document with
        {
            MessageFlows = [new BpmnMessageFlow("Message_1", "A_Task", "B_Task", "Order")],
            Diagrams =
            [
                document.Diagrams[0] with
                {
                    Plane = document.Diagrams[0].Plane with
                    {
                        Edges =
                        [
                            .. document.Diagrams[0].Plane.Edges,
                            new BpmnEdge("Message_1_di", "Message_1", [new DiPoint(270, 140), new DiPoint(270, 360)]),
                        ],
                    },
                },
            ],
        };
    }
}
