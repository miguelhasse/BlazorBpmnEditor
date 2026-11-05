using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;
using Hasseware.AspNetCore.Components.Bpmn.Model.Geometry;
using Hasseware.AspNetCore.Components.Bpmn.Serialization;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Geometry;

public sealed class AnnotationAttachmentLookupTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void IndexedShapeFollowingMatchesScanAndUsesTranslatedNotes(bool reverse, bool changed)
    {
        var before = Document(reverse);
        var diagram = before.Diagrams[0];
        var after = !changed ? before : before with
        {
            Diagrams = [diagram with
            {
                Plane = diagram.Plane with
                {
                    Shapes = [diagram.Plane.Shapes[0] with { Bounds = new Bounds(10, 0, 100, 80) }, .. diagram.Plane.Shapes.Skip(1)],
                },
            }],
        };
        var expected = Model.Geometry.ConnectorEndpoints.FollowAnnotationShapes(before, after);

        var actual = Model.Geometry.ConnectorEndpoints.FollowAnnotationShapes(Indexed(before), Indexed(after));

        Assert.Equal(BpmnJsonWriter.Write(expected), BpmnJsonWriter.Write(WithoutPadding(actual)));
        Assert.Equal(new Bounds(0, 0, 100, 80), before.Diagrams[0].Plane.Shapes[0].Bounds);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void IndexedFlowFollowingMatchesScanForSharedRoundedPaths(bool reverse, bool explicitlyPositioned)
    {
        var before = Document(reverse, flow: true);
        var diagram = before.Diagrams[0];
        var after = before with
        {
            Diagrams = [diagram with
            {
                Plane = diagram.Plane with
                {
                    Edges = [diagram.Plane.Edges[0] with
                    { Waypoints = [new(100, 40), new(130, 40), new(130, 120), new(170, 120), new(170, 40), new(200, 40)] },
                        .. diagram.Plane.Edges.Skip(1)],
                },
            }],
        };
        HashSet<string>? positioned = explicitlyPositioned ? new(StringComparer.Ordinal) { "note0_di" } : null;
        var expectedMoved = new List<Model.Geometry.MovedConnector>();
        var expected = Model.Geometry.ConnectorEndpoints.FollowFlowAttachments(after, before, expectedMoved, positioned);
        var actualMoved = new List<Model.Geometry.MovedConnector>();

        var actual = Model.Geometry.ConnectorEndpoints.FollowFlowAttachments(Indexed(after), Indexed(before), actualMoved, positioned);

        Assert.Equal(BpmnJsonWriter.Write(expected), BpmnJsonWriter.Write(WithoutPadding(actual)));
        Assert.Equal(expectedMoved, actualMoved);
    }

    [Fact]
    public void IndexedFollowingKeepsFirstMatchesAndDiagramBoundaries()
    {
        var before = Document(reverse: false, flow: true);
        var first = before.Diagrams[0];
        var duplicate = first.Plane.Shapes[0] with { Bounds = new Bounds(500, 500, 100, 80) };
        var second = first with
        {
            Id = "second",
            Plane = first.Plane with
            {
                Id = "secondPlane",
                Shapes = [.. first.Plane.Shapes, duplicate],
                Edges = [.. first.Plane.Edges, first.Plane.Edges[0] with
                { Id = "duplicate", Waypoints = [new(100, 300), new(200, 300)] }],
            },
        };
        before = before with { Diagrams = [first, second] };
        var after = before with
        {
            Diagrams = [first with
            {
                Plane = first.Plane with
                {
                    Edges = [first.Plane.Edges[0] with
                    { Waypoints = [new(100, 40), new(150, 40), new(150, 180), new(200, 180), new(200, 40)] },
                        .. first.Plane.Edges.Skip(1)],
                },
            }, second],
        };
        var expected = Model.Geometry.ConnectorEndpoints.FollowFlowAttachments(after, before);

        var actual = Model.Geometry.ConnectorEndpoints.FollowFlowAttachments(Indexed(after), Indexed(before));

        Assert.Equal(BpmnJsonWriter.Write(expected), BpmnJsonWriter.Write(WithoutPadding(actual)));
    }

    [Fact]
    public void SeparateOperationsObserveMutableHostRoutesAndConnections()
    {
        var document = Indexed(Document(reverse: false, flow: true));
        var routes = new List<DiPoint>(document.Diagrams[0].Plane.Edges[0].Waypoints);
        var associations = new List<BpmnAssociation>(document.Associations);
        var diagram = document.Diagrams[0];
        document = document with
        {
            Associations = associations,
            Diagrams = [diagram with
            {
                Plane = diagram.Plane with
                { Edges = [diagram.Plane.Edges[0] with { Waypoints = routes }, .. diagram.Plane.Edges.Skip(1)] },
            }],
        };
        var first = Model.Geometry.ConnectorEndpoints.FollowFlowAttachments(document);
        var firstJson = BpmnJsonWriter.Write(first);
        routes[2] = new(140, 180);
        associations.Add(new("added", "note1", "a", "Process_1"));

        var second = Model.Geometry.ConnectorEndpoints.FollowFlowAttachments(document);

        Assert.NotEqual(firstJson, BpmnJsonWriter.Write(second));
        Assert.Equal(new DiPoint(140, 180), routes[2]);
        var scanDocument = WithoutPadding(document);
        var expected = Model.Geometry.ConnectorEndpoints.FollowFlowAttachments(scanDocument);
        Assert.Equal(BpmnJsonWriter.Write(expected), BpmnJsonWriter.Write(WithoutPadding(second)));
    }

    [Fact]
    public void SharedRoundedPathsStayWithinAllocationBudget()
    {
        var before = Document(reverse: false, flow: true, notes: 60);
        var diagram = before.Diagrams[0];
        var after = before with
        {
            Diagrams = [diagram with
            {
                Plane = diagram.Plane with
                {
                    Edges = [diagram.Plane.Edges[0] with
                    { Waypoints = [new(100, 40), new(130, 40), new(130, 120), new(170, 120), new(170, 40), new(200, 40)] },
                        .. diagram.Plane.Edges.Skip(1)],
                },
            }],
        };
        for (var warm = 0; warm < 3; warm++) ConnectorEndpoints.FollowFlowAttachments(after, before);
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();

        var result = ConnectorEndpoints.FollowFlowAttachments(after, before);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Assert.NotEqual(before.Diagrams[0].Plane.Shapes[2].Bounds, result.Diagrams[0].Plane.Shapes[2].Bounds);
        Assert.InRange(allocated, 0, 1_000_000);
    }

    private static BpmnDocument Indexed(BpmnDocument document) => document with
    { TextAnnotations = [.. document.TextAnnotations, new("unused", "Unused", "Process_1")] };

    private static BpmnDocument WithoutPadding(BpmnDocument document) => document with
    { TextAnnotations = [.. document.TextAnnotations.Where(note => note.Id != "unused")] };

    private static BpmnDocument Document(bool reverse, bool flow = false, int notes = 64)
    {
        var builder = new BpmnDocumentBuilder()
            .Node("a", BpmnFlowNodeType.Task, new Bounds(0, 0, 100, 80))
            .Node("b", BpmnFlowNodeType.Task, new Bounds(200, 0, 100, 80))
            .Flow("flow", "a", "b", [new(100, 40), new(140, 40), new(140, 80), new(160, 80), new(160, 40), new(200, 40)]);
        for (var index = 0; index < notes; index++)
        {
            var target = flow ? "flow" : "a";
            builder.TextAnnotation($"note{index}", "Note", new Bounds(110, 160 + index * 60, 80, 40))
                .Association($"association{index}", reverse ? target : $"note{index}", reverse ? $"note{index}" : target,
                    reverse ? [new(150, 80), new(150, 160 + index * 60)] : [new(150, 160 + index * 60), new(150, 80)]);
        }
        var document = builder.Build();
        document = document with
        { Associations = [.. document.Associations, new("unrelated", "a", "b", "Process_1")] };
        return Model.Geometry.ConnectorEndpoints.Normalize(document);
    }
}
