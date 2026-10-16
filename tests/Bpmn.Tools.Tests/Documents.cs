using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;

namespace Hasseware.AspNetCore.Components.Bpmn.Tools.Tests;

/// <summary>Small documents the tool tests share.</summary>
internal static class Documents
{
    public static readonly BpmnEditOrigin Agent = BpmnEditOrigin.FromAgent("test-agent");

    public static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>Start → Review → End in one process, with the end event far enough away to leave room.</summary>
    public static BpmnDocument Simple() => new BpmnDocumentBuilder()
        .Node("start", BpmnFlowNodeType.StartEvent, new Bounds(100, 132, 36, 36), "Start")
        .Node("review", BpmnFlowNodeType.UserTask, new Bounds(200, 110, 100, 80), "Review")
        .Node("end", BpmnFlowNodeType.EndEvent, new Bounds(500, 132, 36, 36), "Done")
        .Flow("f1", "start", "review", [new DiPoint(136, 150), new DiPoint(200, 150)])
        .Build();

    /// <summary>Two pools, each with its own process and one task.</summary>
    public static BpmnDocument TwoPools() => new BpmnDocumentBuilder()
        .Process("Process_2")
        .Pool("PoolA", "Customer", new Bounds(0, 0, 600, 200), processRef: "Process_1")
        .Pool("PoolB", "Shop", new Bounds(0, 250, 600, 200), processRef: "Process_2")
        .Node("ask", BpmnFlowNodeType.Task, new Bounds(100, 60, 100, 80), "Ask", poolId: "PoolA")
        .Node("answer", BpmnFlowNodeType.Task, new Bounds(100, 310, 100, 80), "Answer", containerId: "Process_2", poolId: "PoolB")
        .Node("other", BpmnFlowNodeType.Task, new Bounds(300, 60, 100, 80), "Other", poolId: "PoolA")
        .Build();

    public static BpmnDocumentToolHost Host(BpmnDocument document, bool isReadOnly = false, BpmnToolLimits? limits = null) =>
        new(document, Agent, limits: limits) { IsReadOnly = isReadOnly };
}
