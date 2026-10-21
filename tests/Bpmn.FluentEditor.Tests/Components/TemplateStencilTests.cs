using Bunit;
using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Interop;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;
using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions.Templates;
using Hasseware.FluentUI.AspNetCore.Components.Bpmn;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.FluentUI.AspNetCore.Components;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Components;

public sealed class TemplateStencilTests : FluentTestContext
{
    private readonly BpmnOptions _options = new();

    public TemplateStencilTests() => Services.AddSingleton<IOptions<BpmnOptions>>(Options.Create(_options));

    private static readonly BpmnDocument Document = new BpmnDocumentBuilder().Build();
    private static IReadOnlyList<BpmnStencilGroup> Rules => BpmnStencil.GroupsFor(Document, "Process_1", "Diagram_1");

    private static BpmnElementTemplate Template(string id = "http", string name = "HTTP request") => new()
    {
        Id = id,
        Name = name,
        Version = 1,
        AppliesTo = ["bpmn:Task"],
        ElementType = new("bpmn:ServiceTask"),
        Category = new("connectors", "Connectors"),
        Description = "Call an HTTP endpoint"
    };

    private IRenderedComponent<FluentBpmnStencilPanel> Panel(BpmnTemplateOptions _, Action<SeedBuilder>? configure = null) =>
        this.RenderSeeded<FluentBpmnStencilPanel>(seed => { seed.Document(Document); configure?.Invoke(seed); });

    [Fact]
    public void NoRegistryOrDisabledOptionHidesTemplates()
    {
        var options = _options.Templates;
        Assert.Empty(Panel(options).FindAll(".bpmn-stencil__templates"));
        options.Registry.Add(Template());
        options.ShowInStencil = false;
        Assert.Empty(Panel(options).FindAll(".bpmn-stencil__templates"));
    }

    [Fact]
    public void GroupsLatestCompatibleVersionsAndSearchesDescriptionAndCategory()
    {
        var options = _options.Templates;
        options.Engines["camunda"] = "8.6.0";
        options.Registry.Add(Template());
        options.Registry.Add(Template() with { Version = 2, Name = "New HTTP" });
        options.Registry.Add(Template() with { Version = 3, Deprecated = new("Old") });
        options.Registry.Add(Template("future") with { Engines = new Dictionary<string, string> { ["camunda"] = ">=9.0.0" } });
        options.Registry.Add(Template("other", "Other") with { Category = null, Description = null });
        var panel = Panel(options);
        Assert.Equal(2, panel.FindAll(".bpmn-stencil__template-tool").Count);
        Assert.Equal(["Connectors", "Other templates"], panel.FindAll(".bpmn-stencil__templates h3").Select(e => e.TextContent).ToArray());
        panel.FindComponent<FluentTextInput>().InvokeAsync(() => panel.FindComponent<FluentTextInput>().Instance.ValueChanged.InvokeAsync("endpoint"));
        Assert.Equal("New HTTP", Assert.Single(panel.FindAll(".bpmn-stencil__template-tool")).TextContent.Trim());
        panel.FindComponent<FluentTextInput>().InvokeAsync(() => panel.FindComponent<FluentTextInput>().Instance.ValueChanged.InvokeAsync("connectors"));
        Assert.Single(panel.FindAll(".bpmn-stencil__template-tool"));
        panel.FindComponent<FluentTextInput>().InvokeAsync(() => panel.FindComponent<FluentTextInput>().Instance.ValueChanged.InvokeAsync("missing"));
        Assert.Empty(panel.FindAll(".bpmn-stencil__template-tool"));
        Assert.Contains("No matching templates", panel.Find("[role=status]").TextContent);
    }

    [Fact]
    public void IconsNeverFetchRemoteUrlsAndBaseTypesFallBackToTask()
    {
        var options = _options.Templates;
        options.Registry.Add(Template() with { Icon = "https://example.invalid/icon.svg" });
        options.Registry.Add(Template("abstract", "Abstract") with { ElementType = null, AppliesTo = ["bpmn:Activity"], Icon = "data:image/png;base64,AAAA" });
        var panel = Panel(options);
        Assert.Single(panel.FindAll(".bpmn-stencil__template-tool svg"));
        Assert.Equal("data:image/png;base64,AAAA", Assert.Single(panel.FindAll(".bpmn-stencil__template-tool img")).GetAttribute("src"));
        var entries = BpmnTemplateStencil.GroupsFor(options, Rules).SelectMany(g => g.Entries).ToArray();
        Assert.Equal(BpmnFlowNodeType.Task, entries.Single(e => e.Template!.Id == "abstract").Type);
        Assert.Equal(BpmnFlowNodeType.ServiceTask, entries.Single(e => e.Template!.Id == "http").Type);
        var fresh = new BpmnTemplateOptions();
        fresh.Registry.Add(Template() with { ElementType = null, AppliesTo = ["bpmn:UserTask"] });
        Assert.Equal(BpmnFlowNodeType.UserTask, BpmnTemplateStencil.GroupsFor(fresh, Rules)[0].Entries[0].Type);
    }

