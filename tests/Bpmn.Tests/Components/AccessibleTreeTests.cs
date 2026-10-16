using Bunit;

using Hasseware.AspNetCore.Components.Bpmn.Accessibility;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;

using Microsoft.AspNetCore.Components.Web;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Components;

/// <summary>
/// The keyboard-navigable tree (implementation plan M7).
/// </summary>
/// <remarks>
/// The component binds keys to directions and renders tree semantics; where a direction leads is
/// <see cref="BpmnGraphNavigation"/>'s decision and is tested separately. What these are about is
/// the part a screen reader depends on and a browser will not tell you: that exactly one item is
/// tabbable, that levels and selection are announced, and that Tab is never swallowed.
/// </remarks>
public sealed class AccessibleTreeTests : BunitContext
{
    /// <summary>Start → Task → Gateway, with the Gateway branching to two Ends.</summary>
    private static IReadOnlyList<BpmnGraphNode> Graph() =>
    [
        new("Start_1", "startEvent", "Begin", "Process_1", null, 1, ["Task_1"], []),
        new("Task_1", "userTask", "Review", "Process_1", null, 1, ["Gateway_1"], ["Start_1"]),
        new("Gateway_1", "exclusiveGateway", "OK?", "Process_1", null, 1, ["End_Yes", "End_No"], ["Task_1"]),
        new("End_Yes", "endEvent", "Accepted", "Process_1", null, 1, [], ["Gateway_1"]),
        new("End_No", "endEvent", "Rejected", "Process_1", null, 1, [], ["Gateway_1"]),
    ];

    // --- tree semantics --------------------------------------------------------------

    [Fact]
    public void IsATreeWithOneItemPerElement()
    {
        var tree = this.RenderSeeded<BpmnAccessibleTree>(seed => seed.Graph(Graph()), canvas: true);

        Assert.Equal("tree", tree.Find("ul").GetAttribute("role"));
        Assert.Equal(5, tree.FindAll("[role=treeitem]").Count);
    }

