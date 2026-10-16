using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Editing;

/// <summary>
/// Creating artifacts, data references and Call Activities.
/// </summary>
/// <remarks>
/// The three things the stencil could not previously offer, and each for its own reason: an
/// artifact is not a Flow Node, a data reference points at a definition that is never drawn, and a
/// Call Activity cannot exist until it names what it calls (section 9.1).
/// </remarks>
public sealed class ArtifactCreationTests
{
    // --- artifacts ------------------------------------------------------------

    [Fact]
    public void CreatesATextAnnotationAndItsShape()
    {
        var result = BpmnDocumentEditor.Apply(
            Process(),
            new CreateArtifactCommand("Note_1", BpmnArtifactKind.TextAnnotation, "Process_1", new Bounds(400, 40, 120, 60), "Needs review"));

        Assert.True(result.IsApplied);

        var annotation = Assert.Single(result.Document.TextAnnotations);
        Assert.Equal("Needs review", annotation.Text);
        Assert.Equal("Process_1", annotation.ContainerId);
        Assert.Contains(result.Document.Diagrams[0].Plane.Shapes, shape => shape.BpmnElement == "Note_1");
    }

    [Fact]
    public void CreatesAGroupAndItsShape()
    {
        var result = BpmnDocumentEditor.Apply(
            Process(),
            new CreateArtifactCommand("Group_1", BpmnArtifactKind.Group, "Process_1", new Bounds(40, 40, 300, 200), Name: "Phase one"));

        Assert.True(result.IsApplied);
        Assert.Equal("Phase one", Assert.Single(result.Document.Groups).Name);
    }

    [Fact]
    public void LetsAnArtifactOverlapAFlowNode()
    {
        // DI-011 governs Events, Activities and Gateways. An annotation is meant to sit beside the
        // thing it annotates and a Group is meant to enclose things.
        var result = BpmnDocumentEditor.Apply(
            Process(),
            new CreateArtifactCommand("Group_1", BpmnArtifactKind.Group, "Process_1", new Bounds(60, 100, 400, 200)));

        Assert.True(result.IsApplied);
    }

    [Fact]
    public void AllowsAnArtifactOnACollaborationPlane()
    {
        // Section 9.1: Text Annotations and Groups created on a Collaboration plane belong to the
        // Collaboration's artifact collection.
        var document = Pooled();

        var result = BpmnDocumentEditor.Apply(
            document,
            new CreateArtifactCommand("Note_1", BpmnArtifactKind.TextAnnotation, document.Collaboration!.Id, new Bounds(0, 400, 120, 60), "Aside"));

        Assert.True(result.IsApplied);
        Assert.Equal(document.Collaboration.Id, Assert.Single(result.Document.TextAnnotations).ContainerId);
    }

    [Fact]
    public void RefusesAnArtifactInSomethingThatCannotHoldOne()
    {
        var result = BpmnDocumentEditor.Apply(
            Process(),
            new CreateArtifactCommand("Note_1", BpmnArtifactKind.TextAnnotation, "Event_1", new Bounds(0, 0, 120, 60)));

        Assert.False(result.IsApplied);
        Assert.Equal("EDIT-031", Assert.Single(result.Rejections).Code);
    }

    // --- data -----------------------------------------------------------------

    [Fact]
    public void CreatesADataObjectReferenceAndItsDefinition()
    {
        var result = BpmnDocumentEditor.Apply(
            Process(),
            new CreateDataReferenceCommand(
                "DataRef_1",
                BpmnDataKind.DataObject,
                "Process_1",
                new Bounds(400, 200, 36, 50),
                "Order",
                NewDefinitionId: "Data_1"));

        Assert.True(result.IsApplied);

        // The reference is drawn; the definition is not, and the reference points at it.
        var reference = Assert.Single(result.Document.DataObjectReferences);
        Assert.Equal("Data_1", reference.DataObjectRef);
        Assert.Equal("Data_1", Assert.Single(result.Document.DataObjects).Id);
        Assert.DoesNotContain(result.Document.Diagrams[0].Plane.Shapes, shape => shape.BpmnElement == "Data_1");
    }

