using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Editing;

public sealed class DiagramCreationTests
{
    [Theory]
    [InlineData("flow")]
    [InlineData("annotation")]
    [InlineData("group")]
    [InlineData("object")]
    [InlineData("store")]
    public void CreatesSemanticElementAndShapeOnRequestedDiagram(string kind)
    {
        var document = Document();
        var result = BpmnDocumentEditor.Apply(document, Command(kind, "Process_2", "Diagram_2"));

        Assert.True(result.IsApplied);
        Assert.Same(document.Diagrams[0], result.Document.Diagrams[0]);
        Assert.NotNull(result.Document.Find("New"));
        Assert.Equal("New", Assert.Single(result.Document.Diagrams[1].Plane.Shapes).BpmnElement);
    }

    [Theory]
    [InlineData("flow")]
    [InlineData("annotation")]
    [InlineData("group")]
    [InlineData("object")]
    [InlineData("store")]
    public void MissingOrIncompatibleDiagramRejectsAtomically(string kind)
    {
        var document = Document();
        foreach (var diagramId in new[] { "Ghost", "Diagram_1" })
        {
            var result = BpmnDocumentEditor.Apply(document, Command(kind, "Process_2", diagramId));
            Assert.False(result.IsApplied);
            Assert.Same(document, result.Document);
            Assert.Equal("CT-001", Assert.Single(result.Rejections).Code);
        }
    }

    [Fact]
    public void OmittedDestinationKeepsFirstDiagramBehavior()
    {
        var document = Document();
        var result = BpmnDocumentEditor.Apply(document, new CreateElementCommand(
            "New", BpmnFlowNodeType.Task, "Process_1", new Bounds(400, 400, 100, 80)));

        Assert.True(result.IsApplied);
        Assert.Contains(result.Document.Diagrams[0].Plane.Shapes, shape => shape.BpmnElement == "New");
        Assert.Same(document.Diagrams[1], result.Document.Diagrams[1]);
    }

    [Fact]
    public void OtherViewsOfSameProcessDoNotCauseFalseCollisions()
    {
        var document = Document();
        document = document with
        {
            Diagrams = [document.Diagrams[0], document.Diagrams[1] with
            {
                Plane = document.Diagrams[1].Plane with { BpmnElement = "Process_1" },
            }],
        };
        var result = BpmnDocumentEditor.Apply(document, Command("flow", "Process_1", "Diagram_2"));
        Assert.True(result.IsApplied);

        var colliding = BpmnDocumentEditor.Apply(result.Document, new CreateElementCommand(
            "Collision", BpmnFlowNodeType.Task, "Process_1", new Bounds(100, 100, 100, 80))
        { DiagramId = "Diagram_2" });
        Assert.False(colliding.IsApplied);
        Assert.Equal("DI-011", Assert.Single(colliding.Rejections).Code);
    }

    [Fact]
    public void BoundaryHostIsResolvedOnlyOnRequestedDiagram()
    {
        var document = Document();
        Assert.Null(BpmnBoundaryPlacement.Resolve(document, new Bounds(140, 125, 36, 36), "Diagram_2"));
        Assert.Equal("Existing", BpmnBoundaryPlacement.Resolve(document, new Bounds(140, 125, 36, 36), "Diagram_1")?.HostId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PoolsAndLanesTargetRequestedCollaborationView(bool lane)
    {
        var document = new BpmnDocumentBuilder()
            .Pool("Pool", "Pool", new Bounds(0, 0, 600, 400))
            .Lane("Lane", "Lane", new Bounds(30, 0, 570, 400), [])
            .Build();
        var first = document.Diagrams[0];
        document = document with
        {
            Diagrams = [first, new BpmnDiagram("Diagram_2", first.Plane with
            {
                Id = "Plane_2",
                Shapes = [.. first.Plane.Shapes.Select(shape => shape with { Id = shape.Id + "_2" })],
            })],
        };
        BpmnEditCommand command = lane
            ? new CreateLaneCommand("New", "Process_1", "New") { DiagramId = "Diagram_2" }
            : new CreateParticipantCommand("New", new Bounds(700, 0, 600, 400)) { DiagramId = "Diagram_2" };

        var result = BpmnDocumentEditor.Apply(document, command);

        Assert.True(result.IsApplied);
        Assert.Same(first, result.Document.Diagrams[0]);
        Assert.Contains(result.Document.Diagrams[1].Plane.Shapes, shape => shape.BpmnElement == "New");
        if (lane)
        {
            Assert.Equal(400, Assert.Single(first.Plane.Shapes, shape => shape.BpmnElement == "Lane").Bounds.Height);
            var lanes = result.Document.Diagrams[1].Plane.Shapes.Where(shape => shape.BpmnElement != "Pool").ToArray();
            Assert.Equal(266.67, Assert.Single(lanes, shape => shape.BpmnElement == "Lane").Bounds.Height, 2);
            Assert.Equal(400, lanes.Sum(shape => shape.Bounds.Height));
        }
    }

    private static BpmnEditCommand Command(string kind, string containerId, string? diagramId) => kind switch
    {
        "flow" => new CreateElementCommand("New", BpmnFlowNodeType.Task, containerId, new Bounds(100, 100, 100, 80))
        { DiagramId = diagramId },
        "annotation" or "group" => new CreateArtifactCommand("New",
            kind == "group" ? BpmnArtifactKind.Group : BpmnArtifactKind.TextAnnotation,
            containerId, new Bounds(100, 100, 100, 80))
        { DiagramId = diagramId },
        _ => new CreateDataReferenceCommand("New",
            kind == "object" ? BpmnDataKind.DataObject : BpmnDataKind.DataStore,
            containerId, new Bounds(100, 100, 36, 50), NewDefinitionId: "Definition")
        { DiagramId = diagramId },
    };

    private static BpmnDocument Document()
    {
        var document = new BpmnDocumentBuilder()
            .Process("Process_2")
            .Node("Existing", BpmnFlowNodeType.Task, new Bounds(100, 100, 100, 80))
            .Build();
        return document with
        {
            Diagrams = [.. document.Diagrams, new BpmnDiagram("Diagram_2", new BpmnPlane("Plane_2", "Process_2", [], []))],
        };
    }
}
