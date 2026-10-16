using Bunit;
using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.FluentUI.AspNetCore.Components.Bpmn;
using Microsoft.FluentUI.AspNetCore.Components;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Components;

/// <summary>
/// The reference browser and the panel's safe rename (implementation plan M6).
/// </summary>
public sealed class ReferenceBrowserTests : FluentTestContext
{
    [Fact]
    public void SaysSoWhenNothingRefersToTheElement()
    {
        // The answer you need before deleting something, and an empty area does not give it.
        var browser = this.RenderSeeded<FluentBpmnReferenceBrowser>(seed => { });

        Assert.Contains("Nothing refers to this", browser.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ListsEachUsageWithTheAttributeThatHoldsIt()
    {
        var browser = this.RenderSeeded<FluentBpmnReferenceBrowser>(seed => seed
            .Current("Task_1")
            .Usages(
            [
                new BpmnReferenceSite("Flow_1", "Sequence Flow", "targetRef", "Task_1"),
                new BpmnReferenceSite("Lane_1", "Lane", "flowNodeRef", "Task_1"),
            ]));

        Assert.Equal(
            ["targetRef", "flowNodeRef"],
            browser.FindAll(".bpmn-usages__attribute").Select(element => element.TextContent.Trim()));
    }

    [Fact]
    public void ADiUsageNamesItsDiagram()
    {
        // Section 10.1: the same element can be drawn on more than one plane, so a DI usage is not
        // located until the diagram is named.
        var browser = this.RenderSeeded<FluentBpmnReferenceBrowser>(seed => seed
            .Current("Task_1")
            .Usages(
                [new BpmnReferenceSite("Task_1_di", "Shape", "bpmnElement", "Task_1", "Diagram_1")]));

        Assert.Equal("Diagram_1", browser.Find(".bpmn-usages__diagram").TextContent.Trim());
    }

    [Fact]
    public void ChoosingAUsageReportsIt()
    {
        BpmnReferenceSite? chosen = null;
        var usage = new BpmnReferenceSite("Flow_1", "Sequence Flow", "targetRef", "Task_1");

        var browser = this.RenderSeeded<FluentBpmnReferenceBrowser>(seed => seed
            .Current("Task_1")
            .Usages([usage])
            .OnUsage(site => chosen = site));

        browser.Find(".bpmn-usages__go").Click();

        Assert.Equal(usage, chosen);
    }

    // --- safe rename, through the panel -------------------------------------------------

    [Fact]
    public void ChangingTheIdSubmitsARename()
    {
        /*
         * Never through the property form. An id is what other elements were written against, so
         * changing it is a whole-document rewrite and a command of its own — and it must not be
         * batched with property edits that would apply even if the rename were refused.
         */
        BpmnEditCommand? submitted = null;

        var panel = this.RenderSeeded<FluentBpmnPropertyPanel>(seed => seed
            .Snapshots([Snapshot("Task_1")])
            .OnCommand(command => submitted = command));

        panel.FindComponent<FluentTextInput>().InvokeAsync(() => panel.FindComponent<FluentTextInput>().Instance.ValueChanged.InvokeAsync("Task_Renamed"));

        var rename = Assert.IsType<RenameElementCommand>(submitted);

        Assert.Equal("Task_1", rename.ElementId);
        Assert.Equal("Task_Renamed", rename.NewId);
    }

    [Fact]
    public void ClearingTheIdSubmitsNothing()
    {
        // An element without an id is not something to offer to make by accident, and a blank box
        // is a half-typed value rather than a request.
        BpmnEditCommand? submitted = null;

        var panel = this.RenderSeeded<FluentBpmnPropertyPanel>(seed => seed
            .Snapshots([Snapshot("Task_1")])
            .OnCommand(command => submitted = command));

        panel.FindComponent<FluentTextInput>().InvokeAsync(() => panel.FindComponent<FluentTextInput>().Instance.ValueChanged.InvokeAsync(string.Empty));

        Assert.Null(submitted);
    }

    [Fact]
    public void RetypingTheSameIdSubmitsNothing()
    {
        BpmnEditCommand? submitted = null;

        var panel = this.RenderSeeded<FluentBpmnPropertyPanel>(seed => seed
            .Snapshots([Snapshot("Task_1")])
            .OnCommand(command => submitted = command));

        panel.FindComponent<FluentTextInput>().InvokeAsync(() => panel.FindComponent<FluentTextInput>().Instance.ValueChanged.InvokeAsync("Task_1"));

        Assert.Null(submitted);
    }

    [Fact]
    public void ReadonlyOffersTheIdForReadingOnly()
    {
        BpmnEditCommand? submitted = null;

        var panel = this.RenderSeeded<FluentBpmnPropertyPanel>(seed => seed
            .Snapshots([Snapshot("Task_1")])
            .ReadOnly()
            .OnCommand(command => submitted = command));

        Assert.True(panel.FindComponent<FluentTextInput>().Instance.ReadOnly);

        panel.FindComponent<FluentTextInput>().InvokeAsync(() => panel.FindComponent<FluentTextInput>().Instance.ValueChanged.InvokeAsync("Task_Renamed"));

        Assert.Null(submitted);
    }

    [Fact]
    public void ASelectionOffersNoIdToRename()
    {
        // A rename is about one element. Two elements cannot share an id, so there is no sensible
        // value to put in the box, let alone to submit.
        var panel = this.RenderSeeded<FluentBpmnPropertyPanel>(seed => seed
            .Snapshots([Snapshot("Task_1"), Snapshot("Task_2")]));

        Assert.Empty(panel.FindAll(".bpmn-properties__id input"));
    }

    private static BpmnElementSnapshot Snapshot(string id) => new()
    {
        ElementId = id,
        ElementType = "task",
        Category = BpmnElementCategory.Activity,
        Properties = [new BpmnPropertyDescriptor("name", "Name", BpmnPropertyKind.Text, "Work")],
    };
}
