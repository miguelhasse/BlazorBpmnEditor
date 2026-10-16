using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Model;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Components;

public sealed class StencilPresentationTests
{
    [Theory]
    [InlineData(BpmnFlowNodeType.SubProcess, "M 10 12 h 4 v 4 h -4 Z", "M 12 13 v 2 M 11 14 h 2")]
    [InlineData(BpmnFlowNodeType.EventSubProcess, "M 10 12 h 4 v 4 h -4 Z", "M 12 13 v 2 M 11 14 h 2")]
    [InlineData(BpmnFlowNodeType.Transaction, "M 10 12 h 4 v 4 h -4 Z", "M 12 13 v 2 M 11 14 h 2")]
    [InlineData(BpmnFlowNodeType.AdHocSubProcess, "M 7 12 h 4 v 4 h -4 Z", "M 9 13 v 2 M 8 14 h 2")]
    public void SubProcessVariantsHaveCollapsedMarkers(BpmnFlowNodeType type, string box, string plus)
    {
        var shape = BpmnStencilPresentation.Silhouette(new BpmnStencilEntry(type, "Host label"));

        Assert.Contains(box, shape.Detail);
        Assert.Contains(plus, shape.Detail);
    }

    [Fact]
    public void EventSubProcessHasDashedOutline()
    {
        var shape = BpmnStencilPresentation.Silhouette(new BpmnStencilEntry(BpmnFlowNodeType.EventSubProcess, ""));

        Assert.Equal("2 1", shape.OutlineDashArray);
    }

    [Fact]
    public void TransactionHasDoubleBorder()
    {
        var shape = BpmnStencilPresentation.Silhouette(new BpmnStencilEntry(BpmnFlowNodeType.Transaction, ""));

        Assert.StartsWith("M 5 7.5", shape.Detail);
        Assert.Null(shape.OutlineDashArray);
    }

    [Fact]
    public void AdHocSubProcessHasTildeAlongsideCollapsedMarker()
    {
        var shape = BpmnStencilPresentation.Silhouette(new BpmnStencilEntry(BpmnFlowNodeType.AdHocSubProcess, ""));

        Assert.Contains("M 13 14 c", shape.Detail);
    }

    [Fact]
    public void TaskHasNoCollapsedMarker()
    {
        var shape = BpmnStencilPresentation.Silhouette(new BpmnStencilEntry(BpmnFlowNodeType.Task, ""));

        Assert.Null(shape.Detail);
    }
}
