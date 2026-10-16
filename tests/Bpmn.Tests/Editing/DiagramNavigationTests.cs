using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Serialization;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Editing;

/// <summary>
/// Moving between the diagrams one document declares (implementation plan M6).
/// </summary>
/// <remarks>
/// A BPMN file may draw several things, and a Call Activity's content is a different Process
/// altogether — sometimes one the same file draws, sometimes one it only names. Which of those is
/// true decides whether a drill-down is offered, so it is answered against the document rather than
/// guessed at on the canvas.
/// </remarks>
public sealed class DiagramNavigationTests
{
    [Fact]
    public void ListsEveryDiagramWithSomethingToCallIt()
    {
        var document = Sample("linked-processes.bpmn");

        var diagrams = BpmnDiagramNavigation.Diagrams(document);

        Assert.Equal(
            [("Diagram_Main", "Main Process Diagram"), ("Diagram_HandleShipment", "Handle Shipment Diagram")],
            diagrams.Select(target => (target.DiagramId, target.Label)));

        // Both draw a Process the file declares at the top level, so neither is a nested plane.
        Assert.All(diagrams, target => Assert.True(target.IsRoot));
    }

    [Fact]
    public void NamesADiagramAfterWhatItDrawsWhenItHasNoNameOfItsOwn()
    {
        // A picker listing four generated ids tells the user nothing about which plane is which.
        var document = Sample("nested-subprocesses.bpmn");

        var labels = BpmnDiagramNavigation.Diagrams(document).Select(target => target.Label);

        Assert.DoesNotContain(labels, label => label.StartsWith("sid-", StringComparison.Ordinal));
    }

    [Fact]
    public void MarksAPlaneOverASubProcessAsNested()
    {
        // Those planes are the inside of something the user drills into, not diagrams in their own
        // right, and a picker that lists them alongside the root reads as six unrelated files.
        var document = Sample("nested-subprocesses.bpmn");

        Assert.Contains(BpmnDiagramNavigation.Diagrams(document), target => !target.IsRoot);
    }

    // --- drilling in ------------------------------------------------------------------

    [Fact]
    public void ACallActivityOpensTheDiagramOfWhatItCalls()
    {
        var document = Sample("linked-processes.bpmn");

        var target = BpmnDiagramNavigation.DrillTargetFor(document, "CallActivity_HandleShipment");

        Assert.True(target.IsAvailable);
        Assert.Equal("Diagram_HandleShipment", target.DiagramId);
        Assert.Equal(BpmnDrillRefusal.None, target.Refusal);
    }

    [Fact]
    public void ACallActivityWhoseProcessIsNotDrawnSaysSo()
    {
        /*
         * The exit criterion for this milestone: drill-down is "disabled with a reason when no
         * local diagram exists". A Call Activity may name a Process in another file entirely, which
         * is legal and common, and a button that simply does nothing tells the user it is broken.
         */
        var document = Sample("linked-processes.bpmn");
        var stripped = document with
        {
            Diagrams = [.. document.Diagrams.Where(diagram => diagram.Id != "Diagram_HandleShipment")],
        };

        var target = BpmnDiagramNavigation.DrillTargetFor(stripped, "CallActivity_HandleShipment");

        Assert.False(target.IsAvailable);
        Assert.Equal(BpmnDrillRefusal.NoLocalDiagram, target.Refusal);
        Assert.Contains("not drawn in this file", target.Reason!, StringComparison.Ordinal);
    }

    [Fact]
    public void ACallActivityThatCallsNothingIsADifferentRefusal()
    {
        // Different to a user: this one is an incomplete model, not a file that lives elsewhere.
        // The builder always names something, because the model requires a Call Activity to either
        // call an element or own its own content; this is the document with neither.
        var built = new BpmnDocumentBuilder("Process_1")
            .Node("Call_1", BpmnFlowNodeType.CallActivity, new Model.Di.Bounds(100, 100, 100, 80))
            .Build();

        var document = built with
        {
            FlowNodes = [.. built.FlowNodes.Select(node => node with { CalledElementRef = null })],
        };

        var target = BpmnDiagramNavigation.DrillTargetFor(document, "Call_1");

        Assert.Equal(BpmnDrillRefusal.NothingCalled, target.Refusal);
        Assert.Contains("does not say what it calls", target.Reason!, StringComparison.Ordinal);
    }

