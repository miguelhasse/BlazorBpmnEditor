using Bunit;
using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.FluentUI.AspNetCore.Components.Bpmn;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.FluentUI.AspNetCore.Components;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Components;

/// <summary>
/// The shortcut help surface.
/// </summary>
/// <remarks>
/// It reads the same binding list the canvas was configured with, so the interesting assertions
/// are about that relationship: a rebound key must change the help text, and nothing may appear
/// that was not configured.
/// </remarks>
public sealed class BpmnShortcutHelpTests : FluentTestContext
{
    [Fact]
    public void RendersNothingWhileHidden()
    {
        var component = this.RenderSeeded<FluentBpmnShortcutHelp>(seed => seed
            .Bindings(KeyboardOptions.Default.Bindings));

        Assert.Empty(component.Markup.Trim());
    }

    [Fact]
    public void ListsTheConfiguredBindings()
    {
        var component = this.RenderSeeded<FluentBpmnShortcutHelp>(seed => seed.ShowHelp()
            .Bindings(KeyboardOptions.Default.Bindings));

        var keys = component.FindAll("kbd").Select(node => node.TextContent).ToList();

        Assert.Contains("F", keys);
        Assert.Contains("ArrowLeft", keys);
        Assert.Contains("Shift + ?", keys);
    }

    [Fact]
    public void GroupsSeveralKeysBoundToOneAction()
    {
        // The defaults bind both '+' and '=' to zoom in. Two rows both reading "Zoom in" looks
        // like a bug in the help, so they share a row.
        var component = this.RenderSeeded<FluentBpmnShortcutHelp>(seed => seed.ShowHelp()
            .Bindings(KeyboardOptions.Default.Bindings));

        var zoomInRows = component.FindAll(".bpmn-shortcut-help__row")
            .Where(row => row.QuerySelector("dd")?.TextContent == "Zoom in")
            .ToList();

        var row = Assert.Single(zoomInRows);
        Assert.Equal(2, row.QuerySelectorAll("kbd").Length);
    }

    [Fact]
    public void ShowsARebindingRatherThanTheDefault()
    {
        // The whole point of generating the list: a host that rebinds fit-to-content must not be
        // shown the key it replaced.
        var component = this.RenderSeeded<FluentBpmnShortcutHelp>(seed => seed.ShowHelp()
            .Bindings(
                [new ShortcutBinding(BpmnShortcutCommand.FitToContent, "h", Ctrl: true, Description: "Fit")]));

        Assert.Equal("Ctrl + H", Assert.Single(component.FindAll("kbd")).TextContent);
    }

    [Fact]
    public void OmitsBindingsWithNoDescription()
    {
        // An undescribed binding has nothing useful to show, and inventing a label for it would
        // be guessing at the host's intent.
        var component = this.RenderSeeded<FluentBpmnShortcutHelp>(seed => seed.ShowHelp()
            .Bindings([new ShortcutBinding(BpmnShortcutCommand.ZoomIn, "+")]));

        Assert.Empty(component.FindAll(".bpmn-shortcut-help__row"));
        Assert.NotNull(component.Find(".bpmn-shortcut-help__empty"));
    }

    [Fact]
    public void SaysSoWhenNothingIsBound()
    {
        var component = this.RenderSeeded<FluentBpmnShortcutHelp>(seed => seed.ShowHelp()
            .Bindings([]));

        Assert.Contains(
            "No keyboard shortcuts",
            component.Find(".bpmn-shortcut-help__empty").TextContent,
            StringComparison.Ordinal);
    }

    [Fact]
    public void IsAModalDialogWithAnAccessibleName()
    {
        var component = this.RenderSeeded<FluentBpmnShortcutHelp>(seed => seed.ShowHelp()
            .Bindings(KeyboardOptions.Default.Bindings));

        var dialog = component.Find("[role=dialog]");
        var labelId = dialog.GetAttribute("aria-labelledby");

        Assert.Equal("true", dialog.GetAttribute("aria-modal"));
        Assert.NotNull(labelId);
        Assert.Equal("Keyboard shortcuts", component.Find($"#{labelId}").TextContent);
    }

    [Fact]
    public void ClosesWhenNativeEscapeDismissalCompletes()
    {
        BpmnEditorSession? session = null;
        var component = this.RenderSeeded<FluentBpmnShortcutHelp>(seed => seed.ShowHelp()
            .Bindings(KeyboardOptions.Default.Bindings)
            .Session(s => session = s));

        var dialog = component.Find("fluent-dialog");
        dialog.TriggerEvent("ondialogtoggle", new DialogToggleEventArgs
        {
            Id = dialog.Id,
            Type = "toggle",
            OldState = "open",
            NewState = "closed",
        });

        Assert.False(session!.State.ShortcutHelpVisible);
    }

    [Fact]
    public void IgnoresOtherKeys()
    {
        BpmnEditorSession? session = null;
        var component = this.RenderSeeded<FluentBpmnShortcutHelp>(seed => seed.ShowHelp()
            .Bindings(KeyboardOptions.Default.Bindings)
            .Session(s => session = s));

        component.Find("fluent-dialog").KeyDown(new KeyboardEventArgs { Key = "a" });

        Assert.True(session!.State.ShortcutHelpVisible);
    }

    [Fact]
    public void ClosesOnTheBackdrop()
    {
        BpmnEditorSession? session = null;
        var component = this.RenderSeeded<FluentBpmnShortcutHelp>(seed => seed.ShowHelp()
            .Bindings(KeyboardOptions.Default.Bindings)
            .Session(s => session = s));

        component.InvokeAsync(() => component.FindComponent<FluentBpmnDialog>().Instance.OnDismiss.InvokeAsync());

        Assert.False(session!.State.ShortcutHelpVisible);
    }

    [Fact]
    public void ClosesOnTheButton()
    {
        BpmnEditorSession? session = null;
        var component = this.RenderSeeded<FluentBpmnShortcutHelp>(seed => seed.ShowHelp()
            .Bindings(KeyboardOptions.Default.Bindings)
            .Session(s => session = s));

        component.Find(".bpmn-shortcut-help__close").Click();

        Assert.False(session!.State.ShortcutHelpVisible);
    }
}
