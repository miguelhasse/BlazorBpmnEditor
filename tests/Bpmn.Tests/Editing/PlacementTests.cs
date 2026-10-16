using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Editing;

/// <summary>
/// Placement finds room for a new element without coordinates from the caller, and what it finds passes
/// the editor's own creation check (design doc, "Tool catalog (v1)").
/// </summary>
public sealed class PlacementTests
{
    private static readonly Bounds Start = new(100, 132, 36, 36);

    // --- beside an anchor --------------------------------------------------------------------

    [Fact]
    public void PlacesToTheRightOfTheAnchorCentredOnIt()
    {
        var document = new BpmnDocumentBuilder().Node("s", BpmnFlowNodeType.StartEvent, Start).Build();

        var placed = BpmnPlacement.NextTo(document, "s", BpmnFlowNodeType.Task);

        Assert.True(placed.Succeeded);
        Assert.Equal(new Bounds(Start.X + Start.Width + BpmnPlacement.Gap, Start.Y + (Start.Height - 80) / 2, 100, 80), placed.Bounds);
        Assert.Equal("Process_1", placed.ContainerId);
        Assert.NotNull(placed.DiagramId);
    }

    [Theory]
    [InlineData(BpmnPlacementDirection.Below)]
    [InlineData(BpmnPlacementDirection.Left)]
    [InlineData(BpmnPlacementDirection.Above)]
    public void PlacesOnTheRequestedSide(BpmnPlacementDirection direction)
    {
        var document = new BpmnDocumentBuilder().Node("s", BpmnFlowNodeType.StartEvent, Start).Build();

        var bounds = BpmnPlacement.NextTo(document, "s", BpmnFlowNodeType.Task, direction).Bounds!.Value;

        switch (direction)
        {
            case BpmnPlacementDirection.Below: Assert.Equal(Start.Y + Start.Height + BpmnPlacement.Gap, bounds.Y); break;
            case BpmnPlacementDirection.Left: Assert.Equal(Start.X - BpmnPlacement.Gap - bounds.Width, bounds.X); break;
            default: Assert.Equal(Start.Y - BpmnPlacement.Gap - bounds.Height, bounds.Y); break;
        }
    }

    [Fact]
    public void MovesClearOfAnElementAlreadyThere()
    {
        var occupied = new Bounds(Start.X + Start.Width + BpmnPlacement.Gap, 110, 100, 80);
        var document = new BpmnDocumentBuilder()
            .Node("s", BpmnFlowNodeType.StartEvent, Start)
            .Node("t", BpmnFlowNodeType.Task, occupied)
            .Build();

        var bounds = BpmnPlacement.NextTo(document, "s", BpmnFlowNodeType.Task).Bounds!.Value;

        Assert.False(Overlap(bounds, occupied));
        Assert.True(bounds.X >= Start.X + Start.Width, "stays on the requested side");
    }

