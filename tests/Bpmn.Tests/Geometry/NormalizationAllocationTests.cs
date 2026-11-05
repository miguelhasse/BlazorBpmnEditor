using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;
using Hasseware.AspNetCore.Components.Bpmn.Model.Geometry;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Geometry;

public sealed class NormalizationAllocationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NormalizationPreservesHostWaypointLists(bool displaced)
    {
        var route = new List<DiPoint>
        {
            new(displaced ? 105 : 100, 40), new(150, 40), new(200, 40),
        };
        var original = route.ToArray();
        var document = Document(route);
        var moved = new List<MovedConnector>();

        var result = ConnectorEndpoints.Normalize(document, moved);

        Assert.Equal(original, route);
        Assert.Equal(new DiPoint(100, 40), result.Diagrams[0].Plane.Edges[0].Waypoints[0]);
        Assert.Same(result, ConnectorEndpoints.Normalize(result));
        if (displaced)
        {
            Assert.NotSame(document, result);
            Assert.Equal(new MovedConnector("flow", 5), Assert.Single(moved));
        }
        else
        {
            Assert.Same(document, result);
            Assert.Same(route, result.Diagrams[0].Plane.Edges[0].Waypoints);
            Assert.Empty(moved);
        }
    }

    [Fact]
    public void DuplicateShapeElementsKeepTheFirstOutline()
    {
        var document = Document([new(105, 40), new(200, 40)]);
        var diagram = document.Diagrams[0];
        document = document with
        {
            Diagrams = [diagram with
            {
                Plane = diagram.Plane with
                {
                    Shapes = [.. diagram.Plane.Shapes, diagram.Plane.Shapes[0] with
                    { Id = "duplicate", Bounds = new Bounds(500, 500, 200, 200) }],
                },
            }],
        };

        var result = ConnectorEndpoints.Normalize(document);

        Assert.Equal(new DiPoint(100, 40), result.Diagrams[0].Plane.Edges[0].Waypoints[0]);
    }

    [Fact]
    public void DuplicateShapeIdsAcrossDiagramsKeepTheFirstOldBounds()
    {
        var document = Document([new(100, 40), new(200, 40)]);
        var diagram = document.Diagrams[0];
        document = document with
        {
            Diagrams = [diagram, diagram with
            {
                Id = "second",
                Plane = diagram.Plane with
                {
                    Shapes = [diagram.Plane.Shapes[0] with { Bounds = new Bounds(0, 0, 100, 160) }],
                    Edges = [],
                },
            }],
        };
        var after = document with
        {
            Diagrams = [diagram with
            {
                Plane = diagram.Plane with
                {
                    Shapes = [diagram.Plane.Shapes[0] with { Bounds = new Bounds(0, 0, 100, 160) }, diagram.Plane.Shapes[1]],
                },
            }, document.Diagrams[1]],
        };

        var result = ConnectorEndpoints.FollowResizedShapes(document, after);

        Assert.Equal(new DiPoint(100, 80), result.Diagrams[0].Plane.Edges[0].Waypoints[0]);
        Assert.Equal(new DiPoint(100, 40), document.Diagrams[0].Plane.Edges[0].Waypoints[0]);
    }

    [Fact]
    public void DuplicateFlowEdgesKeepTheFirstAttachmentRoute()
    {
        var document = new BpmnDocumentBuilder()
            .Node("a", BpmnFlowNodeType.Task, new Bounds(0, 0, 100, 80))
            .Node("b", BpmnFlowNodeType.Task, new Bounds(200, 0, 100, 80))
            .Flow("flow", "a", "b", [new(100, 40), new(200, 40)])
            .TextAnnotation("note", "Note", new Bounds(120, 120, 80, 40))
            .Association("association", "flow", "note", [new(150, 40), new(150, 120)])
            .Build();
        var diagram = document.Diagrams[0];
        document = document with
        {
            Diagrams = [diagram with
            {
                Plane = diagram.Plane with
                {
                    Edges = [.. diagram.Plane.Edges, diagram.Plane.Edges[0] with
                    { Id = "duplicate", Waypoints = [new(100, 240), new(200, 240)] }],
                },
            }],
        };

        var result = ConnectorEndpoints.FollowFlowAttachments(document);

        Assert.Equal(new DiPoint(150, 40), result.Diagrams[0].Plane.Edges[1].Waypoints[0]);
    }

    [Fact]
    public void UnchangedLongRoutesStayWithinAllocationBudget()
    {
        var builder = new BpmnDocumentBuilder();
        for (var index = 0; index < 600; index++)
        {
            builder.Node($"task{index}", BpmnFlowNodeType.Task, new Bounds(index * 200, 0, 100, 80));
            if (index > 0)
                builder.Flow($"flow{index}", $"task{index - 1}", $"task{index}",
                    [.. Enumerable.Range(0, 32).Select(point => new DiPoint(
                        (index - 1) * 200 + 100 + (100d * point / 31), 40))]);
        }
        var document = builder.Build();
        for (var warm = 0; warm < 3; warm++) ConnectorEndpoints.Normalize(document);
        var before = GC.GetAllocatedBytesForCurrentThread();

        var result = ConnectorEndpoints.Normalize(document);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Same(document, result);
        Assert.InRange(allocated, 0, 100_000);
    }

    private static BpmnDocument Document(IReadOnlyList<DiPoint> route) => new BpmnDocumentBuilder()
        .Node("a", BpmnFlowNodeType.Task, new Bounds(0, 0, 100, 80))
        .Node("b", BpmnFlowNodeType.Task, new Bounds(200, 0, 100, 80))
        .Flow("flow", "a", "b", route).Build();
}
