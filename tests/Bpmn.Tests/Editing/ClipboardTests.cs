using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;
using Hasseware.AspNetCore.Components.Bpmn.Validation;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Editing;

/// <summary>
/// Copy and paste.
/// </summary>
/// <remarks>
/// The interesting part is references. A copied fragment is full of ids pointing at other ids, and
/// the paste has to decide for each one whether it means "the copy of that" or "that exact thing":
/// a Sequence Flow between two copied nodes means the copies, a Call Activity's
/// <c>calledElementRef</c> means the same shared definition (section 2.7), and a default-flow
/// reference to a flow that was not copied means nothing at all.
/// </remarks>
public sealed class ClipboardTests
{
    [Fact]
    public void CopyingNothingGivesAnEmptyPayload()
    {
        Assert.True(BpmnClipboard.Copy(Linear(), []).IsEmpty);
    }

    [Fact]
    public void CopiesTheSelectedNodesAndTheirShapes()
    {
        var payload = BpmnClipboard.Copy(Linear(), ["Task_1"]);

        Assert.Single(payload.FlowNodes);
        Assert.Equal("Task_1", Assert.Single(payload.Shapes).BpmnElement);
    }

    [Fact]
    public void CopiesAConnectorOnlyWhenBothEndsAreCopied()
    {
        // A flow with one end outside the fragment would paste as an edge to an element that does
        // not exist.
        Assert.Empty(BpmnClipboard.Copy(Linear(), ["Task_1"]).SequenceFlows);
        Assert.Single(BpmnClipboard.Copy(Linear(), ["Event_1", "Task_1"]).SequenceFlows);
    }

    [Fact]
    public void RefusesToCopyAPoolOrALane()
    {
        // Copying a Pool means copying a Participant and the Process it references, which is a
        // structural transform; a Lane is not a container at all (section 2.8).
        var document = Pooled();

        Assert.True(BpmnClipboard.Copy(document, ["Participant_1"]).IsEmpty);
        Assert.True(BpmnClipboard.Copy(document, ["Lane_1"]).IsEmpty);
    }

    [Fact]
    public void CarriesASubProcessEntireNestedGraph()
    {
        // Section 2.7: copy/paste duplicates the entire nested graph, and each occurrence is a
        // distinct, unrelated set of elements.
        var payload = BpmnClipboard.Copy(Nested(), ["Sub_1"]);

        Assert.Contains(payload.FlowNodes, node => node.Id == "Sub_1");
        Assert.Contains(payload.FlowNodes, node => node.Id == "Inner_Task");
        Assert.Contains(payload.SequenceFlows, flow => flow.Id == "Inner_Flow");
    }

    [Fact]
    public void CarriesABoundaryEventWithItsHost()
    {
        var payload = BpmnClipboard.Copy(WithBoundary(), ["Task_1"]);

        Assert.Contains(payload.FlowNodes, node => node.Id == "Boundary_1");
    }

    [Fact]
    public void DropsABoundaryEventWhoseHostWasNotCopied()
    {
        // It cannot exist without its host, and there is no host in the fragment to attach it to.
        var payload = BpmnClipboard.Copy(WithBoundary(), ["Boundary_1"]);

        Assert.DoesNotContain(payload.FlowNodes, node => node.Id == "Boundary_1" && node.AttachedToRef is null);
        Assert.Contains(payload.FlowNodes, node => node.Id == "Boundary_1");
    }

    // --- pasting --------------------------------------------------------------