    [Fact]
    public void PutsADataStoreDefinitionInTheDocumentCatalog()
    {
        // A Data Store is document-scoped (section 1.0), unlike a Data Object which belongs to a
        // container. That difference is the whole reason the two kinds are distinguished.
        var result = BpmnDocumentEditor.Apply(
            Process(),
            new CreateDataReferenceCommand(
                "StoreRef_1",
                BpmnDataKind.DataStore,
                "Process_1",
                new Bounds(400, 300, 50, 50),
                "Ledger",
                NewDefinitionId: "Store_1"));

        Assert.True(result.IsApplied);
        Assert.Equal("Store_1", Assert.Single(result.Document.Catalog.DataStores).Id);
        Assert.Empty(result.Document.DataObjects);
    }

    [Fact]
    public void PointsAtAnExistingDefinitionWithoutDuplicatingIt()
    {
        var document = BpmnDocumentEditor.Apply(
            Process(),
            new CreateDataReferenceCommand("DataRef_1", BpmnDataKind.DataObject, "Process_1", new Bounds(400, 200, 36, 50), "Order", NewDefinitionId: "Data_1")).Document;

        var result = BpmnDocumentEditor.Apply(
            document,
            new CreateDataReferenceCommand("DataRef_2", BpmnDataKind.DataObject, "Process_1", new Bounds(400, 300, 36, 50), "Order", DefinitionRef: "Data_1"));

        Assert.True(result.IsApplied);

        // Section 9.1 forbids duplicating definitions accidentally: two references, one definition.
        Assert.Equal(2, result.Document.DataObjectReferences.Count);
        Assert.Single(result.Document.DataObjects);
    }

    [Fact]
    public void RefusesADataReferenceThatAsksForBothOrNeither()
    {
        // The choice has to be made, because guessing is how definitions get duplicated.
        var both = BpmnDocumentEditor.Apply(
            Process(),
            new CreateDataReferenceCommand("DataRef_1", BpmnDataKind.DataObject, "Process_1", new Bounds(400, 200, 36, 50), DefinitionRef: "Data_1", NewDefinitionId: "Data_2"));

        var neither = BpmnDocumentEditor.Apply(
            Process(),
            new CreateDataReferenceCommand("DataRef_1", BpmnDataKind.DataObject, "Process_1", new Bounds(400, 200, 36, 50)));

        Assert.False(both.IsApplied);
        Assert.False(neither.IsApplied);
        Assert.Equal("CT-011", Assert.Single(both.Rejections).Code);
        Assert.Equal("CT-011", Assert.Single(neither.Rejections).Code);
    }

    [Fact]
    public void RefusesAReferenceToADefinitionThatDoesNotExist()
    {
        var result = BpmnDocumentEditor.Apply(
            Process(),
            new CreateDataReferenceCommand("DataRef_1", BpmnDataKind.DataObject, "Process_1", new Bounds(400, 200, 36, 50), DefinitionRef: "Nowhere"));

        Assert.False(result.IsApplied);
        Assert.Equal("CT-011", Assert.Single(result.Rejections).Code);
    }

    [Fact]
    public void RefusesADataReferenceOnACollaborationPlane()
    {
        // Section 9.1: Data Objects belong inside a Participant's referenced Process, never as
        // Collaboration children.
        var document = Pooled();

        var result = BpmnDocumentEditor.Apply(
            document,
            new CreateDataReferenceCommand("DataRef_1", BpmnDataKind.DataObject, document.Collaboration!.Id, new Bounds(0, 400, 36, 50), NewDefinitionId: "Data_1"));

        Assert.False(result.IsApplied);
        Assert.Equal("EDIT-031", Assert.Single(result.Rejections).Code);
    }

    // --- call activity --------------------------------------------------------

    [Fact]
    public void CreatesACallActivityAgainstAGlobalProcess()
    {
        var result = BpmnDocumentEditor.Apply(
            WithGlobalDefinition(),
            new CreateElementCommand(
                "Call_1",
                BpmnFlowNodeType.CallActivity,
                "Process_1",
                new Bounds(400, 100, 100, 80),
                "Do the thing",
                CalledElementRef: "Reusable_1"));

        Assert.True(result.IsApplied);

        var call = result.Document.FlowNodes.Single(node => node.Id == "Call_1");
        Assert.Equal("Reusable_1", call.CalledElementRef);

        // Section 2.7: a Call Activity never owns local flow elements.
        Assert.False(call.FlowElementsOwned);
    }

