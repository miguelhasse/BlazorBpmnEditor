using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;
using Hasseware.AspNetCore.Components.Bpmn.Serialization;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Editing;

/// <summary>
/// Creating elements and connectors.
/// </summary>
/// <remarks>
/// Section 9.3 makes creation a transaction: validate everything, then build the semantic object
/// and its DI together, or leave nothing behind. The refusal cases matter more than the happy path
/// here, because a half-created element is a document that will not round-trip.
/// </remarks>
public sealed class CreationCommandTests
{
    [Fact]
    public void CreatesAFlowNodeAndItsShapeTogether()
    {
        var result = BpmnDocumentEditor.Apply(
            Diagram(),
            new CreateElementCommand("Task_new", BpmnFlowNodeType.UserTask, "Process_1", new Bounds(600, 400, 100, 80), "Review"));

        Assert.True(result.IsApplied);

        var node = Assert.Single(result.Document.FlowNodes, candidate => candidate.Id == "Task_new");
        Assert.Equal("Review", node.Name);
        Assert.Equal("Process_1", node.ContainerId);

        // Section 9.3: exactly one BPMNShape, or the element exists with nothing to draw.
        var shape = Assert.Single(
            result.Document.Diagrams.SelectMany(d => d.Plane.Shapes),
            candidate => candidate.BpmnElement == "Task_new");

        Assert.Equal(new Bounds(600, 400, 100, 80), shape.Bounds);
    }

    [Fact]
    public void MarksASubProcessAsOwningItsContent()
    {
        // FlowElementsOwned is what makes the node a valid container for later creations, and what
        // distinguishes an embedded Sub-Process from a Call Activity.
        var result = BpmnDocumentEditor.Apply(
            Diagram(),
            new CreateElementCommand("Sub_new", BpmnFlowNodeType.SubProcess, "Process_1", new Bounds(600, 400, 350, 200)));

        Assert.True(Assert.Single(result.Document.FlowNodes, n => n.Id == "Sub_new").FlowElementsOwned);
    }

    [Fact]
    public void AndDrawsItExpanded()
    {
        /*
         * The size says so already — 350x200 is a container with room inside, not the 100x80 leaf
         * a collapsed Sub-Process draws as — but the shape has to say so too. Unstated, the
         * renderer read it as collapsed and drew the plus marker and a centred label on a box three
         * times too big, while the stencil read it as expanded and offered to create inside it.
         */
        var result = BpmnDocumentEditor.Apply(
            Diagram(),
            new CreateElementCommand("Sub_new", BpmnFlowNodeType.SubProcess, "Process_1", new Bounds(600, 400, 350, 200)));

        Assert.True(ShapeOf(result.Document, "Sub_new").IsExpanded);
    }

    [Fact]
    public void ButLeavesExpansionUnstatedOnEverythingThatCannotBeExpanded()
    {
        // isExpanded is meaningless on a Task, and a shape that carries it anyway would be one
        // more thing an importing tool has to decide how to ignore (section 10.4's reasoning for
        // isHorizontal, applied to the neighbouring attribute).
        var result = BpmnDocumentEditor.Apply(
            Diagram(),
            new CreateElementCommand("Task_new", BpmnFlowNodeType.UserTask, "Process_1", new Bounds(600, 400, 100, 80)));

        Assert.Null(ShapeOf(result.Document, "Task_new").IsExpanded);
    }

    [Fact]
    public void DoesNotMarkACallActivityAsOwningContent()
    {
        // Section 2.7: a Call Activity has no nested graph in this diagram at all. It does need
        // something to call, though — CT-010 — which is why this names a definition.
        var document = Diagram();
        var withDefinition = document with
        {
            Processes = [.. document.Processes, new BpmnProcess("Reusable_1", "Reusable")],
        };

        var result = BpmnDocumentEditor.Apply(
            withDefinition,
            new CreateElementCommand(
                "Call_new",
                BpmnFlowNodeType.CallActivity,
                "Process_1",
                new Bounds(600, 400, 100, 80),
                CalledElementRef: "Reusable_1"));

        Assert.True(result.IsApplied);
        Assert.False(Assert.Single(result.Document.FlowNodes, n => n.Id == "Call_new").FlowElementsOwned);
    }

