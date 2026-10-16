using Bunit;

using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.WebMcp.Tests.Support;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace Hasseware.AspNetCore.Components.Bpmn.WebMcp.Tests;

/// <summary>The component that offers an editor to the browser's agents, and does nothing where there are none.</summary>
public sealed class BpmnWebMcpComponentTests : EditorContext
{
    private static readonly string[] AllTools =
    [
        "bpmn_get_diagram", "bpmn_describe_element", "bpmn_get_selection", "bpmn_validate", "bpmn_export", "bpmn_list_creatable",
    ];

    private static BpmnWebMcp Component(IRenderedComponent<BpmnEditorScope> scope) => scope.FindComponent<BpmnWebMcp>().Instance;

    [Fact]
    public void DrawsNothing()
    {
        var (scope, _) = RenderWithBrowser();

        Assert.Empty(scope.FindComponent<BpmnWebMcp>().Markup);
    }

    [Fact]
    public void RegistersTheReadOnlyToolsAfterTheFirstRender()
    {
        var (scope, browser) = RenderWithBrowser();

        scope.WaitForAssertion(() => Assert.Equal(BpmnWebMcpState.Registered, Component(scope).Status));
        Assert.Equal(AllTools, browser.Module.Registered.Select(tool => tool.Name));
        Assert.Equal(AllTools, Component(scope).RegisteredTools);
    }

    [Fact]
    public async Task RunsAnAgentsCallAgainstTheOpenEditor()
    {
        var (scope, browser) = RenderWithBrowser();
        scope.WaitForAssertion(() => Assert.Equal(BpmnWebMcpState.Registered, Component(scope).Status));

        var result = await browser.Module.CallAsync("bpmn_get_diagram");

        Assert.True(result.GetProperty("ok").GetBoolean());
        Assert.Contains(result.GetProperty("data").GetProperty("elements").EnumerateArray(), element => element.GetProperty("id").GetString() == "review");
    }

    [Fact]
    public async Task SeesWhatTheUserSelected()
    {
        var (scope, browser) = RenderWithBrowser();
        scope.WaitForAssertion(() => Assert.Equal(BpmnWebMcpState.Registered, Component(scope).Status));
        await scope.InvokeAsync(() => scope.Instance.Session.SetSelectionAsync(["review"]));

        var result = await browser.Module.CallAsync("bpmn_get_selection");

        Assert.Equal("review", Assert.Single(result.GetProperty("data").GetProperty("elements").EnumerateArray()).GetProperty("id").GetString());
    }

    [Fact]
    public async Task OffersNoWayToChangeTheDiagram()
    {
        var (scope, browser) = RenderWithBrowser();
        scope.WaitForAssertion(() => Assert.Equal(BpmnWebMcpState.Registered, Component(scope).Status));

        var result = await browser.Module.CallAsync("bpmn_set_property", """{"elementId":"review","name":"name","value":"Hacked"}""");

        Assert.Equal("NOT-FOUND", result.GetProperty("code").GetString());
        Assert.Equal("Review", scope.Instance.Session.EditedDocument!.FindFlowNode("review")!.Name);
    }

    [Fact]
    public void DoesNothingQuietlyWhereTheBrowserHasNoWebMcp()
    {
        var raised = 0;
        var (scope, browser) = RenderWithBrowser(new FakeBrowser(supported: false), parameters => parameters.Add(item => item.OnUnavailable, EventCallback.Factory.Create(this, () => raised++)));

        scope.WaitForAssertion(() => Assert.Equal(BpmnWebMcpState.Unsupported, Component(scope).Status));
        Assert.Empty(browser.Module.Registered);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void IsUnavailableWhenTheModuleCannotBeLoaded()
    {
        var raised = 0;
        var (scope, _) = RenderWithBrowser(new FakeBrowser { ImportFails = true }, parameters => parameters.Add(item => item.OnUnavailable, EventCallback.Factory.Create(this, () => raised++)));

        scope.WaitForAssertion(() => Assert.Equal(BpmnWebMcpState.Failed, Component(scope).Status));
        Assert.Equal(1, raised);
    }

    [Fact]
    public void IsUnavailableWhenTheBrowserRefusesEveryTool()
    {
        var browser = new FakeBrowser();
        foreach (var name in AllTools) browser.Module.Refused.Add(name);

        var (scope, _) = RenderWithBrowser(browser);

        scope.WaitForAssertion(() => Assert.Equal(BpmnWebMcpState.Failed, Component(scope).Status));
    }

    [Fact]
    public void KeepsTheToolsTheBrowserAcceptedWhenItRefusesOne()
    {
        var browser = new FakeBrowser();
        browser.Module.Refused.Add("bpmn_export");

        var (scope, _) = RenderWithBrowser(browser);

        scope.WaitForAssertion(() => Assert.Equal(BpmnWebMcpState.Registered, Component(scope).Status));
        Assert.DoesNotContain("bpmn_export", Component(scope).RegisteredTools);
        Assert.Equal(AllTools.Length - 1, Component(scope).RegisteredTools.Count);
    }

    [Fact]
    public async Task WithdrawsTheToolsWhenTheEditorGoesAway()
    {
        var (scope, browser) = RenderWithBrowser();
        scope.WaitForAssertion(() => Assert.Equal(BpmnWebMcpState.Registered, Component(scope).Status));

        await DisposeComponentsAsync();

        Assert.Equal(1, browser.Module.Unregistrations);
        Assert.Equal(1, browser.Module.Disposals);
        Assert.Empty(browser.Module.Registered);
    }

    [Fact]
    public async Task WithdrawsToolsThatWereBeingRegisteredWhenTheEditorWentAway()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var browser = new FakeBrowser();
        browser.Module.RegisterGate = gate;
        RenderWithBrowser(browser);
        await Task.Delay(50, Xunit.TestContext.Current.CancellationToken);

        var disposing = DisposeComponentsAsync();
        gate.SetResult();
        await disposing;

        Assert.Equal(1, browser.Module.Unregistrations);
        Assert.Empty(browser.Module.Registered);
    }

