using Bunit;
using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.FluentUI.AspNetCore.Components.Bpmn;
using Microsoft.FluentUI.AspNetCore.Components;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Components;

/// <summary>
/// The diagram picker and the drill-down control (implementation plan M6).
/// </summary>
public sealed class DiagramNavigatorTests : FluentTestContext
{
    [Fact]
    public void OffersNoPickerForADocumentWithOneDiagram()
    {
        // Most files draw one thing. A select with a single option is chrome that asks a question
        // with only one answer.
        var navigator = this.RenderSeeded<FluentBpmnDiagramNavigator>(seed => seed
            .Diagrams([Target("Diagram_1", "Main")]));

        Assert.Empty(navigator.FindAll(".bpmn-diagrams__select"));
    }

    [Fact]
    public void ListsEveryDiagramAndMarksTheActiveOne()
    {
        var navigator = this.RenderSeeded<FluentBpmnDiagramNavigator>(seed => seed
            .Diagrams([Target("Diagram_1", "Main"), Target("Diagram_2", "Shipment")])
            .Active("Diagram_2"));

        var select = navigator.FindComponent<FluentSelect<BpmnDiagramTarget, string>>().Instance;
        Assert.Equal(2, select.Items!.Count());
        Assert.Equal("Diagram_2", select.Value);
    }

    [Fact]
    public void SaysWhichDiagramsAreNested()
    {
        // A plane over a Sub-Process is the inside of something, not a diagram in its own right,
        // and listing six of them beside the root reads as six unrelated files.
        var navigator = this.RenderSeeded<FluentBpmnDiagramNavigator>(seed => seed
            .Diagrams(
            [
                Target("Diagram_1", "Main"),
                Target("Diagram_2", "Review", isRoot: false),
            ]));

        var select = navigator.FindComponent<FluentSelect<BpmnDiagramTarget, string>>().Instance;
        Assert.Contains("(nested)", select.OptionText!(select.Items!.ElementAt(1))!, StringComparison.Ordinal);
    }

    [Fact]
    public void ChoosingADiagramReportsIt()
    {
        string? chosen = null;

        var navigator = this.RenderSeeded<FluentBpmnDiagramNavigator>(seed => seed
            .Diagrams([Target("Diagram_1", "Main"), Target("Diagram_2", "Shipment")])
            .OnNavigate(id => chosen = id));

        navigator.InvokeAsync(() => navigator.FindComponent<FluentSelect<BpmnDiagramTarget, string>>().Instance.ValueChanged.InvokeAsync("Diagram_2"));

        Assert.Equal("Diagram_2", chosen);
    }

    // --- the drill-down control ---------------------------------------------------------

    [Fact]
    public void TheOpenButtonIsDisabledWithAReasonRatherThanHidden()
    {
        /*
         * Section 9.3 asks a disabled control to say why. "This element does not open a diagram"
         * and "what it opens is not in this file" are different facts, and a button that vanishes
         * tells the user neither — it reads as a feature that does not exist.
         */
        var navigator = this.RenderSeeded<FluentBpmnDiagramNavigator>(seed => seed
            .Diagrams([Target("Diagram_1", "Main")])
            .Drill(new BpmnDrillTarget(
                null,
                BpmnDrillRefusal.NoLocalDiagram,
                "'Process_X' is called from here but is not drawn in this file.")));

        var button = navigator.Find(".bpmn-diagrams__open");

        Assert.NotNull(button.GetAttribute("disabled"));
        Assert.Contains("not drawn in this file", button.GetAttribute("title")!, StringComparison.Ordinal);
    }

    [Fact]
    public void TheOpenButtonSaysWhatToSelectWhenNothingIs()
    {
        var navigator = this.RenderSeeded<FluentBpmnDiagramNavigator>(seed => seed
            .Diagrams([Target("Diagram_1", "Main")]));

        var button = navigator.Find(".bpmn-diagrams__open");

        Assert.NotNull(button.GetAttribute("disabled"));
        Assert.Contains("Call Activity", button.GetAttribute("title")!, StringComparison.Ordinal);
    }

    [Fact]
    public void TheOpenButtonWorksWhenThereIsSomewhereToGo()
    {
        var opened = false;

        var navigator = this.RenderSeeded<FluentBpmnDiagramNavigator>(seed => seed
            .Diagrams([Target("Diagram_1", "Main")])
            .Drill(new BpmnDrillTarget("Diagram_2", BpmnDrillRefusal.None, null))
            .OnNavigate(_ => opened = true));

        var button = navigator.Find(".bpmn-diagrams__open");

        Assert.Null(button.GetAttribute("disabled"));

        button.Click();

        Assert.True(opened);
    }

    private static BpmnDiagramTarget Target(string id, string label, bool isRoot = true) =>
        new(id, label, $"Process_{id}", isRoot);
}