    [Fact]
    public void RefusesAnIdThatIsAlreadyTaken()
    {
        var document = Diagram();

        var result = BpmnDocumentEditor.Apply(
            document,
            new CreateElementCommand("Task_1", BpmnFlowNodeType.UserTask, "Process_1", new Bounds(600, 400, 100, 80)));

        Assert.False(result.IsApplied);
        Assert.Same(document, result.Document);
        Assert.Equal("EDIT-030", Assert.Single(result.Rejections).Code);
    }

    [Fact]
    public void RefusesAContainerThatIsNotAFlowElementsContainer()
    {
        // Section 9.3: every created Flow Node belongs to exactly one container before it can take
        // part in Sequence Flow, so an unknown container is refused rather than invented.
        var result = BpmnDocumentEditor.Apply(
            Diagram(),
            new CreateElementCommand("Task_new", BpmnFlowNodeType.UserTask, "Nowhere", new Bounds(600, 400, 100, 80)));

        Assert.Equal("CT-001", Assert.Single(result.Rejections).Code);
    }

    [Fact]
    public void AcceptsAnEmbeddedSubProcessAsAContainer()
    {
        var document = BpmnDocumentEditor.Apply(
            Diagram(),
            new CreateElementCommand("Sub_new", BpmnFlowNodeType.SubProcess, "Process_1", new Bounds(600, 400, 350, 200)))
            .Document;

        var result = BpmnDocumentEditor.Apply(
            document,
            new CreateElementCommand("Inner", BpmnFlowNodeType.StartEvent, "Sub_new", new Bounds(630, 470, 36, 36)));

        Assert.True(result.IsApplied);
        Assert.Equal("Sub_new", Assert.Single(result.Document.FlowNodes, n => n.Id == "Inner").ContainerId);
    }

    [Fact]
    public void RefusesAFreeFloatingBoundaryEvent()
    {
        // Section 9.3: Boundary Event creation requires an Activity host and sets attachedToRef
        // immediately; it cannot exist unattached.
        var result = BpmnDocumentEditor.Apply(
            Diagram(),
            new CreateElementCommand("Boundary_new", BpmnFlowNodeType.BoundaryEvent, "Process_1", new Bounds(600, 400, 36, 36)));

        Assert.Equal("EV-001", Assert.Single(result.Rejections).Code);
    }

    [Fact]
    public void RefusesABoundaryEventAttachedToSomethingThatIsNotAnActivity()
    {
        var result = BpmnDocumentEditor.Apply(
            Diagram(),
            new CreateElementCommand(
                "Boundary_new",
                BpmnFlowNodeType.BoundaryEvent,
                "Process_1",
                new Bounds(600, 400, 36, 36),
                AttachedToRef: "Event_1"));

        Assert.Equal("EV-001", Assert.Single(result.Rejections).Code);
    }

    [Fact]
    public void CreatesABoundaryEventOnAnActivity()
    {
        var result = BpmnDocumentEditor.Apply(
            Diagram(),
            new CreateElementCommand(
                "Boundary_new",
                BpmnFlowNodeType.BoundaryEvent,
                "Process_1",
                new Bounds(260, 162, 36, 36),
                EventDefinition: BpmnEventDefinitionType.Timer,
                AttachedToRef: "Task_1"));

        Assert.True(result.IsApplied);

        var node = Assert.Single(result.Document.FlowNodes, n => n.Id == "Boundary_new");
        Assert.Equal("Task_1", node.AttachedToRef);
        Assert.Equal(BpmnEventDefinitionType.Timer, node.EventDefinition);
    }

