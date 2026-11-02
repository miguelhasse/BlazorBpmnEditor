using System.Text.Json;

using Hasseware.AspNetCore.Components.Bpmn.Interop;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.CanvasPayload;

public sealed class CanvasDocumentPatchTests
{
    [Fact]
    public void RenameSendsOneTrimmedRecordAndNoUnchangedGeometry()
    {
        var before = Document();
        var after = before with { FlowNodes = [before.FlowNodes[0] with { Name = null }, before.FlowNodes[1]] };

        var patch = Assert.IsType<CanvasDocumentPatch>(CanvasDocumentPatch.Create(before, after, null, 3, 4));

        Assert.Equal(3, patch.BaseRevision);
        Assert.Equal(4, patch.Revision);
        var collection = Assert.Single(patch.Collections);
        Assert.Equal("flowNodes", collection.Collection);
        var node = Assert.Single(collection.Delta.Upserts);
        Assert.Equal(JsonValueKind.Null, node.GetProperty("name").ValueKind);
        Assert.False(node.TryGetProperty("instantiate", out _));
        Assert.Null(collection.Delta.Order);
        Assert.Empty(collection.Delta.Removals);
        Assert.Empty(patch.Planes);
    }

    [Fact]
    public void NonCanvasEditAdvancesTheRevisionWithoutSendingRecords()
    {
        var before = Document();
        var after = before with { FlowNodes = [before.FlowNodes[0] with { Instantiate = true }, before.FlowNodes[1]] };

        var patch = Assert.IsType<CanvasDocumentPatch>(CanvasDocumentPatch.Create(before, after, null, 0, 1));

        Assert.Empty(patch.Collections);
        Assert.Empty(patch.Planes);
    }

    [Fact]
    public void GeometryPatchContainsOnlyChangedDiAndPreservesShapeOrder()
    {
        var before = Document();
        var diagram = before.Diagrams[0];
        var moved = diagram.Plane.Shapes[0] with { Bounds = new Bounds(200, 150, 100, 80) };
        var after = before with { Diagrams = [diagram with { Plane = diagram.Plane with { Shapes = [moved, diagram.Plane.Shapes[1]] } }] };

        var patch = Assert.IsType<CanvasDocumentPatch>(CanvasDocumentPatch.Create(before, after, null, 0, 1));

        Assert.Empty(patch.Collections);
        var plane = Assert.Single(patch.Planes);
        var shapes = Assert.IsType<CanvasArrayPatch>(plane.Shapes);
        Assert.Equal(200, Assert.Single(shapes.Upserts).GetProperty("bounds").GetProperty("x").GetDouble());
        Assert.Null(shapes.Order);
        Assert.Null(plane.Edges);
    }

    [Fact]
    public void DeleteAndUndoCarryExplicitMembershipAndOrder()
    {
        var before = Document();
        var diagram = before.Diagrams[0];
        var after = before with
        {
            FlowNodes = [before.FlowNodes[1]],
            Diagrams = [diagram with { Plane = diagram.Plane with { Shapes = [diagram.Plane.Shapes[1]] } }],
        };

        var delete = Assert.IsType<CanvasDocumentPatch>(CanvasDocumentPatch.Create(before, after, null, 0, 1));
        var undo = Assert.IsType<CanvasDocumentPatch>(CanvasDocumentPatch.Create(after, before, null, 1, 2));

        Assert.Equal(["Task_1"], Assert.Single(delete.Collections).Delta.Removals);
        Assert.Null(Assert.Single(delete.Collections).Delta.Order);
        Assert.Equal(["Task_1", "Task_2"], Assert.Single(undo.Collections).Delta.Order);
        Assert.Equal("Task_1", Assert.Single(Assert.Single(undo.Collections).Delta.Upserts).GetProperty("id").GetString());
        Assert.Equal(["Task_1_di", "Task_2_di"], Assert.Single(undo.Planes).Shapes!.Order);
    }

    [Fact]
    public void ContainmentAndPlaneChangesRequireAnAuthoritativeFullLoad()
    {
        var before = Document();
        Assert.Null(CanvasDocumentPatch.Create(before, before with { Definitions = before.Definitions with { Id = "Other" } }, null, 0, 1));
        Assert.Null(CanvasDocumentPatch.Create(before, before with { Diagrams = [] }, null, 0, 1));
        Assert.Null(CanvasDocumentPatch.Create(before, before with { Diagrams = [before.Diagrams[0] with { Id = "Other" }] }, null, 0, 1));
    }

    [Fact]
    public void DuplicateIdsCannotBeAcknowledgedAsAPatch()
    {
        var before = Document();
        var after = before with { FlowNodes = [before.FlowNodes[0], before.FlowNodes[0]] };

        Assert.Null(CanvasDocumentPatch.Create(before, after, null, 0, 1));
    }

    [Theory]
    [InlineData(602)]
    [InlineData(2000)]
    [InlineData(5000)]
    public void RenamePayloadStaysBelowOneKilobyteAtEveryStressSize(int count)
    {
        var before = Document() with
        {
            FlowNodes = Enumerable.Range(0, count)
                .Select(index => new BpmnFlowNode($"Task_{index}", BpmnFlowNodeType.Task, "Process_1", "Original"))
                .ToArray(),
        };
        var nodes = before.FlowNodes.ToArray();
        nodes[1] = nodes[1] with { Name = "Renamed" };
        var after = before with { FlowNodes = nodes };

        var patch = Assert.IsType<CanvasDocumentPatch>(CanvasDocumentPatch.Create(before, after, null, 0, 1));
        var payload = JsonSerializer.Serialize(patch, JsonSerializerOptions.Web);

        Assert.InRange(payload.Length, 1, 1024);
        Assert.Single(Assert.Single(patch.Collections).Delta.Upserts);
        Assert.Empty(patch.Planes);
    }

    private static BpmnDocument Document() => new()
    {
        Definitions = new BpmnDefinitions("Definitions_1", "http://example.test/bpmn"),
        FlowNodes =
        [
            new("Task_1", BpmnFlowNodeType.Task, "Process_1", "First"),
            new("Task_2", BpmnFlowNodeType.Task, "Process_1", "Second"),
        ],
        Diagrams =
        [
            new("Diagram_1", new("Plane_1", "Process_1",
            [
                new("Task_1_di", "Task_1", new Bounds(0, 0, 100, 80)),
                new("Task_2_di", "Task_2", new Bounds(140, 0, 100, 80)),
            ], [])),
        ],
    };
}