    [Theory]
    [InlineData("bpmn:Process")]
    [InlineData("bpmn:SequenceFlow")]
    [InlineData("bpmn:Collaboration")]
    [InlineData("bpmn:Participant")]
    [InlineData("bpmn:RootElement")]
    [InlineData("bpmn:Event")]
    [InlineData("bpmn:Gateway")]
    public void NonCreatableTargetsAreExcluded(string appliesTo)
    {
        var template = Template() with { ElementType = null, AppliesTo = [appliesTo] };
        Assert.Empty(BpmnTemplateStencil.GroupsFor(Add(template), Rules));
        Assert.Empty(Panel(Add(template)).FindAll(".bpmn-stencil__template-tool"));
    }

    [Theory]
    [InlineData("bpmn:Process", "bpmn:Task")]
    [InlineData("bpmn:Task", "bpmn:ServiceTask")]
    [InlineData("bpmn:ServiceTask", "bpmn:Process")]
    [InlineData("bpmn:Activity", "bpmn:Activity")]
    public void InvalidOrInapplicableElementTypeDoesNotFallBack(string elementType, string appliesTo)
    {
        var template = Template() with { ElementType = new(elementType), AppliesTo = [appliesTo] };
        Assert.Empty(BpmnTemplateStencil.GroupsFor(Add(template), Rules));
    }

    [Theory]
    [InlineData("bpmn:Task", BpmnFlowNodeType.Task)]
    [InlineData("bpmn:Activity", BpmnFlowNodeType.Task)]
    [InlineData("bpmn:FlowNode", BpmnFlowNodeType.Task)]
    [InlineData("bpmn:ServiceTask", BpmnFlowNodeType.ServiceTask)]
    public void CreatableAndAbstractTargetsCreateApplicableNodes(string appliesTo, BpmnFlowNodeType type)
    {
        var template = Template() with { ElementType = null, AppliesTo = [appliesTo] };
        var entry = Assert.Single(Assert.Single(BpmnTemplateStencil.GroupsFor(Add(template), Rules)).Entries);
        Assert.Equal(type, entry.Type);
        Assert.Equal(type, ApplyEntry(entry).Type);
    }

    [Fact]
    public void FirstCreatableAppliesToTakesPrecedenceOverAbstractFallback()
    {
        var template = Template() with { ElementType = null, AppliesTo = ["bpmn:Process", "bpmn:Activity", "bpmn:ServiceTask", "bpmn:UserTask"] };
        var entry = Assert.Single(Assert.Single(BpmnTemplateStencil.GroupsFor(Add(template), Rules)).Entries);
        Assert.Equal(BpmnFlowNodeType.ServiceTask, ApplyEntry(entry).Type);
    }

    [Fact]
    public void MessageCatchTemplateCreatesItsEventDefinition()
    {
        var template = Template() with
        {
            AppliesTo = ["bpmn:CatchEvent"],
            ElementType = new("bpmn:IntermediateCatchEvent", "bpmn:MessageEventDefinition")
        };
        var entry = Assert.Single(Assert.Single(BpmnTemplateStencil.GroupsFor(Add(template), Rules)).Entries);
        Assert.Equal(BpmnFlowNodeType.IntermediateCatchEvent, entry.Type);
        Assert.Equal(BpmnEventDefinitionType.Message, entry.EventDefinition);
        var node = ApplyEntry(entry);
        Assert.Equal(BpmnFlowNodeType.IntermediateCatchEvent, node.Type);
        Assert.Equal(BpmnEventDefinitionType.Message, node.EventDefinition);
    }