    [Fact]
    public void ReportsAnInstanceNameNoToolNameCanCarryInsteadOfFailingTheEditor()
    {
        var (scope, _) = RenderWithBrowser(parameters: parameters => parameters.Add(item => item.InstanceName, "my editor"));

        scope.WaitForAssertion(() => Assert.Equal(BpmnWebMcpState.Failed, Component(scope).Status));
    }

    [Fact]
    public async Task RefusesACallThatArrivesAfterTheEditorWentAway()
    {
        var (scope, browser) = RenderWithBrowser();
        scope.WaitForAssertion(() => Assert.Equal(BpmnWebMcpState.Registered, Component(scope).Status));
        var module = browser.Module;
        var reference = module.Reference!.Value;

        await DisposeComponentsAsync();

        var result = System.Text.Json.JsonDocument.Parse(await reference.ExecuteAsync("late", "bpmn_validate", "{}")).RootElement;
        Assert.False(result.GetProperty("ok").GetBoolean());
        Assert.Equal(BpmnWebMcpBridge.Cancelled, result.GetProperty("code").GetString());
    }

    [Fact]
    public void AppliesAToolPrefixFromTheComponent()
    {
        var (scope, browser) = RenderWithBrowser(parameters: parameters => parameters.Add(item => item.ToolPrefix, "diagram_"));

        scope.WaitForAssertion(() => Assert.Equal(BpmnWebMcpState.Registered, Component(scope).Status));
        Assert.All(browser.Module.Registered, tool => Assert.StartsWith("diagram_", tool.Name, StringComparison.Ordinal));
    }

    [Fact]
    public void AppliesTheOptionsPrefixAndDisabledTools()
    {
        Services.AddBpmnWebMcp(options =>
        {
            options.ToolPrefix = "agent_";
            options.DisabledTools.Add("bpmn_export");
        });

        var (scope, browser) = RenderWithBrowser();

        scope.WaitForAssertion(() => Assert.Equal(BpmnWebMcpState.Registered, Component(scope).Status));
        Assert.DoesNotContain(browser.Module.Registered, tool => tool.Name == "agent_export");
        Assert.All(browser.Module.Registered, tool => Assert.StartsWith("agent_", tool.Name, StringComparison.Ordinal));
    }

    [Fact]
    public void GivesASecondEditorOnThePageNamesOfItsOwn()
    {
        var browser = new FakeBrowser();
        Services.AddSingleton<IJSRuntime>(browser);

        var editor = Render<BpmnEditorScope>(parameters => parameters
            .Add(scope => scope.Document, Documents.Simple())
            .AddChildContent(builder =>
            {
                builder.OpenComponent<BpmnWebMcp>(0);
                builder.CloseComponent();
                builder.OpenComponent<BpmnWebMcp>(1);
                builder.AddComponentParameter(2, nameof(BpmnWebMcp.InstanceName), "second");
                builder.CloseComponent();
            }));

        editor.WaitForAssertion(() => Assert.Equal(AllTools.Length * 2, browser.Module.Registered.Count));
        var names = browser.Module.Registered.Select(tool => tool.Name).ToList();
        Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
        Assert.Contains("bpmn_get_diagram", names);
        Assert.Contains("bpmn_get_diagram_second", names);
    }
}