    [Fact]
    public void RefusesACallActivityWithNothingToCall()
    {
        // CT-010. Section 9.1 forbids inventing a hidden embedded graph to fill the gap, so the
        // creation is refused rather than half-done.
        var result = BpmnDocumentEditor.Apply(
            WithGlobalDefinition(),
            new CreateElementCommand("Call_1", BpmnFlowNodeType.CallActivity, "Process_1", new Bounds(400, 100, 100, 80)));

        Assert.False(result.IsApplied);
        Assert.Equal("CT-010", Assert.Single(result.Rejections).Code);
        Assert.DoesNotContain(result.Document.FlowNodes, node => node.Id == "Call_1");
    }

    [Fact]
    public void RefusesACallActivityPointedAtSomethingUncallable()
    {
        var result = BpmnDocumentEditor.Apply(
            WithGlobalDefinition(),
            new CreateElementCommand(
                "Call_1",
                BpmnFlowNodeType.CallActivity,
                "Process_1",
                new Bounds(400, 100, 100, 80),
                CalledElementRef: "Event_1"));

        Assert.False(result.IsApplied);
        Assert.Equal("CT-010", Assert.Single(result.Rejections).Code);
    }

    [Fact]
    public void RefusesACalledElementOnSomethingThatIsNotACallActivity()
    {
        var result = BpmnDocumentEditor.Apply(
            WithGlobalDefinition(),
            new CreateElementCommand(
                "Task_new",
                BpmnFlowNodeType.UserTask,
                "Process_1",
                new Bounds(400, 100, 100, 80),
                CalledElementRef: "Reusable_1"));

        Assert.False(result.IsApplied);
        Assert.Equal("CT-010", Assert.Single(result.Rejections).Code);
    }

    [Fact]
    public void OffersTheDocumentsCallableDefinitions()
    {
        var callable = BpmnDocumentEditor.CallableDefinitions(WithGlobalDefinition());

        Assert.Contains(callable, candidate => candidate.Id == "Reusable_1");
        Assert.Contains(callable, candidate => candidate.Id == "Process_1");
        Assert.DoesNotContain(callable, candidate => candidate.Id == "Event_1");
    }

    [Fact]
    public void ARoundTripKeepsTheCalledElementAndTheDataDefinition()
    {
        var document = BpmnDocumentEditor.Apply(
            WithGlobalDefinition(),
            new CreateElementCommand("Call_1", BpmnFlowNodeType.CallActivity, "Process_1", new Bounds(400, 100, 100, 80), "Call", CalledElementRef: "Reusable_1")).Document;

        document = BpmnDocumentEditor.Apply(
            document,
            new CreateDataReferenceCommand("DataRef_1", BpmnDataKind.DataObject, "Process_1", new Bounds(400, 240, 36, 50), "Order", NewDefinitionId: "Data_1")).Document;

        var xml = Hasseware.AspNetCore.Components.Bpmn.Serialization.BpmnXmlWriter.Write(document).Xml;
        var reread = Hasseware.AspNetCore.Components.Bpmn.Serialization.BpmnXmlReader.Read(xml).Document;

        Assert.Equal("Reusable_1", reread.FlowNodes.Single(node => node.Id == "Call_1").CalledElementRef);
        Assert.Equal("Data_1", reread.DataObjectReferences.Single().DataObjectRef);
    }

    // --- fixtures -------------------------------------------------------------

    private static BpmnDocument Process() => new BpmnDocumentBuilder("Process_1")
        .Node("Event_1", BpmnFlowNodeType.StartEvent, new Bounds(80, 120, 36, 36), "Start")
        .Build();

    private static BpmnDocument Pooled() => new BpmnDocumentBuilder("Process_1")
        .Pool("Participant_1", "Customer", new Bounds(0, 0, 600, 250))
        .Node("Task_1", BpmnFlowNodeType.UserTask, new Bounds(220, 100, 100, 80), "Work")
        .Build();

    /// <summary>A document with a second Process that a Call Activity may invoke.</summary>
    private static BpmnDocument WithGlobalDefinition()
    {
        var document = Process();

        return document with
        {
            Processes = [.. document.Processes, new BpmnProcess("Reusable_1", "Reusable", IsExecutable: false)],
        };
    }
}
