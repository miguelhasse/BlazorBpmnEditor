using Bunit;
using Hasseware.AspNetCore.Components.Bpmn.Interop;
using Microsoft.Extensions.DependencyInjection;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Components;

public sealed class BpmnDialogTests : BunitContext
{
    private readonly BunitJSModuleInterop _module;

    public BpmnDialogTests()
    {
        Services.AddLocalization();
        _module = JSInterop.SetupModule(BpmnInteropContract.ModulePath);
        _module.SetupVoid("showDialog", _ => true).SetVoidResult();
        _module.SetupVoid("closeDialog", _ => true).SetVoidResult();
    }

    [Fact]
    public void HiddenDialogDoesNotOpenModal()
    {
        var component = Render<BpmnDialog>(parameters => parameters.Add(p => p.Visible, false));
        Assert.Empty(component.FindAll("dialog"));
        Assert.Empty(_module.Invocations);
    }

    [Fact]
    public void RerenderingDoesNotShowAgain()
    {
        var component = Render<BpmnDialog>();
        component.Render(parameters => parameters.Add(p => p.Class, "updated"));
        Assert.Single(_module.Invocations["showDialog"]);
    }

    [Fact]
    public void UsesTitleBodyActionsAndOneHeaderClose()
    {
        var component = Render<BpmnDialog>(parameters => parameters
            .Add(p => p.TitleTemplate, "<h2>Title</h2>")
            .Add(p => p.ChildContent, "<p>Body</p>")
            .Add(p => p.ActionTemplate, "<button>Save</button>"));
        Assert.Equal("Title", component.Find("header h2").TextContent);
        Assert.Equal("Body", component.Find(".bpmn-dialog__body p").TextContent);
        Assert.Equal("Save", component.Find("footer button").TextContent);
        Assert.Equal("Close", Assert.Single(component.FindAll("header button")).TextContent);
        Assert.Empty(component.FindAll("span[tabindex]"));
    }

    [Fact]
    public void HidingClosesBeforeRemovingWithoutDismissal()
    {
        var dismissals = 0;
        var closed = 0;
        var component = Render<BpmnDialog>(parameters => parameters
            .Add(p => p.OnDismiss, () => dismissals++)
            .Add(p => p.OnClosed, () => closed++));
        component.Render(parameters => parameters.Add(p => p.Visible, false));
        Assert.Single(_module.Invocations["closeDialog"]);
        Assert.Empty(component.FindAll("dialog"));
        Assert.Equal(0, dismissals);
        Assert.Equal(1, closed);
    }

    [Fact]
    public void RetainsHostUntilCloseCompletes()
    {
        var close = _module.SetupVoid("closeDialog", _ => true);
        var component = Render<BpmnDialog>();
        component.Render(parameters => parameters.Add(p => p.Visible, false));
        component.Render(parameters => parameters.Add(p => p.Class, "closing"));
        Assert.Single(component.FindAll("dialog"));
        close.SetVoidResult();
        component.WaitForAssertion(() => Assert.Empty(component.FindAll("dialog")));
    }

    [Fact]
    public void VisibilityChangeDuringShowClosesAfterShowCompletes()
    {
        var show = _module.SetupVoid("showDialog", _ => true);
        var component = Render<BpmnDialog>();
        component.Render(parameters => parameters.Add(p => p.Visible, false));
        Assert.Empty(_module.Invocations["closeDialog"]);
        show.SetVoidResult();
        component.WaitForAssertion(() => Assert.Single(_module.Invocations["closeDialog"]));
        component.WaitForAssertion(() => Assert.Empty(component.FindAll("dialog")));
    }

    [Fact]
    public async Task NativeDismissalNotifiesOnceAfterModalRelease()
    {
        var dismissals = 0;
        var closed = 0;
        IRenderedComponent<BpmnDialog>? component = null;
        component = Render<BpmnDialog>(parameters => parameters
            .Add(p => p.OnDismiss, () =>
            {
                dismissals++;
                component!.Render(update => update.Add(p => p.Visible, false));
            })
            .Add(p => p.OnClosed, () => closed++));
        await component.InvokeAsync(component.Instance.DismissAsync);
        await component.InvokeAsync(component.Instance.DismissAsync);
        Assert.Equal(1, dismissals);
        Assert.Equal(1, closed);
        Assert.Empty(_module.Invocations["closeDialog"]);
        Assert.Empty(component.FindAll("dialog"));
    }

    [Fact]
    public void ReopeningShowsNewHost()
    {
        var component = Render<BpmnDialog>();
        component.Render(parameters => parameters.Add(p => p.Visible, false));
        component.Render(parameters => parameters.Add(p => p.Visible, true));
        Assert.Equal(2, _module.Invocations["showDialog"].Count);
        Assert.Single(component.FindAll("dialog"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisposalClosesOnlyMountedModal(bool hideFirst)
    {
        var component = Render<BpmnDialog>();
        if (hideFirst)
            component.Render(parameters => parameters.Add(p => p.Visible, false));

        await component.Instance.DisposeAsync();

        Assert.Single(_module.Invocations["closeDialog"]);
    }
}