    [Fact]
    public void SharedProcessPoolsRenderWithoutDuplicateLaneKeys()
    {
        var document = new BpmnDocumentBuilder()
            .Pool("Pool_A", "A", new Bounds(0, 0, 400, 120))
            .Pool("Pool_B", "B", new Bounds(0, 160, 400, 120))
            .Lane("S_LaneA", "Shared", new Bounds(30, 0, 370, 120), ["Task_1"])
            .Node("Task_1", BpmnFlowNodeType.Task, new Bounds(100, 20, 100, 80))
            .Build();
        var graph = BpmnAccessibleGraph.For(document, null);
        var tree = this.RenderSeeded<BpmnAccessibleTree>(seed => seed.Graph(graph), canvas: true);

        var ids = tree.FindAll("[role=treeitem]").Select(item => item.Id).ToList();
        Assert.Equal(4, ids.Count);
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void ExactlyOneItemIsTabbable()
    {
        /*
         * The roving tabindex. Five hundred tab stops is not navigation, it is an obstacle, and a
         * tree with none at all cannot be reached by keyboard at all — so the count is exactly one
         * whether or not anything is selected yet.
         */
        var tree = this.RenderSeeded<BpmnAccessibleTree>(seed => seed.Graph(Graph()), canvas: true);

        Assert.Single(tree.FindAll("[role=treeitem][tabindex='0']"));

        tree = this.RenderSeeded<BpmnAccessibleTree>(seed => seed.Graph(Graph()).Current("Gateway_1"), canvas: true);

        var tabbable = Assert.Single(tree.FindAll("[role=treeitem][tabindex='0']"));
        Assert.Equal(BpmnAccessibleTree.ItemId("Gateway_1"), tabbable.GetAttribute("id"));
    }

    [Fact]
    public void AnnouncesTypeNameAndFlowCounts()
    {
        // The counts are not decoration: on a diagram nobody can see, they are the only indication
        // that there is somewhere further to go.
        var tree = this.RenderSeeded<BpmnAccessibleTree>(seed => seed.Graph(Graph()), canvas: true);

        var items = tree.FindAll("[role=treeitem]");

        Assert.Equal("Start event Begin. no incoming, 1 outgoing.", items[0].TextContent.Trim());
        Assert.Equal("Exclusive gateway OK?. 1 incoming, 2 outgoing.", items[2].TextContent.Trim());
    }

    [Fact]
    public void SaysSoWhenThereIsNothingToWalk()
    {
        var tree = this.RenderSeeded<BpmnAccessibleTree>(seed => { }, canvas: true);

        var item = Assert.Single(tree.FindAll("[role=treeitem]"));
        Assert.Contains("no elements", item.TextContent, StringComparison.Ordinal);
        Assert.Equal("0", item.GetAttribute("tabindex"));
    }

    // --- keys ------------------------------------------------------------------------

    [Fact]
    public void RightFollowsTheFlow()
    {
        var moved = Press("Task_1", "ArrowRight");

        Assert.Equal("Gateway_1", moved);
    }

    [Fact]
    public void LeftGoesBackUpIt()
    {
        Assert.Equal("Task_1", Press("Gateway_1", "ArrowLeft"));
    }

    [Fact]
    public void DownMovesBetweenTheBranchesOfAFork()
    {
        // The reason up and down are not simply "next in the list": a Gateway's alternatives are
        // the thing a reader most needs to hear, and they are not adjacent in any reading order.
        Assert.Equal("End_No", Press("End_Yes", "ArrowDown"));
        Assert.Equal("End_Yes", Press("End_No", "ArrowUp"));
    }

    [Fact]
    public void HomeAndEndAreTheEndsOfTheDiagram()
    {
        Assert.Equal("Start_1", Press("Gateway_1", "Home"));
        Assert.Equal("End_No", Press("Gateway_1", "End"));
    }

    [Fact]
    public void AKeyWithNowhereToGoChangesNothing()
    {
        // Silence rather than a wrap-around: arriving back at the Start Event because the End had
        // no outgoing flow would tell a reader the diagram loops when it does not.
        Assert.Null(Press("End_Yes", "ArrowRight"));
        Assert.Null(Press("Start_1", "ArrowLeft"));
    }

    [Fact]
    public void TabIsLeftAlone()
    {
        /*
         * The one thing a keyboard surface must never be is a trap. Tab is not handled here at all,
         * so the browser moves focus out of the tree exactly as it would from any other control.
         */
        Assert.Null(Press("Task_1", "Tab"));
        Assert.Null(Press("Task_1", "a"));
        Assert.Null(Press("Task_1", "Escape"));
    }

    [Fact]
    public void EnterActivatesWhereTheReaderIs()
    {
        string? activated = null;

        var tree = this.RenderSeeded<BpmnAccessibleTree>(
            seed => seed.Graph(Graph()).Current("Task_1").OnActivate(id => activated = id),
            canvas: true);

        tree.Find("ul").KeyDown(new KeyboardEventArgs { Key = "Enter" });

        Assert.Equal("Task_1", activated);
    }

    [Fact]
    public void TheFirstKeystrokeLandsSomewhereRatherThanNowhere()
    {
        // Nothing focused yet is the ordinary state on load, and a first arrow key that did
        // nothing would read as the tree being broken.
        Assert.Equal("Start_1", Press(null, "ArrowRight"));
    }

    private static string? Press(string? current, string key)
    {
        BpmnEditorSession? session = null;

        using var context = new BunitContext();
        var tree = context.RenderSeeded<BpmnAccessibleTree>(
            seed => seed.Graph(Graph()).Current(current).Session(s => session = s),
            canvas: true);

        tree.Find("ul").KeyDown(new KeyboardEventArgs { Key = key });

        var moved = session!.AccessibleCurrent;
        return moved == current ? null : moved;
    }
}