    [Fact]
    public void RefusesAttachingAnythingElseToAnActivity()
    {
        var result = BpmnDocumentEditor.Apply(
            Diagram(),
            new CreateElementCommand(
                "Task_new",
                BpmnFlowNodeType.UserTask,
                "Process_1",
                new Bounds(600, 400, 100, 80),
                AttachedToRef: "Task_1"));

        Assert.Equal("EV-002", Assert.Single(result.Rejections).Code);
    }

    [Fact]
    public void RefusesADropThatCollidesAndLeavesNothingBehind()
    {
        // Section 9.3 is explicit that an invalid stencil drop is rejected with DI-011 and leaves
        // no partial semantic or DI object.
        var document = Diagram();

        var result = BpmnDocumentEditor.Apply(
            document,
            new CreateElementCommand("Task_new", BpmnFlowNodeType.UserTask, "Process_1", new Bounds(240, 120, 100, 80)));

        Assert.False(result.IsApplied);
        Assert.Equal("DI-011", Assert.Single(result.Rejections).Code);
        Assert.Same(document, result.Document);
        Assert.DoesNotContain("Task_new", result.Document.FlowNodes.Select(n => n.Id));
        Assert.DoesNotContain(
            "Task_new",
            result.Document.Diagrams.SelectMany(d => d.Plane.Shapes).Select(s => s.BpmnElement));
    }

    [Fact]
    public void AllowsACollidingDropWhenPreventionIsOff()
    {
        var options = EditingOptions.Default with { PreventCollisions = false };

        var result = BpmnDocumentEditor.Apply(
            Diagram(),
            new CreateElementCommand("Task_new", BpmnFlowNodeType.UserTask, "Process_1", new Bounds(240, 120, 100, 80)),
            options);

        Assert.True(result.IsApplied);
    }

    // --- connectors -----------------------------------------------------------

    [Fact]
    public void CreatesASequenceFlowAndItsEdge()
    {
        var result = BpmnDocumentEditor.Apply(
            Diagram(),
            new CreateConnectorCommand(
                "Flow_new",
                BpmnConnectorKind.SequenceFlow,
                "Task_1",
                "Event_2",
                [new DiPoint(320, 140), new DiPoint(420, 140)]));

        Assert.True(result.IsApplied);

        var flow = Assert.Single(result.Document.SequenceFlows, f => f.Id == "Flow_new");
        Assert.Equal("Process_1", flow.ContainerId);

        var edge = Assert.Single(
            result.Document.Diagrams.SelectMany(d => d.Plane.Edges),
            e => e.BpmnElement == "Flow_new");

        Assert.Equal(2, edge.Waypoints.Count);
    }

    [Fact]
    public void DerivesTheContainerFromTheSourceWhenNoneIsGiven()
    {
        var document = BpmnDocumentEditor.Apply(
            Diagram(),
            new CreateElementCommand("Sub_new", BpmnFlowNodeType.SubProcess, "Process_1", new Bounds(600, 400, 350, 200)))
            .Document;

        document = BpmnDocumentEditor.Apply(
            document,
            new CreateElementCommand("A", BpmnFlowNodeType.StartEvent, "Sub_new", new Bounds(620, 470, 36, 36))).Document;

        document = BpmnDocumentEditor.Apply(
            document,
            new CreateElementCommand("B", BpmnFlowNodeType.EndEvent, "Sub_new", new Bounds(880, 470, 36, 36))).Document;

        var result = BpmnDocumentEditor.Apply(
            document,
            new CreateConnectorCommand(
                "Flow_inner",
                BpmnConnectorKind.SequenceFlow,
                "A",
                "B",
                [new DiPoint(656, 488), new DiPoint(880, 488)]));

        // The nested flow must stay nested (section 2.7), which means inheriting the source's
        // container rather than defaulting to the root Process.
        Assert.Equal("Sub_new", Assert.Single(result.Document.SequenceFlows, f => f.Id == "Flow_inner").ContainerId);
    }

