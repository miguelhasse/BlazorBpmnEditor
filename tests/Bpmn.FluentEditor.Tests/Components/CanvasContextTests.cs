using System.Collections.Immutable;
using Bunit;
using Hasseware.AspNetCore.Components.Bpmn.Accessibility;
using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Interop;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;
using Hasseware.FluentUI.AspNetCore.Components.Bpmn;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Components;

/// <summary>The widgets drawn over the canvas read what the cascaded session and state say.</summary>
public sealed class CanvasContextTests : FluentTestContext
{
    [Fact]
    public async Task TheContextMenuOpensWhenTheSessionOpensIt()
    {
        BpmnEditorSession? session = null;
        var menu = this.RenderSeeded<FluentBpmnContextMenu>(seed => seed.Snapshots([Snapshot("Task_1")]).Session(s => session = s), canvas: true);
        Assert.Empty(menu.FindAll(".bpmn-menu"));

        await menu.InvokeAsync(() => session!.HandleContextMenuRequestedAsync(
            new ContextMenuRequested("element", "Task_1", Anchor(), "mouse", ["Task_1"])));

        Assert.Equal("Task_1", menu.Find(".bpmn-menu").GetAttribute("data-bpmn-element"));
    }

    [Fact]
    public async Task TheLabelEditorOpensWhenTheSessionStartsAnEdit()
    {
        BpmnEditorSession? session = null;
        var editor = this.RenderSeeded<BpmnLabelEditor>(seed => seed.Session(s => session = s), canvas: true);
        Assert.Empty(editor.FindAll(".bpmn-label-editor"));

        await editor.InvokeAsync(() => session!.HandleLabelEditRequestedAsync(LabelEdit("Task_1")));

        Assert.Equal("Task_1", editor.Find(".bpmn-label-editor").GetAttribute("data-bpmn-element"));
    }

    [Fact]
    public void AGroupHasNoContextualQuickActions()
    {
        var group = Snapshot("Group_1") with
        {
            ElementType = "group",
            Category = BpmnElementCategory.Artifact,
        };

        Assert.Empty(FluentBpmnContextualActions.DefaultsFor(group));
    }

    [Fact]
    public void TheTreeWalksTheSessionsGraph()
    {
        var tree = this.RenderSeeded<BpmnAccessibleTree>(seed => seed.Graph(Graph()).Current("Task_1"), canvas: true);

        Assert.Equal(2, tree.FindAll("[role=treeitem]").Count);

        var tabbable = Assert.Single(tree.FindAll("[role=treeitem][tabindex='0']"));
        Assert.Equal(BpmnAccessibleTree.ItemId("Task_1"), tabbable.GetAttribute("id"));
    }

    private static ImmutableArray<BpmnGraphNode> Graph() =>
    [
        new("Start_1", "startEvent", "Begin", "Process_1", null, 1, ["Task_1"], []),
        new("Task_1", "userTask", "Review", "Process_1", null, 1, [], ["Start_1"]),
    ];

    private static BpmnElementSnapshot Snapshot(string id) => new()
    {
        ElementId = id,
        ElementType = "task",
        Category = BpmnElementCategory.Activity,
        Label = "Work",
    };

    private static Anchor Anchor() => new(new Bounds(0, 0, 100, 80), new Bounds(10, 10, 100, 80));

    private static LabelEditRequested LabelEdit(string id) => new(id, Anchor(), "Work", "Segoe UI", 12d, 16d, "center", 1d);
}