    [Fact]
    public void EveryPlacementPassesTheEditorsCreationCheck()
    {
        var options = EditingOptions.Default with { CollisionClearance = 15 };
        var document = new BpmnDocumentBuilder().Node("s", BpmnFlowNodeType.StartEvent, Start).Build();

        // Five tasks beside the same anchor must fan out, not stack on one another.
        for (var i = 0; i < 5; i++)
        {
            var placed = BpmnPlacement.NextTo(document, "s", BpmnFlowNodeType.Task, options: options);
            var result = BpmnDocumentEditor.Apply(
                document,
                new CreateElementCommand($"T{i}", BpmnFlowNodeType.Task, placed.ContainerId!, placed.Bounds!.Value) { DiagramId = placed.DiagramId },
                options);

            Assert.True(result.IsApplied, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
            document = result.Document;
        }
    }

    [Fact]
    public void PlacesBesideABoundaryEventInItsHostsContainer()
    {
        var document = new BpmnDocumentBuilder()
            .Node("task", BpmnFlowNodeType.Task, new Bounds(100, 100, 100, 80))
            .Node("b", BpmnFlowNodeType.BoundaryEvent, new Bounds(132, 162, 36, 36), attachedToRef: "task")
            .Build();

        var placed = BpmnPlacement.NextTo(document, "b", BpmnFlowNodeType.Task, BpmnPlacementDirection.Below);

        Assert.True(placed.Succeeded);
        Assert.Equal("Process_1", placed.ContainerId);
        Assert.False(Overlap(placed.Bounds!.Value, new Bounds(100, 100, 100, 80)));
    }

    [Fact]
    public void RefusesAnAnchorThatIsNotThereOrNotDrawn()
    {
        var document = new BpmnDocumentBuilder().Node("s", BpmnFlowNodeType.StartEvent, Start).Build();
        var undrawn = document with
        {
            Diagrams = [.. document.Diagrams.Select(diagram => diagram with { Plane = diagram.Plane with { Shapes = [] } })],
        };

        var missing = BpmnPlacement.NextTo(document, "nope", BpmnFlowNodeType.Task);
        var hidden = BpmnPlacement.NextTo(undrawn, "s", BpmnFlowNodeType.Task);

        Assert.Equal("PL-001", missing.Problem?.Code);
        Assert.Equal("PL-001", hidden.Problem?.Code);
        Assert.False(hidden.Succeeded);
    }

    [Fact]
    public void StaysOffAnExpandedSubProcessThatTheEditorWouldAllowItOn()
    {
        // The editor exempts containers from its overlap check, so only placement keeps clear of them.
        var sub = new Bounds(200, 60, 350, 200);
        var document = new BpmnDocumentBuilder()
            .Node("s", BpmnFlowNodeType.StartEvent, Start)
            .Node("sub", BpmnFlowNodeType.SubProcess, sub, isExpanded: true)
            .Build();

        var bounds = BpmnPlacement.NextTo(document, "s", BpmnFlowNodeType.Task).Bounds!.Value;

        Assert.False(Overlap(bounds, sub), $"{bounds} lies on the sub-process {sub}");
    }

    [Fact]
    public void StaysOutOfAnotherPool()
    {
        var other = new Bounds(0, 250, 600, 200);
        var document = new BpmnDocumentBuilder()
            .Process("Process_2")
            .Pool("PoolA", "Customer", new Bounds(0, 0, 600, 200), processRef: "Process_1")
            .Pool("PoolB", "Shop", other, processRef: "Process_2")
            .Node("ask", BpmnFlowNodeType.Task, new Bounds(100, 100, 100, 60), poolId: "PoolA")
            .Build();

        // Fifty units below the anchor is inside the other pool.
        var bounds = BpmnPlacement.NextTo(document, "ask", BpmnFlowNodeType.Task, BpmnPlacementDirection.Below).Bounds!.Value;

        Assert.False(Overlap(bounds, other), $"{bounds} lies in the other pool {other}");
    }

    [Fact]
    public void StillPlacesInsideASubProcessThatItBelongsTo()
    {
        var sub = new Bounds(100, 100, 400, 250);
        var document = new BpmnDocumentBuilder()
            .Node("sub", BpmnFlowNodeType.SubProcess, sub, isExpanded: true)
            .Node("inner", BpmnFlowNodeType.Task, new Bounds(120, 140, 100, 80), containerId: "sub")
            .Build();

        var placed = BpmnPlacement.NextTo(document, "inner", BpmnFlowNodeType.Task);

        Assert.True(placed.Succeeded, placed.Problem?.Message);
        Assert.Equal("sub", placed.ContainerId);
    }

    // --- inside a container ------------------------------------------------------------------

    [Fact]
    public void PlacesInsideAnExpandedSubProcessClearOfItsContent()
    {
        var sub = new Bounds(100, 100, 400, 250);
        var document = new BpmnDocumentBuilder()
            .Node("sub", BpmnFlowNodeType.SubProcess, sub, isExpanded: true)
            .Node("inner", BpmnFlowNodeType.Task, new Bounds(120, 140, 100, 80), containerId: "sub")
            .Build();

        var placed = BpmnPlacement.Inside(document, "sub", BpmnFlowNodeType.Task);

        Assert.Equal("sub", placed.ContainerId);
        var bounds = placed.Bounds!.Value;
        Assert.True(bounds.X >= sub.X && bounds.X + bounds.Width <= sub.X + sub.Width, $"inside horizontally: {bounds}");
        Assert.True(bounds.Y >= sub.Y && bounds.Y + bounds.Height <= sub.Y + sub.Height, $"inside vertically: {bounds}");
        Assert.False(Overlap(bounds, new Bounds(120, 140, 100, 80)));
    }

    [Fact]
    public void PlacesInsideAPoolPastItsHeader()
    {
        var pool = new Bounds(0, 0, 600, 250);
        var document = new BpmnDocumentBuilder().Pool("pool", "Pool", pool).Build();

        var placed = BpmnPlacement.Inside(document, "Process_1", BpmnFlowNodeType.Task);

        Assert.Equal("pool", placed.PoolId);
        Assert.True(placed.Bounds!.Value.X >= pool.X + 30, $"clear of the header: {placed.Bounds}");
    }

    [Fact]
    public void ContinuesAnOpenPlaneToTheRight()
    {
        var document = new BpmnDocumentBuilder()
            .Node("a", BpmnFlowNodeType.Task, new Bounds(100, 100, 100, 80))
            .Node("b", BpmnFlowNodeType.Task, new Bounds(300, 100, 100, 80))
            .Build();

        var placed = BpmnPlacement.Inside(document, "Process_1", BpmnFlowNodeType.Task);

        Assert.Equal(300 + 100 + BpmnPlacement.Gap, placed.Bounds!.Value.X);
    }

    [Fact]
    public void RefusesSomethingThatCannotHoldFlowNodes()
    {
        var document = new BpmnDocumentBuilder()
            .Node("task", BpmnFlowNodeType.Task, new Bounds(100, 100, 100, 80))
            .Node("collapsed", BpmnFlowNodeType.SubProcess, new Bounds(300, 100, 100, 80), isExpanded: false)
            .Build();

        Assert.Equal("PL-002", BpmnPlacement.Inside(document, "task", BpmnFlowNodeType.Task).Problem?.Code);
        Assert.Equal("PL-002", BpmnPlacement.Inside(document, "collapsed", BpmnFlowNodeType.Task).Problem?.Code);
        Assert.Equal("PL-002", BpmnPlacement.Inside(document, "nope", BpmnFlowNodeType.Task).Problem?.Code);
    }

    [Theory]
    [InlineData(BpmnFlowNodeType.StartEvent, 36, 36)]
    [InlineData(BpmnFlowNodeType.ParallelGateway, 50, 50)]
    [InlineData(BpmnFlowNodeType.UserTask, 100, 80)]
    [InlineData(BpmnFlowNodeType.SubProcess, 350, 200)]
    public void UsesTheCanvasDefaultSizes(BpmnFlowNodeType type, double width, double height)
    {
        Assert.Equal((width, height), BpmnPlacement.DefaultSize(type));
    }

    private static bool Overlap(Bounds a, Bounds b) =>
        a.X < b.X + b.Width && b.X < a.X + a.Width && a.Y < b.Y + b.Height && b.Y < a.Y + a.Height;
}