    [Fact]
    public void CreatesAMessageFlowWithoutAContainer()
    {
        // A Message Flow belongs to the Collaboration, not to a Process, so it needs no container.
        var result = BpmnDocumentEditor.Apply(
            Diagram(),
            new CreateConnectorCommand(
                "Message_new",
                BpmnConnectorKind.MessageFlow,
                "Task_1",
                "Event_2",
                [new DiPoint(320, 140), new DiPoint(420, 140)]));

        Assert.True(result.IsApplied);
        Assert.Single(result.Document.MessageFlows, f => f.Id == "Message_new");
    }

    [Theory]
    [InlineData("Ghost", "Task_1", "EDIT-031")]
    [InlineData("Task_1", "Ghost", "EDIT-031")]
    [InlineData("Task_1", "Task_1", "EDIT-032")]
    public void RefusesUnusableEndpoints(string source, string target, string code)
    {
        var result = BpmnDocumentEditor.Apply(
            Diagram(),
            new CreateConnectorCommand(
                "Flow_new",
                BpmnConnectorKind.SequenceFlow,
                source,
                target,
                [new DiPoint(0, 0), new DiPoint(10, 10)]));

        Assert.Equal(code, Assert.Single(result.Rejections).Code);
    }

    [Fact]
    public void RefusesAConnectorWithFewerThanTwoWaypoints()
    {
        // DI-001 is a schema minimum: a one-point edge cannot be drawn or round-tripped.
        var result = BpmnDocumentEditor.Apply(
            Diagram(),
            new CreateConnectorCommand(
                "Flow_new",
                BpmnConnectorKind.SequenceFlow,
                "Task_1",
                "Event_2",
                [new DiPoint(0, 0)]));

        Assert.Equal("DI-001", Assert.Single(result.Rejections).Code);
    }

    [Fact]
    public void RefusesNonFiniteWaypoints()
    {
        var result = BpmnDocumentEditor.Apply(
            Diagram(),
            new CreateConnectorCommand(
                "Flow_new",
                BpmnConnectorKind.SequenceFlow,
                "Task_1",
                "Event_2",
                [new DiPoint(0, 0), new DiPoint(double.NaN, 10)]));

        Assert.Equal("DI-003", Assert.Single(result.Rejections).Code);
    }

    [Fact]
    public void RunsADataAssociationTheWayTheDataFlows()
    {
        // Section 2.9: data into an Activity is an Input Association, data out of one is an Output
        // Association. The same gesture means different things by which end the reference is.
        var document = new BpmnDocumentBuilder("Process_1")
            .Node("Task_1", BpmnFlowNodeType.UserTask, new Bounds(220, 100, 100, 80), "Work")
            .DataObject("Data_1", "Order", new Bounds(400, 300, 36, 50))
            .Build();

        var intoActivity = BpmnDocumentEditor.Apply(
            document,
            new CreateConnectorCommand(
                "Assoc_in",
                BpmnConnectorKind.DataAssociation,
                "Data_1",
                "Task_1",
                [new DiPoint(400, 300), new DiPoint(270, 180)],
                ContainerId: "Process_1"));

        Assert.Equal(
            BpmnDataAssociationKind.Input,
            Assert.Single(intoActivity.Document.DataAssociations).Kind);

        var outOfActivity = BpmnDocumentEditor.Apply(
            document,
            new CreateConnectorCommand(
                "Assoc_out",
                BpmnConnectorKind.DataAssociation,
                "Task_1",
                "Data_1",
                [new DiPoint(270, 180), new DiPoint(400, 300)],
                ContainerId: "Process_1"));

        Assert.Equal(
            BpmnDataAssociationKind.Output,
            Assert.Single(outOfActivity.Document.DataAssociations).Kind);
    }

