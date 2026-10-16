using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;
using Hasseware.AspNetCore.Components.Bpmn.Serialization;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Editing;

/// <summary>
/// A collapsed Sub-Process is an opaque box (specification section 2.7).
/// </summary>
/// <remarks>
/// Its children are drawn on a plane of their own, with their own origin. The containment and
/// auto-grow rules compared them with the box on the parent plane, so resizing the collapsed box
/// was refused because content "would be left outside" it, and moving a child while drilled in could
/// have grown the box far beyond the diagram. Found by resizing a Sub-Process in the demo's
/// <c>nested-subprocesses.bpmn</c>.
/// </remarks>
public sealed class CollapsedContainerEditTests
{
    private const string Collapsed = "collapsedProcess";

    [Fact]
    public void ACollapsedSubProcessCanBeGrown()
    {
        var document = Sample();
        var box = BoundsOf(document, Collapsed);

        var result = BpmnDocumentEditor.Apply(
            document,
            new ResizeShapeCommand("collapsedProcess_gui", box with { Height = box.Height + 40 }),
            EditingOptions.Default);

        Assert.True(result.IsApplied, string.Join("; ", result.Rejections.Select(rejection => rejection.Message)));
        Assert.Equal(box.Height + 40, BoundsOf(result.Document, Collapsed).Height);
    }

    [Fact]
    public void ACollapsedSubProcessCanBeShrunkWhateverItsHiddenChildrenAre()
    {
        var document = Sample();
        var box = BoundsOf(document, Collapsed);

        var result = BpmnDocumentEditor.Apply(
            document,
            new ResizeShapeCommand("collapsedProcess_gui", box with { Width = box.Width - 20 }),
            EditingOptions.Default);

        Assert.True(result.IsApplied, string.Join("; ", result.Rejections.Select(rejection => rejection.Message)));
    }

    [Fact]
    public void MovingAChildOnTheDrilledInPlaneLeavesTheCollapsedBoxAlone()
    {
        var document = Sample();
        var before = BoundsOf(document, Collapsed);

        var child = document.FlowNodes.First(node => node.ContainerId == Collapsed && !node.FlowElementsOwned);
        var shape = document.Diagrams
            .SelectMany(diagram => diagram.Plane.Shapes)
            .First(candidate => candidate.BpmnElement == child.Id);

        var result = BpmnDocumentEditor.Apply(
            document,
            new MoveShapesCommand([new ShapeGeometry(shape.Id, shape.Bounds with { X = shape.Bounds.X + 2, Y = shape.Bounds.Y + 2 })]),
            EditingOptions.Default);

        Assert.True(result.IsApplied);
        Assert.Equal(before, BoundsOf(result.Document, Collapsed));
    }

    private static Bounds BoundsOf(BpmnDocument document, string elementId) => document.Diagrams
        .SelectMany(diagram => diagram.Plane.Shapes)
        .First(shape => shape.BpmnElement == elementId)
        .Bounds;

    private static BpmnDocument Sample()
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
            "nested-subprocesses.bpmn");

        return BpmnXmlReader.Read(File.ReadAllText(path)).Document;
    }
}
