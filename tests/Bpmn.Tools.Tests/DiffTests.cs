using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;

using static Hasseware.AspNetCore.Components.Bpmn.Tools.Tests.Documents;

namespace Hasseware.AspNetCore.Components.Bpmn.Tools.Tests;

/// <summary>The minimal id-based diff (design doc, "Open questions": bpmn_diff).</summary>
public sealed class DiffTests
{
    [Fact]
    public void FindsNothingBetweenADocumentAndItself()
    {
        var document = Simple();

        var diff = BpmnDocumentDiff.Compare(document, document);

        Assert.Empty(diff.Added);
        Assert.Empty(diff.Removed);
        Assert.Empty(diff.Changed);
    }

    [Fact]
    public void ReportsAddedAndRemovedElements()
    {
        var before = Simple();
        var after = BpmnDocumentEditor.Apply(before, new DeleteElementsCommand(["end"])).Document;
        after = BpmnDocumentEditor.Apply(after, new CreateElementCommand("extra", Model.BpmnFlowNodeType.Task, "Process_1", new Bounds(700, 110, 100, 80), "Extra")).Document;

        var diff = BpmnDocumentDiff.Compare(before, after);

        Assert.Equal("end", Assert.Single(diff.Removed).Id);
        var added = Assert.Single(diff.Added);
        Assert.Equal(("extra", "task", "Extra"), (added.Id, added.Type, added.Name?.UntrustedText));
    }

    [Fact]
    public void ReportsAChangedProperty()
    {
        var before = Simple();
        var after = BpmnDocumentEditor.Apply(before, new UpdatePropertiesCommand("review", "name", "Approve")).Document;

        var changed = Assert.Single(BpmnDocumentDiff.Compare(before, after).Changed);

        Assert.Equal("review", changed.Id);
        var property = Assert.Single(changed.Properties);
        Assert.Equal(("name", "Review", "Approve"), (property.Name, property.Before?.UntrustedText, property.After?.UntrustedText));
        Assert.False(changed.GeometryChanged);
    }

    [Fact]
    public void ReportsAMoveAsGeometryOnly()
    {
        var before = Simple();
        var after = BpmnDocumentEditor.Apply(before, new MoveShapesCommand([new ShapeGeometry("end_di", new Bounds(520, 140, 36, 36))])).Document;

        var changed = Assert.Single(BpmnDocumentDiff.Compare(before, after).Changed, element => element.Id == "end");

        Assert.True(changed.GeometryChanged);
        Assert.Empty(changed.Properties);
    }

    [Fact]
    public void ReportsAReconnectedFlow()
    {
        var before = Simple();
        var after = BpmnDocumentEditor.Apply(before, new ReconnectConnectorCommand("f1", SourceRef: null, TargetRef: "end", [new DiPoint(136, 150), new DiPoint(500, 150)])).Document;

        var changed = Assert.Single(BpmnDocumentDiff.Compare(before, after).Changed, element => element.Id == "f1");

        Assert.Equal(("review", "end"), (changed.TargetBefore, changed.TargetAfter));
        Assert.Null(changed.SourceBefore);
    }

    [Fact]
    public void StopsAtTheLimitAndSaysSo()
    {
        var before = Simple();
        var after = BpmnDocumentEditor.Apply(before, new DeleteElementsCommand(["start", "review", "end"])).Document;

        var diff = BpmnDocumentDiff.Compare(before, after, new BpmnToolLimits(MaxElements: 2));

        Assert.Equal(2, diff.Removed.Count + diff.Added.Count + diff.Changed.Count);
        Assert.True(diff.Truncated);
    }

    [Fact]
    public async Task DiffsTheHostsDocumentAgainstABaseline()
    {
        var host = Host(Simple());
        var baseline = host.Document;
        await BpmnTools.SetPropertyAsync(host, new BpmnSetPropertyRequest("review", "name", "Approve"), Token);

        var result = await BpmnTools.DiffAsync(host, baseline, Token);

        Assert.Equal("review", Assert.Single(result.Data!.Changed).Id);
    }
}