    [Fact]
    public void CreatedElementsAndConnectorsSurviveARoundTrip()
    {
        // The real test of "created DI is complete": write it out and read it back.
        var document = BpmnDocumentEditor.Apply(
            Diagram(),
            new CreateElementCommand("Gateway_new", BpmnFlowNodeType.ExclusiveGateway, "Process_1", new Bounds(600, 118, 50, 50)))
            .Document;

        document = BpmnDocumentEditor.Apply(
            document,
            new CreateConnectorCommand(
                "Flow_new",
                BpmnConnectorKind.SequenceFlow,
                "Task_1",
                "Gateway_new",
                [new DiPoint(320, 140), new DiPoint(600, 143)])).Document;

        var xml = BpmnXmlWriter.Write(document).Xml;
        var reread = BpmnXmlReader.Read(xml).Document;

        Assert.Single(reread.FlowNodes, n => n.Id == "Gateway_new");
        Assert.Single(reread.SequenceFlows, f => f.Id == "Flow_new");
        Assert.Single(reread.Diagrams.SelectMany(d => d.Plane.Shapes), s => s.BpmnElement == "Gateway_new");
        Assert.Single(reread.Diagrams.SelectMany(d => d.Plane.Edges), e => e.BpmnElement == "Flow_new");
    }

    [Fact]
    public void ResolvesAnElementCreatedByAPreviousCommand()
    {
        // A record's `with` expression copies every field, so a cached element index used to
        // survive into the edited document — and a lookup for anything just created would miss.
        // Two chained creations is the smallest thing that catches it: the connector's endpoint
        // validation could not see the element the previous command had added.
        var document = BpmnDocumentEditor.Apply(
            Diagram(),
            new CreateElementCommand("Gateway_new", BpmnFlowNodeType.ExclusiveGateway, "Process_1", new Bounds(600, 118, 50, 50)))
            .Document;

        Assert.NotNull(document.Find("Gateway_new"));
        Assert.NotNull(document.FindFlowNode("Gateway_new"));

        // And the original must not have gained it.
        Assert.Null(Diagram().Find("Gateway_new"));
    }

    [Fact]
    public void ForgetsAnElementThatAnEditRemoved()
    {
        var document = BpmnDocumentEditor.Apply(Diagram(), new DeleteElementsCommand(["Task_1"])).Document;

        Assert.Null(document.Find("Task_1"));
    }

    // --- ids ------------------------------------------------------------------

    [Fact]
    public void MintsUniquePrefixedIds()
    {
        var ids = Enumerable.Range(0, 100).Select(_ => BpmnId.New("Task")).ToList();

        Assert.Equal(100, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.All(ids, id => Assert.StartsWith("Task_", id, StringComparison.Ordinal));
    }

    [Fact]
    public void MintsIdsThatAreValidXmlIds()
    {
        // An xsd:ID may not start with a digit, which a bare GUID can — the prefix is load-bearing.
        Assert.All(
            Enumerable.Range(0, 20).Select(_ => BpmnId.New("Flow")),
            id => Assert.True(char.IsLetter(id[0]) || id[0] == '_', $"'{id}' cannot start an xsd:ID"));
    }

    [Fact]
    public void StripsCharactersAnXmlIdCannotContain()
    {
        var id = BpmnId.New("Order Process!");

        Assert.StartsWith("OrderProcess_", id, StringComparison.Ordinal);
    }

    [Fact]
    public void PairsADiIdWithItsElement() =>
        Assert.Equal("Task_1_di", BpmnId.ForDiagramElement("Task_1"));

    private static BpmnShape ShapeOf(BpmnDocument document, string elementId) => Assert.Single(
        document.Diagrams.SelectMany(diagram => diagram.Plane.Shapes),
        shape => shape.BpmnElement == elementId);

    private static BpmnDocument Diagram() => new BpmnDocumentBuilder("Process_1")
        .Node("Event_1", BpmnFlowNodeType.StartEvent, new Bounds(80, 120, 36, 36), "Start")
        .Node("Task_1", BpmnFlowNodeType.UserTask, new Bounds(220, 100, 100, 80), "Work")
        .Node("Event_2", BpmnFlowNodeType.EndEvent, new Bounds(420, 120, 36, 36), "Done")
        .Build();
}