    [Fact]
    public void EveryDemoTemplateStencilEntryAppliesSuccessfully()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "BlazorBpmnEditor.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        var options = _options.Templates;
        foreach (var path in Directory.GetFiles(Path.Combine(root.FullName, "playground", "Bpmn.Demo.Client", "wwwroot", "templates"), "*.json"))
        {
            var loaded = BpmnElementTemplateLoader.Load(File.ReadAllText(path));
            Assert.Empty(loaded.Errors);
            foreach (var template in loaded.Templates) options.Registry.Add(template);
        }
        var entries = BpmnTemplateStencil.GroupsFor(options, Rules).SelectMany(group => group.Entries).ToArray();
        Assert.Equal(3, entries.Length);
        Assert.All(entries, entry => ApplyEntry(entry));
    }

    private static BpmnFlowNode ApplyEntry(BpmnStencilEntry entry)
    {
        Assert.True(entry.IsEnabled);
        var create = new CreateElementCommand("Template_new", entry.Type, "Process_1", new Bounds(500, 400, 100, 80),
            EventDefinition: entry.EventDefinition);
        var result = BpmnDocumentEditor.Apply(Document, BpmnTemplateStencil.CreateCommand(create, entry.Template!));
        Assert.True(result.IsApplied, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        var node = Assert.Single(result.Document.FlowNodes);
        Assert.Equal(entry.Template!.Id, node.Extras.ForeignAttributes.Values.Single(value => value == entry.Template.Id));
        return node;
    }

    private BpmnTemplateOptions Add(BpmnElementTemplate template)
    {
        var options = _options.Templates;
        options.Registry.Add(template);
        return options;
    }

    [Fact]
    public void ClickKeyboardAndDragUseSameTemplateEntryAndDisabledToolsDoNothing()
    {
        var options = Add(Template());
        BpmnEditorSession? session = null;
        BpmnEditorSession? disabledSession = null;
        var activated = new List<BpmnStencilEntry>();
        var panel = Panel(options, seed => seed.OnActivateTool(entry => activated.Add(entry)).Session(s => session = s));
        var tool = panel.Find(".bpmn-stencil__template-tool");
        tool.Click();
        tool.KeyDown(new KeyboardEventArgs { Key = "Enter" });
        tool.KeyDown(new KeyboardEventArgs { Key = " " });
        tool.DragStart();
        Assert.Equal(3, activated.Count);
        Assert.Equal("http", session!.State.ArmedTool!.Template!.Id);
        Assert.All(activated, entry => Assert.Equal("http", entry.Template!.Id));
        Assert.Equal("Call an HTTP endpoint", tool.GetAttribute("title"));
        var disabled = this.RenderSeeded<FluentBpmnStencilPanel>(seed => seed.OnActivateTool(entry => activated.Add(entry)).Session(s => disabledSession = s));
        var disabledTool = disabled.Find(".bpmn-stencil__template-tool");
        Assert.Equal("true", disabledTool.GetAttribute("aria-disabled"));
        Assert.Equal("-1", disabledTool.GetAttribute("tabindex"));
        disabledTool.Click();
        disabledTool.KeyDown(new KeyboardEventArgs { Key = "Enter" });
        disabledTool.DragStart();
        Assert.Equal(3, activated.Count);
        Assert.Null(disabledSession!.State.ArmedTool);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EditorCreatesTemplateInOneUndoStepForActivationAndDrop(bool drop)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentBpmnEditor();
        var module = JSInterop.SetupModule(BpmnInteropContract.ModulePath);
        var canvas = module.SetupModule("initialize", _ => true);
        canvas.Setup<CanvasInitResult>("getInitResult", _ => true)
            .SetResult(new("instance-1", BpmnInteropContract.Version, 1, true, null));
        canvas.Setup<DocumentLoaded?>("loadDocument", _ => true).SetResult(new("Diagram_1", 0, 0, []));
        canvas.Setup<DropTarget?>(drop ? "resolveDrop" : "resolveCreationPoint", _ => true)
            .SetResult(new(new Bounds(500, 400, 100, 80), "Process_1", null));
        var template = Template();
        var entry = BpmnTemplateStencil.GroupsFor(Add(template), Rules)[0].Entries[0];
        var create = new CreateElementCommand("Task_new", entry.Type, "Process_1", new Bounds(500, 400, 100, 80));
        var compound = BpmnTemplateStencil.CreateCommand(create, template);
        Assert.Same(create, compound.Commands[0]);
        Assert.Equal(create.Id, Assert.IsType<ApplyElementTemplateCommand>(compound.Commands[1]).ElementId);
        var editor = Render<FluentBpmnEditor>(p => p.Add(c => c.Document, Document));
        var result = await editor.InvokeAsync(() => drop ? editor.Instance.CreateAtAsync(entry, 500, 400) : editor.Instance.CreateAsync(entry));
        Assert.NotNull(result);
        Assert.True(result.IsApplied, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        var node = Assert.Single(editor.Instance.EditedDocument!.FlowNodes);
        Assert.Equal(BpmnFlowNodeType.ServiceTask, node.Type);
        Assert.Equal("http", node.Extras.ForeignAttributes["{http://camunda.org/schema/zeebe/1.0}modelerTemplate"]);
        await editor.InvokeAsync(editor.Instance.UndoAsync);
        Assert.Empty(editor.Instance.EditedDocument!.FlowNodes);
        Assert.False(editor.Instance.CanUndo);
    }
}