    [Fact]
    public void PastingGivesEveryElementAFreshId()
    {
        var document = Linear();
        var payload = BpmnClipboard.Copy(document, ["Event_1", "Task_1"]);

        var result = BpmnDocumentEditor.Apply(document, new PasteCommand(payload, "Process_1", new DiPoint(0, 300)));

        Assert.True(result.IsApplied);
        Assert.Equal(document.FlowNodes.Count + 2, result.Document.FlowNodes.Count);
        Assert.Equal(document.SequenceFlows.Count + 1, result.Document.SequenceFlows.Count);

        // Distinct ids, or the second occurrence would overwrite the first in the element index.
        var ids = result.Document.FlowNodes.Select(node => node.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void PastingAMultiInstanceNodeRemintsNestedIdsAndTheirReferences()
    {
        var document = Linear();
        var task = document.FindFlowNode("Task_1")!;
        var withLoopItems = document with
        {
            FlowNodes =
            [
                .. document.FlowNodes.Select(node => node.Id == task.Id
                    ? node with
                    {
                        LoopCharacteristics = BpmnLoopCharacteristics.MultiInstanceParallel,
                        MultiInstanceLoop = new BpmnMultiInstanceLoopDetails(
                            LoopDataInputRef: "Input_1",
                            InputDataItem: new BpmnLoopDataItem(Id: "Input_1"),
                            LoopDataOutputRef: "Output_1",
                            OutputDataItem: new BpmnLoopDataItem(Id: "Output_1")),
                    }
                    : node),
            ],
        };
        var payload = BpmnClipboard.Copy(withLoopItems, ["Task_1"]);

        var result = BpmnDocumentEditor.Apply(
            withLoopItems,
            new PasteCommand(payload, "Process_1", new DiPoint(0, 300)));

        Assert.True(result.IsApplied);
        var pasted = result.Document.FlowNodes.Single(node => node.Id != "Task_1" && node.Type == BpmnFlowNodeType.UserTask);
        var multi = pasted.MultiInstanceLoop!;
        Assert.NotEqual("Input_1", multi.InputDataItem?.Id);
        Assert.Equal(multi.InputDataItem?.Id, multi.LoopDataInputRef);
        Assert.NotEqual("Output_1", multi.OutputDataItem?.Id);
        Assert.Equal(multi.OutputDataItem?.Id, multi.LoopDataOutputRef);
        Assert.DoesNotContain(
            BpmnValidator.Validate(result.Document).Diagnostics,
            diagnostic => diagnostic.RuleId == "D018");
    }

    [Fact]
    public void RewiresACopiedConnectorToTheCopies()
    {
        var document = Linear();
        var payload = BpmnClipboard.Copy(document, ["Event_1", "Task_1"]);

        var result = BpmnDocumentEditor.Apply(document, new PasteCommand(payload, "Process_1", new DiPoint(0, 300)));

        var pasted = result.Document.SequenceFlows.Single(flow => flow.Id != "Flow_1");

        // The new flow must join the new nodes, not reach back to the originals.
        Assert.DoesNotContain(pasted.SourceRef, (string[])["Event_1", "Task_1"]);
        Assert.DoesNotContain(pasted.TargetRef, (string[])["Event_1", "Task_1"]);
        Assert.Contains(result.Document.FlowNodes, node => node.Id == pasted.SourceRef);
        Assert.Contains(result.Document.FlowNodes, node => node.Id == pasted.TargetRef);
    }

    [Fact]
    public void KeepsACallActivityPointingAtTheSameSharedDefinition()
    {
        // Section 2.7: a Call Activity is reusable by reference. Deep-cloning what it calls would
        // fork a definition that other Call Activities still share.
        var document = WithCallActivity();
        var payload = BpmnClipboard.Copy(document, ["Call_1"]);

        var result = BpmnDocumentEditor.Apply(document, new PasteCommand(payload, "Process_1", new DiPoint(0, 300)));

        var pasted = result.Document.FlowNodes.Single(node => node.Type == BpmnFlowNodeType.CallActivity && node.Id != "Call_1");
        Assert.Equal("GlobalProcess_1", pasted.CalledElementRef);
    }

    [Fact]
    public void RewiresABoundaryEventToTheCopiedHost()
    {
        var document = WithBoundary();
        var payload = BpmnClipboard.Copy(document, ["Task_1"]);

        var result = BpmnDocumentEditor.Apply(document, new PasteCommand(payload, "Process_1", new DiPoint(0, 300)));

        var boundary = result.Document.FlowNodes
            .Single(node => node.Type == BpmnFlowNodeType.BoundaryEvent && node.Id != "Boundary_1");

        Assert.NotEqual("Task_1", boundary.AttachedToRef);
        Assert.Contains(result.Document.FlowNodes, node => node.Id == boundary.AttachedToRef);
    }

    [Fact]
    public void KeepsNestedContentInsideTheCopiedSubProcessRatherThanThePasteTarget()
    {
        var document = Nested();
        var payload = BpmnClipboard.Copy(document, ["Sub_1"]);

        var result = BpmnDocumentEditor.Apply(document, new PasteCommand(payload, "Process_1", new DiPoint(0, 400)));

        var sub = result.Document.FlowNodes
            .Single(node => node.Type == BpmnFlowNodeType.SubProcess && node.Id != "Sub_1");
        var inner = result.Document.FlowNodes
            .Single(node => node.Name == "Inner" && node.Id != "Inner_Task");

        // Only the roots are re-parented into the paste target; what came with them keeps its
        // relationship to whichever copied element brought it.
        Assert.Equal("Process_1", sub.ContainerId);
        Assert.Equal(sub.Id, inner.ContainerId);
    }

    [Fact]
    public void ClearsADefaultFlowReferenceToAFlowThatWasNotCopied()
    {
        var document = WithDefaultFlow();
        var payload = BpmnClipboard.Copy(document, ["Gateway_1"]);

        var result = BpmnDocumentEditor.Apply(document, new PasteCommand(payload, "Process_1", new DiPoint(0, 300)));

        var pasted = result.Document.FlowNodes
            .Single(node => node.Type == BpmnFlowNodeType.ExclusiveGateway && node.Id != "Gateway_1");

        // Never rewritten by guesswork and never left dangling: the flow it named is not in the
        // fragment, so the copy has no default flow.
        Assert.Null(pasted.DefaultFlowRef);
    }

    [Fact]
    public void RewiresADefaultFlowReferenceWhenTheFlowWasCopiedToo()
    {
        var document = WithDefaultFlow();
        var payload = BpmnClipboard.Copy(document, ["Gateway_1", "Task_A"]);

        var result = BpmnDocumentEditor.Apply(document, new PasteCommand(payload, "Process_1", new DiPoint(0, 300)));

        var pasted = result.Document.FlowNodes
            .Single(node => node.Type == BpmnFlowNodeType.ExclusiveGateway && node.Id != "Gateway_1");

        Assert.NotNull(pasted.DefaultFlowRef);
        Assert.NotEqual("Flow_A", pasted.DefaultFlowRef);
        Assert.Contains(result.Document.SequenceFlows, flow => flow.Id == pasted.DefaultFlowRef);
    }

    [Fact]
    public void CarriesVendorExtensionsWithTheElementTheyBelongTo()
    {
        // Section 6.1: deep-clone extension content with its owner, and never rewrite arbitrary
        // strings inside it.
        var document = WithExtension();
        var payload = BpmnClipboard.Copy(document, ["Task_1"]);

        var result = BpmnDocumentEditor.Apply(document, new PasteCommand(payload, "Process_1", new DiPoint(0, 300)));

        var pasted = result.Document.FlowNodes.Single(node => node.Id != "Task_1" && node.Type == BpmnFlowNodeType.UserTask);
        Assert.Equal(document.FlowNodes[0].Extras, pasted.Extras);
    }

    [Fact]
    public void GivesEveryPastedShapeItsOwnDiEntry()
    {
        var document = Linear();
        var payload = BpmnClipboard.Copy(document, ["Event_1", "Task_1"]);

        var result = BpmnDocumentEditor.Apply(document, new PasteCommand(payload, "Process_1", new DiPoint(0, 300)));

        var shapes = result.Document.Diagrams.SelectMany(diagram => diagram.Plane.Shapes).ToList();

        // Section 9.3, again: an element with nothing to draw is not a created element.
        Assert.All(
            result.Document.FlowNodes,
            node => Assert.Contains(shapes, shape => shape.BpmnElement == node.Id));

        var ids = shapes.Select(shape => shape.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void OffsetsThePastedGeometry()
    {
        var document = Linear();
        var payload = BpmnClipboard.Copy(document, ["Task_1"]);

        var result = BpmnDocumentEditor.Apply(document, new PasteCommand(payload, "Process_1", new DiPoint(0, 300)));

        var original = document.Diagrams[0].Plane.Shapes.Single(shape => shape.BpmnElement == "Task_1");
        var pastedNode = result.Document.FlowNodes.Single(node => node.Id != "Task_1" && node.Type == BpmnFlowNodeType.UserTask);
        var pasted = result.Document.Diagrams[0].Plane.Shapes.Single(shape => shape.BpmnElement == pastedNode.Id);

        Assert.Equal(original.Bounds.X, pasted.Bounds.X);
        Assert.Equal(original.Bounds.Y + 300, pasted.Bounds.Y);
    }

    [Fact]
    public void StepsClearRatherThanRefusingWhenTheOffsetWouldCollide()
    {
        var document = Linear();
        var payload = BpmnClipboard.Copy(document, ["Task_1"]);

        // Straight on top of the original. Refusing here would be correct and useless: the user
        // asked for a copy and has no way to say where it should go.
        var result = BpmnDocumentEditor.Apply(document, new PasteCommand(payload, "Process_1", new DiPoint(0, 0)));

        Assert.True(result.IsApplied);

        var pastedNode = result.Document.FlowNodes.Single(node => node.Id != "Task_1" && node.Type == BpmnFlowNodeType.UserTask);
        var pasted = result.Document.Diagrams[0].Plane.Shapes.Single(shape => shape.BpmnElement == pastedNode.Id);
        var original = document.Diagrams[0].Plane.Shapes.Single(shape => shape.BpmnElement == "Task_1");

        Assert.NotEqual(original.Bounds, pasted.Bounds);
    }

    [Fact]
    public void RefusesAPasteIntoAContainerThatDoesNotExist()
    {
        var document = Linear();
        var payload = BpmnClipboard.Copy(document, ["Task_1"]);

        var result = BpmnDocumentEditor.Apply(document, new PasteCommand(payload, "Nowhere", new DiPoint(0, 300)));

        Assert.False(result.IsApplied);
        Assert.Equal("EDIT-041", Assert.Single(result.Rejections).Code);
        Assert.Same(document, result.Document);
    }

    [Fact]
    public void RefusesAnEmptyClipboard()
    {
        var result = BpmnDocumentEditor.Apply(
            Linear(),
            new PasteCommand(BpmnClipboardPayload.Empty, "Process_1", new DiPoint(0, 300)));

        Assert.False(result.IsApplied);
        Assert.Equal("EDIT-040", Assert.Single(result.Rejections).Code);
    }

    [Fact]
    public void PastingTwiceProducesTwoUnrelatedSets()
    {
        var document = Linear();
        var payload = BpmnClipboard.Copy(document, ["Event_1", "Task_1"]);

        var once = BpmnDocumentEditor.Apply(document, new PasteCommand(payload, "Process_1", new DiPoint(0, 300)));
        var twice = BpmnDocumentEditor.Apply(once.Document, new PasteCommand(payload, "Process_1", new DiPoint(0, 600)));

        Assert.True(twice.IsApplied);
        Assert.Equal(document.FlowNodes.Count + 4, twice.Document.FlowNodes.Count);

        var ids = twice.Document.FlowNodes.Select(node => node.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void ARoundTripThroughTheWriterSurvivesAPaste()
    {
        // The real test of reference rewiring: a document with a dangling ref does not round-trip.
        var document = Nested();
        var payload = BpmnClipboard.Copy(document, ["Sub_1"]);
        var result = BpmnDocumentEditor.Apply(document, new PasteCommand(payload, "Process_1", new DiPoint(0, 400)));

        Assert.True(result.IsApplied);

        var xml = Hasseware.AspNetCore.Components.Bpmn.Serialization.BpmnXmlWriter.Write(result.Document).Xml;
        var reread = Hasseware.AspNetCore.Components.Bpmn.Serialization.BpmnXmlReader.Read(xml);

        Assert.Equal(result.Document.FlowNodes.Count, reread.Document.FlowNodes.Count);
        Assert.All(
            reread.Document.FlowNodes.Where(node => node.AttachedToRef is not null),
            node => Assert.Contains(reread.Document.FlowNodes, candidate => candidate.Id == node.AttachedToRef));
    }

    [Fact]
    public void KeepsACollapsedSubProcessInteriorOnAPlaneOfItsOwn()
    {
        /*
         * A collapsed Sub-Process draws its interior on a separate plane (section 10.1). Flattening
         * the copied DI put that interior on whatever plane the paste landed on, so pasting one
         * collapsed Sub-Process spilled its whole nested graph onto the top level with nothing
         * hiding it.
         */
        var document = NestedOnItsOwnPlane();
        var payload = BpmnClipboard.Copy(document, ["Sub_1"]);

        Assert.Equal(2, payload.Planes.Count);
        Assert.Contains(payload.Planes, plane => plane.OwnerId is null);
        Assert.Contains(payload.Planes, plane => plane.OwnerId == "Sub_1");

        var result = BpmnDocumentEditor.Apply(document, new PasteCommand(payload, "Process_1", new DiPoint(0, 400)));
        Assert.True(result.IsApplied);

        var sub = result.Document.FlowNodes
            .Single(node => node.Type == BpmnFlowNodeType.SubProcess && node.Id != "Sub_1");

        // The active plane gained exactly the Sub-Process, and the interior went to a new diagram
        // owned by the copy.
        var active = result.Document.Diagrams[0].Plane;
        Assert.Equal(document.Diagrams[0].Plane.Shapes.Count + 1, active.Shapes.Count);
        Assert.DoesNotContain(active.Shapes, shape => shape.BpmnElement == "Inner_Task");

        var nested = Assert.Single(result.Document.Diagrams, diagram => diagram.Plane.BpmnElement == sub.Id);
        Assert.Single(nested.Plane.Shapes);
    }

    [Fact]
    public void DoesNotOffsetANestedPlanesOwnCoordinates()
    {
        // A nested plane has its own coordinate space: shifting it would move the interior
        // relative to a Sub-Process that has not itself moved.
        var document = NestedOnItsOwnPlane();
        var payload = BpmnClipboard.Copy(document, ["Sub_1"]);

        var result = BpmnDocumentEditor.Apply(document, new PasteCommand(payload, "Process_1", new DiPoint(0, 400)));

        var original = document.Diagrams[1].Plane.Shapes.Single(shape => shape.BpmnElement == "Inner_Task");
        var copied = result.Document.Diagrams
            .Single(diagram => diagram.Plane.BpmnElement is { } owner && owner != "Process_1" && owner.StartsWith("subProcess", StringComparison.Ordinal))
            .Plane.Shapes[0];

        Assert.Equal(original.Bounds, copied.Bounds);
    }

    // --- fixtures -------------------------------------------------------------

    private static BpmnDocument Linear() => new BpmnDocumentBuilder("Process_1")
        .Node("Event_1", BpmnFlowNodeType.StartEvent, new Bounds(80, 120, 36, 36), "Start")
        .Node("Task_1", BpmnFlowNodeType.UserTask, new Bounds(220, 100, 100, 80), "Work")
        .Flow("Flow_1", "Event_1", "Task_1", [new DiPoint(116, 138), new DiPoint(220, 140)])
        .Build();

    private static BpmnDocument Pooled() => new BpmnDocumentBuilder("Process_1")
        .Pool("Participant_1", "Customer", new Bounds(0, 0, 600, 250))
        .Lane("Lane_1", "Sales", new Bounds(30, 0, 570, 250), ["Task_1"])
        .Node("Task_1", BpmnFlowNodeType.UserTask, new Bounds(220, 100, 100, 80), "Work")
        .Build();

    private static BpmnDocument Nested() => new BpmnDocumentBuilder("Process_1")
        .Node("Sub_1", BpmnFlowNodeType.SubProcess, new Bounds(100, 100, 350, 200), "Outer")
        .Node("Inner_Task", BpmnFlowNodeType.UserTask, new Bounds(140, 150, 100, 80), "Inner", containerId: "Sub_1")
        .Node("Inner_End", BpmnFlowNodeType.EndEvent, new Bounds(320, 170, 36, 36), "Done", containerId: "Sub_1")
        .Flow(
            "Inner_Flow",
            "Inner_Task",
            "Inner_End",
            [new DiPoint(240, 190), new DiPoint(320, 188)],
            containerId: "Sub_1")
        .Build();

    /// <summary>A collapsed Sub-Process whose interior is drawn on its own plane, as a file does.</summary>
    private static BpmnDocument NestedOnItsOwnPlane()
    {
        var document = new BpmnDocumentBuilder("Process_1")
            .Node("Sub_1", BpmnFlowNodeType.SubProcess, new Bounds(100, 100, 120, 80), "Outer")
            .Build();

        var inner = new BpmnFlowNode("Inner_Task", BpmnFlowNodeType.UserTask, "Sub_1", "Inner");
        var innerShape = new BpmnShape("Inner_Task_di", "Inner_Task", new Bounds(40, 40, 100, 80));

        return document with
        {
            FlowNodes = [.. document.FlowNodes, inner],
            Diagrams =
            [
                document.Diagrams[0] with
                {
                    Plane = document.Diagrams[0].Plane with
                    {
                        Shapes = [.. document.Diagrams[0].Plane.Shapes.Select(shape =>
                            shape.BpmnElement == "Sub_1" ? shape with { IsExpanded = false } : shape)],
                    },
                },
                new BpmnDiagram("Diagram_Sub", new BpmnPlane("Plane_Sub", "Sub_1", [innerShape], [])),
            ],
        };
    }

    private static BpmnDocument WithBoundary() => new BpmnDocumentBuilder("Process_1")
        .Node("Task_1", BpmnFlowNodeType.UserTask, new Bounds(220, 100, 100, 80), "Work")
        .Node(
            "Boundary_1",
            BpmnFlowNodeType.BoundaryEvent,
            new Bounds(302, 162, 36, 36),
            "Timeout",
            BpmnEventDefinitionType.Timer,
            attachedToRef: "Task_1")
        .Build();

    private static BpmnDocument WithCallActivity()
    {
        var document = new BpmnDocumentBuilder("Process_1")
            .Node("Call_1", BpmnFlowNodeType.CallActivity, new Bounds(220, 100, 100, 80), "Call")
            .Build();

        return document with
        {
            FlowNodes = [document.FlowNodes[0] with { CalledElementRef = "GlobalProcess_1" }],
        };
    }

    private static BpmnDocument WithDefaultFlow()
    {
        var document = new BpmnDocumentBuilder("Process_1")
            .Node("Gateway_1", BpmnFlowNodeType.ExclusiveGateway, new Bounds(200, 100, 50, 50), "Choose")
            .Node("Task_A", BpmnFlowNodeType.UserTask, new Bounds(340, 80, 100, 80), "A")
            .Flow("Flow_A", "Gateway_1", "Task_A", [new DiPoint(250, 125), new DiPoint(340, 120)])
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

    private static BpmnDocument WithExtension()
    {
        var document = new BpmnDocumentBuilder("Process_1")
            .Node("Task_1", BpmnFlowNodeType.UserTask, new Bounds(220, 100, 100, 80), "Work")
            .Build();

        var extension = new Model.Extensions.BpmnExtensionElement(
            "http://example.org/vendor",
            "taskConfig",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["retries"] = "3" },
            []);

        return document with
        {
            FlowNodes =
            [
                document.FlowNodes[0] with
                {
                    Extras = document.FlowNodes[0].Extras with { Extensions = [extension] },
                },
            ],
        };
    }
}
