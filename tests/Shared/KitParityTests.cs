using System.Globalization;
using System.Resources;
using AngleSharp.Dom;
using Bunit;
using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Interop;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;
using Hasseware.AspNetCore.Components.Bpmn.Resources;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Components;

// Linked into both kit projects: every assertion runs unchanged against both service registries.
public sealed class KitParityTests : BunitContext
{
    private static readonly ResourceManager Strings = new(typeof(BpmnStrings));
    private static readonly Anchor Location = new(new Bounds(200, 120, 100, 80), new Bounds(200, 120, 100, 80));
    private readonly BunitJSModuleInterop _canvas;

    public KitParityTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        KitFixture.Register(Services);
        var module = JSInterop.SetupModule(BpmnInteropContract.ModulePath);
        _canvas = module.SetupModule("initialize", _ => true);
        _canvas.Setup<CanvasInitResult>("getInitResult", _ => true).SetResult(new("parity", BpmnInteropContract.Version, 1, true, null));
        _canvas.Setup<DocumentLoaded?>("loadDocument", _ => true).SetResult(new("Diagram_1", 1, 0, []));
        _canvas.Setup<DropTarget?>("resolveCreationPoint", _ => true).SetResult(new(new Bounds(400, 120, 100, 80), "Process_1", null));
        _canvas.Setup<DropTarget?>("resolveAppendPoint", _ => true).SetResult(new(new Bounds(400, 120, 100, 80), "Process_1", null));
    }

    public static TheoryData<BpmnEditorPart, string, string> AccessibleParts => new()
    {
        { BpmnEditorPart.Toolbar, "toolbar", "Toolbar.AriaLabel" },
        { BpmnEditorPart.Stencil, "listbox", "StencilPanel.AriaLabel" },
        { BpmnEditorPart.PropertyPanel, "region", "PropertyPanel.AriaLabel" },
        { BpmnEditorPart.ContextMenu, "menu", "ContextMenu.CanvasTitle" },
        { BpmnEditorPart.ContextualActions, "toolbar", "ContextualActions.AriaLabel" },
        { BpmnEditorPart.ValidationPanel, "region", "ValidationPanel.AriaLabel" },
        { BpmnEditorPart.ShortcutHelp, "dialog", "ShortcutHelp.Title" },
    };

    [Theory, MemberData(nameof(AccessibleParts))]
    public async Task PartsExposeSameAccessibleRolesAndCoreNames(BpmnEditorPart part, string role, string key)
    {
        var scope = this.Render<BpmnPart>(p => p.Add(c => c.Part, part),
            scope: p => p.Add(c => c.Document, Document()), canvas: true);
        await OpenPartAsync(scope.Scope, part);
        // A named section has the implicit ARIA region role; buttons have implicit button roles.
        var element = part == BpmnEditorPart.PropertyPanel ? scope.Cut.Find("section[aria-label]") : scope.Cut.Find($"[role='{role}']");
        Assert.Equal(CoreText(key), AccessibleName(scope.Cut, element));
    }

    [Theory, MemberData(nameof(AccessibleParts))]
    public async Task DefaultAccessibleNamesUseCoreLocalizerUnderHostCulture(BpmnEditorPart part, string role, string key)
    {
        Services.AddSingleton<IStringLocalizer<BpmnStrings>, TranslatedCoreStrings>();
        var before = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de");
            var scope = this.Render<BpmnPart>(p => p.Add(c => c.Part, part), scope: p => p.Add(c => c.Document, Document()), canvas: true);
            await OpenPartAsync(scope.Scope, part);
            var element = part == BpmnEditorPart.PropertyPanel ? scope.Cut.Find("section[aria-label]") : scope.Cut.Find($"[role='{role}']");
            Assert.Equal($"de:{key}", AccessibleName(scope.Cut, element));
        }
        finally { CultureInfo.CurrentUICulture = before; }
    }

    [Fact]
    public void ToolbarButtonsExposeSameAccessibleNames()
    {
        var editor = RenderEditor();
        foreach (var name in new[] { "Zoom out", "Zoom in", "Reset zoom", "Fit to content", "Undo", "Redo", "Show keyboard shortcuts" })
        {
            var button = Assert.Single(editor.FindAll($"[aria-label='{name}']"));
            Assert.True(button.LocalName is "button" or "fluent-button", $"Expected a button host, got {button.LocalName}.");
        }
        Assert.Equal("100%", editor.Find(".bpmn-toolbar__zoom").TextContent);
    }

    [Theory]
    [InlineData("Background")]
    [InlineData("Border")]
    [InlineData("Text")]
    public async Task ToolbarColorChangesSupportHistoryAndReset(string channel)
    {
        var editor = RenderEditor();
        var session = Session(editor);
        await editor.InvokeAsync(() => session.SetSelectionAsync(["Task_1"]));

        await KitFixture.ChangeColorAsync(editor, channel, "#123456");

        string? ReadColor() => channel switch
        {
            "Background" => session.EditedDocument!.Diagrams[0].Plane.Shapes[0].BackgroundColor,
            "Border" => session.EditedDocument!.Diagrams[0].Plane.Shapes[0].BorderColor,
            _ => session.EditedDocument!.Diagrams[0].Plane.Shapes[0].Label?.Color,
        };
        Assert.Equal("#123456", ReadColor());
        Assert.Contains("#123456", editor.Find(".bpmn-toolbar").TextContent, StringComparison.Ordinal);
        await editor.InvokeAsync(session.UndoAsync);
        Assert.Null(ReadColor());
        await editor.InvokeAsync(session.RedoAsync);
        Assert.Equal("#123456", ReadColor());
        var label = channel switch { "Background" => "Fill color", "Border" => "Border / line color", _ => "Text color" };
        editor.Find($"[aria-label='Reset {label}']").Click();
        Assert.Null(ReadColor());
    }

    [Fact]
    public async Task ToolbarColorsTrackMixedSelectionAndReadOnlyState()
    {
        var document = new BpmnDocumentBuilder("Process_1")
            .Node("Task_1", BpmnFlowNodeType.UserTask, new Bounds(200, 120, 100, 80), "Work")
            .Node("Task_2", BpmnFlowNodeType.Task, new Bounds(400, 120, 100, 80), "Other").Build();
        var parameters = new Dictionary<string, object> { ["Document"] = document };
        var editor = Render<DynamicComponent>(p => p.Add(c => c.Type, KitFixture.Composition)
            .Add(c => c.Parameters, parameters));
        var session = Session(editor);
        Assert.True(Button(editor, "Reset Fill color").HasAttribute("disabled"));
        await editor.InvokeAsync(() => session.SetSelectionAsync(["Task_1"]));
        await KitFixture.ChangeColorAsync(editor, "Background", "#123456");
        await editor.InvokeAsync(() => session.SetSelectionAsync(["Task_1", "Task_2"]));
        Assert.Contains("Mixed", editor.Find(".bpmn-toolbar").TextContent, StringComparison.Ordinal);
        await KitFixture.ChangeColorAsync(editor, "Background", "#abcdef");
        Assert.All(session.EditedDocument!.Diagrams[0].Plane.Shapes, shape => Assert.Equal("#abcdef", shape.BackgroundColor));
        parameters["Document"] = session.EditedDocument;
        parameters["IsReadOnly"] = true;
        editor.Render(p => p.Add(c => c.Type, KitFixture.Composition).Add(c => c.Parameters, parameters));
        Assert.True(Button(editor, "Reset Fill color").HasAttribute("disabled"));
    }

    [Fact]
    public async Task ToolbarColorsRejectShorthandWithoutRecordingAnEdit()
    {
        var editor = RenderEditor();
        var session = Session(editor);
        await editor.InvokeAsync(() => session.SetSelectionAsync(["Task_1"]));

        await KitFixture.ChangeColorAsync(editor, "Background", "#abc");

        Assert.Contains("#RRGGBB", editor.Find(".bpmn-toolbar [role='alert']").TextContent, StringComparison.Ordinal);
        Assert.False(session.State.CanUndo);
        Assert.Null(session.EditedDocument!.Diagrams[0].Plane.Shapes[0].BackgroundColor);
    }

    [Fact]
    public async Task DynamicBuiltInCaptionsUseCoreResourcesInBothKits()
    {
        Services.AddSingleton<IStringLocalizer<BpmnStrings>, TranslatedCoreStrings>();
        var before = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de");
            var editor = RenderEditor();
            Assert.Equal("de:Editor.AriaLabel", editor.Find("section.bpmn-editor").GetAttribute("aria-label"));
            Assert.Contains(editor.FindAll(".bpmn-stencil__title"), heading => heading.TextContent == "de:StencilGroup.Activities");
            Assert.Contains(editor.FindAll(".bpmn-stencil__label"), label => label.TextContent == "de:StencilEntry.Task");

            var session = Session(editor);
            await editor.InvokeAsync(() => session.SetSelectionAsync(["Task_1"]));
            Assert.Equal("Work", editor.Find(".bpmn-properties__title").TextContent);
            Assert.Equal("de:ElementType.userTask", editor.Find(".bpmn-properties__subtitle").TextContent);
            Assert.Contains(editor.FindAll(".bpmn-properties__label"), label => label.TextContent == "de:Property.name");
            Assert.Contains(editor.FindAll(".bpmn-properties__label"), label => label.TextContent == "de:Property.loopCharacteristics");
            Assert.Contains(editor.FindAll("option, fluent-option"),
                option => option.TextContent.Trim() == "de:PropertyChoice.loopCharacteristics.standard");
            Assert.Equal("Work", session.EditedDocument!.FindFlowNode("Task_1")!.Name);

            await editor.InvokeAsync(() => session.HandleContextMenuRequestedAsync(new("element", "Task_1", Location, "pointer", ["Task_1"])));
            Assert.Contains(editor.FindAll("[role='menuitemradio']"), option => option.TextContent.Trim() == "de:ElementType.serviceTask");
        }
        finally { CultureInfo.CurrentUICulture = before; }
    }

    [Fact]
    public void ExplicitCompositionAccessibleNamesRemainHostOwned()
    {
        Services.AddSingleton<IStringLocalizer<BpmnStrings>, TranslatedCoreStrings>();
        var parameters = Parameters(false);
        parameters["EditorLabel"] = "Host editor";
        parameters["StencilLabel"] = "Host palette";
        parameters["PanelLabel"] = "Host properties";
        parameters["AriaLabel"] = "Host canvas";
        var editor = Render<DynamicComponent>(p => p.Add(c => c.Type, KitFixture.Composition).Add(c => c.Parameters, parameters));

        Assert.Equal("Host editor", editor.Find("section.bpmn-editor").GetAttribute("aria-label"));
        Assert.Equal("Host palette", editor.Find(".bpmn-editor__stencil").GetAttribute("aria-label"));
        Assert.Equal("Host properties", editor.Find(".bpmn-editor__panel").GetAttribute("aria-label"));
        Assert.Equal("Host canvas", Session(editor).AriaLabel);
    }

    [Fact]
    public async Task ToolbarUndoRedoDispatchToHistory()
    {
        var editor = RenderEditor();
        var session = Session(editor);
        Assert.True(Button(editor, "Undo").HasAttribute("disabled"));
        Assert.True(Button(editor, "Redo").HasAttribute("disabled"));
        await editor.InvokeAsync(() => session.ApplyAsync(new UpdatePropertiesCommand("Task_1", "name", "Changed")));
        Assert.False(Button(editor, "Undo").HasAttribute("disabled"));
        Button(editor, "Undo").Click();
        Assert.Equal("Work", session.EditedDocument!.FlowNodes.Single().Name);
        Assert.False(Button(editor, "Redo").HasAttribute("disabled"));
        Button(editor, "Redo").Click();
        Assert.Equal("Changed", session.EditedDocument!.FlowNodes.Single().Name);
    }

    [Theory, InlineData("Zoom in", "zoomIn"), InlineData("Zoom out", "zoomOut"), InlineData("Reset zoom", "reset"), InlineData("Fit to content", "fitToContent")]
    public void ToolbarZoomDispatchesToCanvas(string label, string command)
    {
        var editor = RenderEditor();
        Button(editor, label).Click();
        var invocation = Assert.Single(JSInterop.Invocations["setViewport"]);
        Assert.Equal(command, Assert.IsType<ViewportCommand>(invocation.Arguments[0]).Kind);
    }

    [Fact]
    public void StencilKeyboardActivationCreatesElement()
    {
        var editor = RenderEditor();
        var before = Session(editor).EditedDocument!.FlowNodes.Count;
        editor.Find("[role='listbox'] [role='option'][tabindex='0']").KeyDown(new KeyboardEventArgs { Key = "Enter" });
        Assert.Equal(before + 1, Session(editor).EditedDocument!.FlowNodes.Count);
        Assert.Single(JSInterop.Invocations["resolveCreationPoint"]);
        Assert.True(Session(editor).CanUndo);
    }

    [Fact]
    public async Task PropertyEditAppliesToDocumentAndHistory()
    {
        var editor = RenderEditor();
        var session = Session(editor);
        await editor.InvokeAsync(() => session.HandleSelectionChangedAsync(new(["Task_1"], Location)));
        await KitFixture.ChangeNameAsync(editor, "Updated");
        Assert.Equal("Updated", session.EditedDocument!.FlowNodes.Single().Name);
        Assert.True(session.CanUndo);
    }

    [Fact]
    public async Task ContextMenuDeleteDispatchesAndDismisses()
    {
        var editor = RenderEditor();
        var session = Session(editor);
        await editor.InvokeAsync(() => session.HandleContextMenuRequestedAsync(new("element", "Task_1", Location, "pointer", ["Task_1"])));
        var menu = editor.Find("[role='menu']");
        Assert.Equal("Work", menu.GetAttribute("aria-label"));
        var delete = editor.FindAll("[role='menuitem']").Single(element => element.TextContent.Trim() == CoreText("ContextMenu.Command.Delete"));
        delete.Click();
        Assert.Empty(session.EditedDocument!.FlowNodes);
        Assert.Empty(editor.FindAll("[role='menu']"));
        Assert.True(session.CanUndo);
    }

    [Fact]
    public async Task ContextualAppendCreatesNodeAndConnectorAtomically()
    {
        var editor = RenderEditor();
        var session = Session(editor);
        await editor.InvokeAsync(() => session.HandleSelectionChangedAsync(new(["Task_1"], Location)));
        Button(editor, CoreText("ContextualActions.Append.Task")).Click();
        Assert.Equal(2, session.EditedDocument!.FlowNodes.Count);
        var flow = Assert.Single(session.EditedDocument.SequenceFlows);
        Assert.Equal("Task_1", flow.SourceRef);
        Assert.Equal(session.EditedDocument.FlowNodes.Single(node => node.Id != "Task_1").Id, flow.TargetRef);
        await editor.InvokeAsync(session.UndoAsync);
        Assert.Single(session.EditedDocument.FlowNodes);
        Assert.Empty(session.EditedDocument.SequenceFlows);
    }

    [Fact]
    public async Task ReadOnlyDisablesEditsButKeepsInspectionNavigationAndHelp()
    {
        var editor = RenderEditor();
        var session = Session(editor);
        await editor.InvokeAsync(() => session.ApplyAsync(new UpdatePropertiesCommand("Task_1", "name", "Changed")));
        var parameters = new Dictionary<string, object>(editor.Instance.Parameters!) { ["IsReadOnly"] = true };
        editor.Render(p => p.Add(c => c.Parameters, parameters));
        Assert.True(Button(editor, "Undo").HasAttribute("disabled"));
        Assert.True(Button(editor, "Redo").HasAttribute("disabled"));
        Assert.Empty(editor.FindAll(".bpmn-editor__stencil [role='option']"));
        await editor.InvokeAsync(() => session.HandleSelectionChangedAsync(new(["Task_1"], Location)));
        Assert.Empty(editor.FindAll(".bpmn-actions"));
        await KitFixture.ChangeNameAsync(editor, "Forbidden");
        Assert.Equal("Changed", session.EditedDocument!.FlowNodes.Single().Name);
        await editor.InvokeAsync(() => session.HandleContextMenuRequestedAsync(new("element", "Task_1", Location, "pointer", ["Task_1"])));
        Assert.All(editor.FindAll("[role='menuitem']").Where(element => element.TextContent.Trim() == CoreText("ContextMenu.Command.Delete")),
            element => Assert.True(element.HasAttribute("disabled")));
        Assert.False(Button(editor, "Zoom in").HasAttribute("disabled"));
        Button(editor, "Show keyboard shortcuts").Click();
        Assert.Equal(CoreText("ShortcutHelp.Title"), AccessibleName(editor, editor.Find("[role='dialog']")));
    }

    [Fact]
    public async Task PropertyPanelTracksSelectionAndExternalEditAfterSuppressedViewport()
    {
        var editor = RenderEditor();
        var session = Session(editor);
        await editor.InvokeAsync(() => session.SetSelectionAsync(["Task_1"]));
        Assert.Equal("Work", editor.Find(".bpmn-properties__title").TextContent.Trim());
        await editor.InvokeAsync(() => session.HandleViewportChangedAsync(new(1.2, 0, 0)));
        await editor.InvokeAsync(() => session.ApplyAsync(new UpdatePropertiesCommand("Task_1", "name", "External edit")));
        Assert.Equal("External edit", editor.Find(".bpmn-properties__title").TextContent.Trim());
        await editor.InvokeAsync(() => session.SetSelectionAsync([]));
        Assert.Empty(editor.FindAll(".bpmn-properties__title"));
        await editor.InvokeAsync(() => session.SetSelectionAsync(["Task_1"]));
        Assert.Equal("External edit", editor.Find(".bpmn-properties__title").TextContent.Trim());
    }

    [Fact]
    public async Task WidgetCascadeReachesPartChildWithoutPartRender()
    {
        var editor = RenderEditor();
        var session = Session(editor);
        await editor.InvokeAsync(() => session.HandleViewportChangedAsync(new(1.2, 0, 0)));
        await editor.InvokeAsync(() => session.HandleContextMenuRequestedAsync(new("element", "Task_1", Location, "pointer", ["Task_1"])));
        Assert.Equal("Work", editor.Find("[role='menu']").GetAttribute("aria-label"));
        await editor.InvokeAsync(() => session.HandleViewportChangedAsync(new(1.3, 0, 0)));
        Assert.Single(editor.FindAll("[role='menu']"));
        await editor.InvokeAsync(session.CloseContextMenu);
        Assert.Empty(editor.FindAll("[role='menu']"));
    }

    private IRenderedComponent<DynamicComponent> RenderEditor() => Render<DynamicComponent>(p => p.Add(c => c.Type, KitFixture.Composition).Add(c => c.Parameters, Parameters(false)));
    private static Dictionary<string, object> Parameters(bool readOnly) => new() { ["Document"] = Document(), ["IsReadOnly"] = readOnly, ["ShowStencil"] = true };
    private static BpmnEditorSession Session(IRenderedComponent<DynamicComponent> editor) => editor.FindComponent<BpmnEditorScope>().Instance.Session;
    private static IElement Button<T>(IRenderedComponent<T> rendered, string label) where T : IComponent => rendered.Find($"[aria-label='{label}']");
    private static string CoreText(string key) => Strings.GetString(key, CultureInfo.InvariantCulture)!;
    private static string AccessibleName<T>(IRenderedComponent<T> rendered, IElement element) where T : IComponent => element.GetAttribute("aria-label")
        ?? string.Join(" ", (element.GetAttribute("aria-labelledby") ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(id => rendered.Find($"#{id}").TextContent.Trim()));
    private static BpmnDocument Document() => new BpmnDocumentBuilder("Process_1").Node("Task_1", BpmnFlowNodeType.UserTask, new Bounds(200, 120, 100, 80), "Work").Build();

    private static async Task OpenPartAsync(IRenderedComponent<BpmnEditorScope> scope, BpmnEditorPart part)
    {
        var session = scope.Instance.Session;
        if (part == BpmnEditorPart.ContextMenu)
            await scope.InvokeAsync(() => session.HandleContextMenuRequestedAsync(new("canvas", null, Location, "pointer", [])));
        if (part == BpmnEditorPart.ContextualActions)
            await scope.InvokeAsync(() => session.HandleSelectionChangedAsync(new(["Task_1"], Location)));
        if (part == BpmnEditorPart.ShortcutHelp) await scope.InvokeAsync(session.ShowShortcutHelp);
    }

    private sealed class TranslatedCoreStrings : IStringLocalizer<BpmnStrings>
    {
        public LocalizedString this[string name] => new(name, $"{CultureInfo.CurrentUICulture.Name}:{name}");
        public LocalizedString this[string name, params object[] arguments] => this[name];
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }
}
