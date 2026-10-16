using Bunit;

using Hasseware.AspNetCore.Components.Bpmn.Accessibility;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;

using Microsoft.AspNetCore.Components.Web;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Components;

/// <summary>
/// What the accessible tree says and does about a Sub-Process's two states.
/// </summary>
/// <remarks>
/// <para>
/// The canvas is hidden from assistive technology, so the tree is the whole story for a reader who
/// is being read to. On a diagram nobody can see, the difference between a leaf and a container
/// with a process inside it is not a detail — it is the difference between "there is nothing more
/// here" and "there is a whole process here you have not been offered".
/// </para>
/// <para>
/// It is also the keyboard path to opening one, since the expand button is a pointer target drawn
/// over the glyph rather than something in the tab order of the tree.
/// </para>
/// </remarks>
public sealed class AccessibleExpansionTests : BunitContext
{
    // --- what the tree says ------------------------------------------------------------

    [Fact]
    public void ACollapsedSubProcessSaysSo()
    {
        var item = Render(Graph()).Find($"#{BpmnAccessibleTree.ItemId("Sub_shut")}");

        Assert.Equal("false", item.GetAttribute("aria-expanded"));
        Assert.Contains("collapsed", item.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void AndAnExpandedOneDoes()
    {
        var item = Render(Graph()).Find($"#{BpmnAccessibleTree.ItemId("Sub_open")}");

        Assert.Equal("true", item.GetAttribute("aria-expanded"));
        Assert.Contains("expanded", item.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void ATaskSaysNothingAboutBeingExpanded()
    {
        /*
         * Absent, not "false". A Task with aria-expanded="false" is announced as collapsed, which
         * tells the reader it is hiding something — and every Task in the diagram would be saying
         * it. This is the assertion that keeps the attribute meaningful.
         */
        var item = Render(Graph()).Find($"#{BpmnAccessibleTree.ItemId("Task_1")}");

        Assert.False(item.HasAttribute("aria-expanded"));
        Assert.DoesNotContain("collapsed", item.TextContent, StringComparison.Ordinal);
    }

    // --- and what the keyboard does ----------------------------------------------------

    [Fact]
    public void PlusAsksForACollapsedSubProcessToBeOpened()
    {
        var asked = Toggle("Sub_shut", "+");

        Assert.Equal("Sub_shut", asked);
    }

    [Fact]
    public void MinusAsksForAnExpandedOneToBeClosed()
    {
        Assert.Equal("Sub_open", Toggle("Sub_open", "-"));
    }

    [Fact]
    public void TheNumpadKeysDoTheSame()
    {
        Assert.Equal("Sub_shut", Toggle("Sub_shut", "Add"));
        Assert.Equal("Sub_open", Toggle("Sub_open", "Subtract"));
    }

    [Fact]
    public void PlusOnSomethingAlreadyOpenAsksForNothing()
    {
        // The keys name a state rather than toggling, so pressing the one it is already in is a
        // no-op rather than a round trip through the document.
        Assert.Null(Toggle("Sub_open", "+"));
    }

    [Fact]
    public void AndNeitherKeyDoesAnythingOnATask()
    {
        Assert.Null(Toggle("Task_1", "+"));
        Assert.Null(Toggle("Task_1", "-"));
    }

    [Fact]
    public void TheArrowKeysStillWalkTheFlow()
    {
        // The reason expansion is on plus and minus at all: Left and Right are spent on the
        // Sequence Flow, which is the more valuable binding in a graph.
        BpmnEditorSession? session = null;

        var tree = this.RenderSeeded<BpmnAccessibleTree>(
            seed => seed.Graph(Graph()).Current("Start_1").Session(s => session = s),
            canvas: true);

        tree.Find("ul").KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });

        Assert.Equal("Task_1", session!.AccessibleCurrent);
    }

    // --- and where the state comes from -------------------------------------------------

    [Fact]
    public void TheGraphReadsExpansionOffTheShapeOnThePlane()
    {
        var nodes = BpmnAccessibleGraph.For(Document(), null);

        Assert.True(Assert.Single(nodes, node => node.ElementId == "Sub_open").IsExpanded);
        Assert.False(Assert.Single(nodes, node => node.ElementId == "Sub_shut").IsExpanded);
    }

    [Fact]
    public void AndLeavesItUnsaidForAnythingThatCannotExpand()
    {
        var nodes = BpmnAccessibleGraph.For(Document(), null);

        Assert.Null(Assert.Single(nodes, node => node.ElementId == "Task_1").IsExpanded);
    }

    [Fact]
    public void ACallActivityCannotExpandEither()
    {
        // It looks like a container and never opens — its content belongs to a global Process — so
        // announcing it as collapsed would promise something that will never happen.
        var nodes = BpmnAccessibleGraph.For(Document(), null);

        Assert.Null(Assert.Single(nodes, node => node.ElementId == "Call_1").IsExpanded);
    }

    // --- fixtures ------------------------------------------------------------------------

    private IRenderedComponent<BpmnAccessibleTree> Render(IReadOnlyList<BpmnGraphNode> nodes) =>
        this.RenderSeeded<BpmnAccessibleTree>(seed => seed.Graph(nodes), canvas: true);

    /// <summary>Presses a key on a tree item and reports what the tree asked the editor for.</summary>
    private string? Toggle(string on, string key)
    {
        string? asked = null;

        var tree = this.RenderSeeded<BpmnAccessibleTree>(
            seed => seed.Graph(Graph()).Current(on).OnToggle(id => asked = id),
            canvas: true);

        tree.Find("ul").KeyDown(new KeyboardEventArgs { Key = key });

        return asked;
    }

    private static IReadOnlyList<BpmnGraphNode> Graph() =>
    [
        new("Task_1", "userTask", "Review", "Process_1", null, 1, ["Sub_shut"], []),
        new("Sub_shut", "subProcess", "Order Products", "Process_1", null, 1, [], ["Task_1"]) { IsExpanded = false },
        new("Sub_open", "subProcess", "Handle Payment", "Process_1", null, 1, [], []) { IsExpanded = true },
    ];

    private static BpmnDocument Document()
    {
        var document = new BpmnDocumentBuilder("Process_1")
            .Node("Task_1", BpmnFlowNodeType.UserTask, new Bounds(0, 0, 100, 80), "Review")
            .Node("Sub_open", BpmnFlowNodeType.SubProcess, new Bounds(200, 0, 350, 200), "Handle Payment", isExpanded: true)
            .Node("Sub_shut", BpmnFlowNodeType.SubProcess, new Bounds(600, 0, 100, 80), "Order Products", isExpanded: false)
            .Node("Call_1", BpmnFlowNodeType.CallActivity, new Bounds(800, 0, 100, 80), "Elsewhere")
            .Build();

        return document with
        {
            Processes = [.. document.Processes, new BpmnProcess("Reusable_1", "Reusable")],
            FlowNodes =
            [
                .. document.FlowNodes.Select(node => node.Id == "Call_1"
                    ? node with { CalledElementRef = "Reusable_1" }
                    : node),
            ],
        };
    }
}
