using Bunit;
using Hasseware.FluentUI.AspNetCore.Components.Bpmn;
using Microsoft.FluentUI.AspNetCore.Components;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Components;

public sealed class FluentBpmnDialogTests : FluentTestContext
{
    private const string Show = "Microsoft.FluentUI.Blazor.Components.Dialog.Show";
    private const string Hide = "Microsoft.FluentUI.Blazor.Components.Dialog.Hide";

    [Fact]
    public void UsesBodyTemplatesAndOneHeaderClose()
    {
        var component = Render<FluentBpmnDialog>(parameters => parameters
            .Add(p => p.TitleTemplate, "<h2>Title</h2>")
            .Add(p => p.ChildContent, "<p>Body</p>")
            .Add(p => p.ActionTemplate, "<button>Save</button>"));
        var body = component.FindComponent<FluentDialogBody>().Instance;
        Assert.NotNull(body.TitleTemplate);
        Assert.NotNull(body.TitleActionTemplate);
        Assert.NotNull(body.ActionTemplate);
        Assert.Equal("Close", Assert.Single(component.FindAll(".bpmn-dialog__close")).TextContent.Trim());
        Assert.Empty(component.FindAll("span[tabindex]"));
    }

    [Fact]
    public void HiddenDialogDoesNotOpenNativeModal()
    {
        var component = Render<FluentBpmnDialog>(parameters => parameters.Add(p => p.Visible, false));

        Assert.Empty(component.FindAll("fluent-dialog"));
        Assert.Empty(JSInterop.Invocations[Show]);
    }

    [Fact]
    public void RerenderingVisibleDialogDoesNotShowItAgain()
    {
        var component = Render<FluentBpmnDialog>();

        component.Render(parameters => parameters.Add(p => p.Class, "updated"));

        Assert.Single(JSInterop.Invocations[Show]);
    }

    [Fact]
    public void HidingClosesNativeDialogBeforeRemovingHostWithoutDismissing()
    {
        var dismissals = 0;
        var component = Render<FluentBpmnDialog>(parameters => parameters
            .Add(p => p.OnDismiss, () => dismissals++));
        var id = component.Find("fluent-dialog").Id;

        component.Render(parameters => parameters.Add(p => p.Visible, false));

        Assert.Equal(id, Assert.Single(JSInterop.Invocations[Hide]).Arguments[0]);
        Assert.Empty(component.FindAll("fluent-dialog"));
        Assert.Equal(0, dismissals);
    }

    [Fact]
    public void HostIsRetainedUntilNativeHideCompletes()
    {
        var hide = JSInterop.SetupVoid(Hide, _ => true);
        var component = Render<FluentBpmnDialog>();

        component.Render(parameters => parameters.Add(p => p.Visible, false));
        component.Render(parameters => parameters.Add(p => p.Class, "still-closing"));

        Assert.Single(component.FindAll("fluent-dialog"));
        hide.SetVoidResult();
        component.WaitForAssertion(() => Assert.Empty(component.FindAll("fluent-dialog")));
    }

    [Fact]
    public void VisibilityChangeDuringShowClosesAfterShowCompletes()
    {
        var show = JSInterop.SetupVoid(Show, _ => true);
        var component = Render<FluentBpmnDialog>();

        component.Render(parameters => parameters.Add(p => p.Visible, false));

        Assert.Empty(JSInterop.Invocations[Hide]);
        show.SetVoidResult();
        component.WaitForAssertion(() => Assert.Single(JSInterop.Invocations[Hide]));
        component.WaitForAssertion(() => Assert.Empty(component.FindAll("fluent-dialog")));
    }

    [Fact]
    public void ReopeningCreatesAndShowsAnotherNativeDialog()
    {
        var component = Render<FluentBpmnDialog>();
        component.Render(parameters => parameters.Add(p => p.Visible, false));

        component.Render(parameters => parameters.Add(p => p.Visible, true));

        Assert.Equal(2, JSInterop.Invocations[Show].Count);
        Assert.Single(component.FindAll("fluent-dialog"));
    }

    [Fact]
    public void NativeCloseNotifiesOwnerOnceWithoutClosingAgain()
    {
        var dismissals = 0;
        IRenderedComponent<FluentBpmnDialog>? component = null;
        component = Render<FluentBpmnDialog>(parameters => parameters.Add(p => p.OnDismiss, () =>
        {
            dismissals++;
            component!.Render(update => update.Add(p => p.Visible, false));
        }));
        var id = component.Find("fluent-dialog").Id;

        component.Find("fluent-dialog").TriggerEvent("ondialogtoggle", new DialogToggleEventArgs
        {
            Id = id,
            Type = "toggle",
            OldState = "open",
            NewState = "closed",
        });

        Assert.Equal(1, dismissals);
        Assert.Empty(JSInterop.Invocations[Hide]);
        Assert.Empty(component.FindAll("fluent-dialog"));
    }

    [Theory]
    [InlineData("nested-dialog", "closed")]
    [InlineData(null, "open")]
    public void UnrelatedToggleDoesNotDismiss(string? eventId, string newState)
    {
        var dismissals = 0;
        var component = Render<FluentBpmnDialog>(parameters => parameters
            .Add(p => p.OnDismiss, () => dismissals++));

        component.Find("fluent-dialog").TriggerEvent("ondialogtoggle", new DialogToggleEventArgs
        {
            Id = eventId ?? component.Find("fluent-dialog").Id,
            Type = "toggle",
            OldState = "closed",
            NewState = newState,
        });

        Assert.Equal(0, dismissals);
        Assert.Single(JSInterop.Invocations[Show]);
    }
}