    [Fact]
    public void AnOrdinaryElementDrillsNowhere()
    {
        var document = Sample("linked-processes.bpmn");

        var target = BpmnDiagramNavigation.DrillTargetFor(document, "Event_MainStart");

        Assert.Equal(BpmnDrillRefusal.NotDrillable, target.Refusal);
        Assert.False(target.IsAvailable);
    }

    [Fact]
    public void ACollapsedSubProcessOpensItsOwnPlane()
    {
        var document = Sample("nested-subprocesses.bpmn");

        // Whichever Sub-Process this file draws a plane for.
        var drillable = document.FlowNodes
            .Where(node => node.Type == BpmnFlowNodeType.SubProcess)
            .Select(node => (node.Id, Target: BpmnDiagramNavigation.DrillTargetFor(document, node.Id)))
            .FirstOrDefault(pair => pair.Target.IsAvailable);

        Assert.NotNull(drillable.Id);
        Assert.True(drillable.Target.IsAvailable, "no Sub-Process in the fixture has a plane of its own");
    }

    [Fact]
    public void AnElementThatIsNotThereDrillsNowhereRatherThanThrowing()
    {
        var target = BpmnDiagramNavigation.DrillTargetFor(Sample("linked-processes.bpmn"), "Nope_1");

        Assert.Equal(BpmnDrillRefusal.NotDrillable, target.Refusal);
    }

    // --- finding where something is drawn ----------------------------------------------

    [Fact]
    public void FindsThePlaneAnElementIsDrawnOn()
    {
        // What a reference browser needs: selecting an element on another plane without going there
        // first selects something nobody can see.
        var document = Sample("linked-processes.bpmn");

        Assert.Equal("Diagram_Main", BpmnDiagramNavigation.DiagramShowing(document, "CallActivity_HandleShipment"));
        Assert.Null(BpmnDiagramNavigation.DiagramShowing(document, "Nope_1"));
    }

    // --- across planes ------------------------------------------------------------------

    [Fact]
    public void RenamingAProcessMovesTheCallAndThePlaneThatDrawsIt()
    {
        /*
         * Cross-diagram integrity, which is this milestone's other exit criterion. Process_
         * HandleShipment is named twice in different roles — once by the Call Activity that invokes
         * it and once by the plane that draws it — and a rename that moved only one would leave
         * either a Call Activity calling nothing or a diagram drawing nothing.
         */
        var document = Sample("linked-processes.bpmn");

        var renamed = BpmnDocumentEditor
            .Apply(document, new RenameElementCommand("Process_HandleShipment", "Process_Renamed"))
            .Document;

        Assert.Equal(
            "Process_Renamed",
            renamed.FindFlowNode("CallActivity_HandleShipment")!.CalledElementRef);

        Assert.Equal(
            "Process_Renamed",
            renamed.Diagrams.Single(diagram => diagram.Id == "Diagram_HandleShipment").Plane.BpmnElement);

        // And the drill-down still lands somewhere afterwards.
        Assert.True(BpmnDiagramNavigation.DrillTargetFor(renamed, "CallActivity_HandleShipment").IsAvailable);
    }

    private static BpmnDocument Sample(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "playground")))
        {
            directory = directory.Parent;
        }

        var path = Path.Combine(
            directory?.FullName ?? throw new InvalidOperationException("the repository root was not found"),
            "playground",
            "Bpmn.Demo",
            "wwwroot",
            "samples",
            fileName);

        return BpmnXmlReader.Read(File.ReadAllText(path)).Document;
    }
}
