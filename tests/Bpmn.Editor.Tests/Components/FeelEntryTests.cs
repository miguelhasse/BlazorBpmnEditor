using Bunit;
using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Serialization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Components;

public sealed class FeelEntryTests : BunitContext
{
    [Theory]
    [InlineData(BpmnFeelMode.Required)]
    [InlineData(BpmnFeelMode.Optional)]
    public void RegistryUsesFeelBeforeKindButAfterHostOverrides(BpmnFeelMode mode)
    {
        var entry = Entry(mode, "=x");
        var registry = BpmnStandardDefaults.Register(new BpmnPropertyEditorRegistry()).AddEditor<BpmnNumberEntry>(BpmnPropertyEntryKind.Text);
        Assert.Equal(typeof(BpmnFeelEntry), registry.Resolve(entry));
        registry.AddFeelEditor<BpmnTextAreaEntry>();
        Assert.Equal(typeof(BpmnTextAreaEntry), registry.Resolve(entry));
        registry.AddEditor<BpmnCheckboxEntry>(value => value.Feel == mode);
        Assert.Equal(typeof(BpmnCheckboxEntry), registry.Resolve(entry));
        registry.AddEditor<BpmnReadOnlyEntry>("custom");
        Assert.Equal(typeof(BpmnReadOnlyEntry), registry.Resolve(entry with { EditorKey = "custom" }));
    }

    [Fact]
    public void RequiredHidesPrefixAndCommitsExpressionOrEmpty()
    {
        var submitted = new List<string?>();
        var editor = Editor(Entry(BpmnFeelMode.Required, "=original"), submitted.Add);
        Assert.Equal("original", editor.Find("textarea").GetAttribute("value"));
        Assert.Equal("FEEL", editor.Find(".bpmn-properties__feel-badge").TextContent);
        editor.Find("textarea").Change("x");
        Assert.Equal("=x", submitted[^1]);
        editor.Find("textarea").Change("");
        Assert.Null(submitted[^1]);
    }

    [Fact]
    public void OptionalToggleAddsAndRemovesOnlyPrefix()
    {
        var submitted = new List<string?>();
        var editor = Editor(Entry(BpmnFeelMode.Optional, "x + 1"), submitted.Add);
        Assert.Equal("false", editor.Find("button").GetAttribute("aria-pressed"));
        Assert.NotEmpty(editor.FindAll("input[type=text]"));
        editor.Find("button").Click();
        Assert.Equal("=x + 1", submitted[^1]);
        Assert.Equal("true", editor.Find("button").GetAttribute("aria-pressed"));
        Assert.Equal("x + 1", editor.Find("textarea").GetAttribute("value"));
        editor.Find("button").Click();
        Assert.Equal("x + 1", submitted[^1]);
        Assert.Equal("x + 1", editor.Find("input").GetAttribute("value"));
    }

    [Theory]
    [InlineData(BpmnPropertyEntryKind.Text, "text")]
    [InlineData(BpmnPropertyEntryKind.Number, "number")]
    [InlineData(BpmnPropertyEntryKind.Checkbox, "checkbox")]
    public void OptionalLiteralRetainsKindEditor(BpmnPropertyEntryKind kind, string inputType)
    {
        var editor = Editor(Entry(BpmnFeelMode.Optional, "3") with { Kind = kind });
        Assert.Equal(inputType, editor.Find("input").GetAttribute("type"));
    }

    [Theory]
    [InlineData(BpmnFeelMode.Static)]
    [InlineData(BpmnFeelMode.None)]
    public void StaticAndNoneDelegateToKind(BpmnFeelMode mode)
    {
        var editor = Editor(Entry(mode, "3") with { Kind = BpmnPropertyEntryKind.Number });
        Assert.Equal("number", editor.Find("input").GetAttribute("type"));
        Assert.Empty(editor.FindAll("button"));
    }

    [Fact]
    public void ReadOnlyShowsRawValueAndBlocksBothWritesAndToggle()
    {
        var submitted = new List<string?>();
        var editor = Editor(Entry(BpmnFeelMode.Optional, "=x"), submitted.Add, readOnly: true);
        Assert.Equal("=x", editor.Find("textarea").GetAttribute("value"));
        Assert.NotNull(editor.Find("textarea").GetAttribute("readonly"));
        Assert.NotNull(editor.Find("button").GetAttribute("disabled"));
        editor.Find("textarea").Change("y");
        editor.Find("button").Click();
        Assert.Empty(submitted);
    }

    [Theory]
    [InlineData("(x", true)]
    [InlineData("[x}", true)]
    [InlineData("\"hello", true)]
    [InlineData("{x: [1, 2]}", false)]
    [InlineData("\"(\"", false)]
    [InlineData("\"a\\\"b\"", false)]
    public void LintIsAdvisoryAndAccessible(string expression, bool warning)
    {
        string? submitted = null;
        var editor = Editor(Entry(BpmnFeelMode.Required, "=x"), value => submitted = value);
        editor.Find("textarea").Input(expression);
        Assert.Null(submitted);
        Assert.Equal(warning ? 1 : 0, editor.FindAll("[role=status]").Count);
        if (warning)
            Assert.Equal(editor.Find("[role=status]").Id, editor.Find("textarea").GetAttribute("aria-describedby"));
        editor.Find("textarea").Change(expression);
        Assert.Equal("=" + expression, submitted);
    }

    [Fact]
    public void RevisionResetsRefusedToggleAndEditedText()
    {
        var context = Context(Entry(BpmnFeelMode.Optional, "literal"));
        var editor = Render<BpmnFeelEntry>(p => p.AddCascadingValue(StandardPropertyScope.Default).Add(c => c.Context, context));
        editor.Find("button").Click();
        Assert.Single(editor.FindAll("textarea"));
        editor.Render(p => p.Add(c => c.Context, context with { Revision = 1 }));
        Assert.Empty(editor.FindAll("textarea"));
        Assert.Equal("literal", editor.Find("input").GetAttribute("value"));
        context = context with { Entry = Entry(BpmnFeelMode.Required, "=original"), Revision = 2 };
        editor.Render(p => p.Add(c => c.Context, context));
        editor.Find("textarea").Change("refused");
        editor.Render(p => p.Add(c => c.Context, context with { Revision = 3 }));
        Assert.Equal("original", editor.Find("textarea").GetAttribute("value"));
    }

    [Fact]
    public void ServerErrorsRemainVisible()
    {
        var editor = Editor(Entry(BpmnFeelMode.Required, null) with { Error = "Expression required" });
        Assert.Equal("Expression required", editor.Find("[role=alert]").TextContent);
        Assert.Equal("true", editor.Find("textarea").GetAttribute("aria-invalid"));
    }

    [Fact]
    public void ZeebeSourcesExposeFeelOnColumnsAndUseItWhenEditingRows()
    {
        var options = new BpmnOptions();
        options.Extensions.AddZeebe();
        var document = BpmnXmlReader.Read(Xml).Document;
        var snapshot = BpmnElementInspector.Describe(document, "Task_1", extensionOptions: options.Extensions)!;
        foreach (var id in new[] { "inputs", "outputs" })
        {
            var entry = Assert.Single(snapshot.ExtensionGroups.Single(group => group.Id == id).Entries);
            Assert.Equal(BpmnFeelMode.Optional, entry.Columns!.Single(column => column.Name == "source").Feel);
            Assert.Equal(BpmnFeelMode.None, entry.Columns!.Single(column => column.Name == "target").Feel);
        }
        Assert.All(snapshot.ExtensionGroups.Single(group => group.Id == "task-definition").Entries,
            entry => Assert.Equal(BpmnFeelMode.Optional, entry.Feel));
        BpmnEditCommand? edit = null;
        var panel = this.RenderSeeded<BpmnPropertyPanel>(seed => seed.Snapshots([snapshot]).OnCommand(command => edit = command));
        panel.Find("details[data-group-id=inputs] textarea").Change("customer.id");
        var rows = Assert.IsType<ReplaceExtensionCollectionCommand>(edit).Rows;
        Assert.Equal("=customer.id", Assert.Single(rows)["source"]);
        Assert.Equal("alpha", Assert.Single(rows)["target"]);
    }

    [Fact]
    public void ListCellsHonorHostFeelReplacement()
    {
        var entry = new BpmnPropertyEntry("inputs", "Inputs", BpmnPropertyEntryKind.List)
        {
            Columns = [new("source", "Source") { Feel = BpmnFeelMode.Optional }],
            Rows = [new Dictionary<string, string?> { ["source"] = "=x" }],
        };
        var view = Render<BpmnPropertyEntryView>(p => p.AddCascadingValue(StandardPropertyScope.Default).Add(c => c.Context, Context(entry))
            .Add(c => c.Registry, BpmnStandardDefaults.Register(new BpmnPropertyEditorRegistry()).AddFeelEditor<BpmnTextAreaEntry>()));
        Assert.Equal("=x", view.Find("textarea").GetAttribute("value"));
        Assert.Empty(view.FindAll(".bpmn-properties__feel-toggle"));
    }

    [Fact]
    public void CalledProcessAndExistingLoopExpressionFieldsHaveFeelMetadata()
    {
        var options = new BpmnOptions();
        options.Extensions.AddZeebe();
        var document = BpmnXmlReader.Read(Xml.Replace("<process id=\"Process_1\">", """
            <process id="Process_1">
              <callActivity id="Call_1"><extensionElements>
                <zeebe:calledElement processId="=processId" />
              </extensionElements></callActivity>
              <serviceTask id="Multi_1"><multiInstanceLoopCharacteristics>
                <extensionElements><zeebe:loopCharacteristics inputCollection="=items" inputElement="item"
                  outputCollection="results" outputElement="=item.result" /></extensionElements>
              </multiInstanceLoopCharacteristics></serviceTask>
            """, StringComparison.Ordinal)).Document;
        var called = BpmnElementInspector.Describe(document, "Call_1", extensionOptions: options.Extensions)!;
        var calledEntries = called.ExtensionGroups.Single(group => group.Id == "called-element").Entries;
        Assert.Equal(BpmnFeelMode.Optional, calledEntries.Single(entry => entry.Id == "processId").Feel);
        Assert.All(calledEntries.Where(entry => entry.Id != "processId"), entry => Assert.Equal(BpmnFeelMode.None, entry.Feel));
        var multi = BpmnElementInspector.Describe(document, "Multi_1", extensionOptions: options.Extensions)!;
        var loop = multi.ExtensionGroups.Single(group => group.Id == "multi-instance").Entries;
        Assert.All(loop.Where(entry => entry.Id is "inputCollection" or "outputElement"),
            entry => Assert.Equal(BpmnFeelMode.Optional, entry.Feel));
        Assert.All(loop.Where(entry => entry.Id is "inputElement" or "outputCollection"),
            entry => Assert.Equal(BpmnFeelMode.None, entry.Feel));
    }

    [Fact]
    public async Task DefaultProviderReturnsDistinctSortedZeebeTargetsAndResultsOnly()
    {
        var variables = await new BpmnFeelVariableProvider().GetVariablesAsync(BpmnXmlReader.Read(Xml).Document, "Task_1", Xunit.TestContext.Current.CancellationToken);
        Assert.Equal(["alpha", "decisionResult", "scriptResult", "zeta"], variables.Select(variable => variable.Name));
    }

    [Fact]
    public void RegistrationPreservesHostVariableProvider()
    {
        var provider = new CountingProvider();
        Services.AddSingleton<IBpmnFeelVariableProvider>(provider);
        Services.AddBpmnEditor();
        Assert.Same(provider, Services.GetRequiredService<IBpmnFeelVariableProvider>());
    }

    [Fact]
    public void DefaultVariableProviderIsRegistered()
    {
        Services.AddBpmnEditor();
        Assert.IsType<BpmnFeelVariableProvider>(Services.GetRequiredService<IBpmnFeelVariableProvider>());
    }

    [Fact]
    public void PanelResolvesVariablesOncePerSelectionAndPassesElementContext()
    {
        var provider = new CountingProvider();
        Services.AddSingleton<IBpmnFeelVariableProvider>(provider);
        BpmnPropertyEntryContext? captured = null;
        RenderFragment<BpmnPropertyEntryContext> template = context => builder => captured = context;
        var document = BpmnXmlReader.Read(Xml).Document;
        var snapshot = new BpmnElementSnapshot
        {
            ElementId = "Task_1",
            ElementType = "task",
            Category = BpmnElementCategory.Activity,
            Properties = [new("name", "Name", BpmnPropertyKind.Text, "Task")],
        };
        BpmnEditorSession? session = null;
        var panel = this.RenderSeeded<BpmnPropertyPanel>(
            seed => seed.Document(document).Snapshots([snapshot]).Session(s => session = s),
            p => p.Add(c => c.EntryTemplate, template));
        Assert.Equal("Task_1", captured!.ElementId);
        Assert.Equal("Task_1", Assert.Single(captured.Variables).Name);
        Assert.Equal(1, provider.Calls);
        Select(panel, session!, snapshot);
        Assert.Equal(1, provider.Calls);
        Select(panel, session!, snapshot with { ElementId = "Task_2" });
        Assert.Equal(2, provider.Calls);
        Assert.Equal("Task_2", captured.ElementId);
        Select(panel, session!, snapshot, snapshot with { ElementId = "Task_2" });
        Assert.Null(captured.ElementId);
        Assert.Empty(captured.Variables);
        Assert.Equal(2, provider.Calls);
    }

    private static void Select(IRenderedComponent<BpmnPropertyPanel> panel, BpmnEditorSession session, params BpmnElementSnapshot[] snapshots)
    {
        var byId = snapshots.ToDictionary(snapshot => snapshot.ElementId);
        session.DescribeOverride = id => byId.GetValueOrDefault(id);
        panel.InvokeAsync(() => session.SetSelectionAsync([.. snapshots.Select(snapshot => snapshot.ElementId)])).GetAwaiter().GetResult();
    }
    private IRenderedComponent<BpmnFeelEntry> Editor(BpmnPropertyEntry entry, Action<string?>? submit = null, bool readOnly = false) =>
        Render<BpmnFeelEntry>(p => p.AddCascadingValue(StandardPropertyScope.Default).Add(c => c.Context, Context(entry, submit, readOnly)));

    private BpmnPropertyEntryContext Context(BpmnPropertyEntry entry, Action<string?>? submit = null, bool readOnly = false) =>
        new(entry, EventCallback.Factory.Create<string?>(this, value => submit?.Invoke(value)), default, readOnly, 0);

    private static BpmnPropertyEntry Entry(BpmnFeelMode mode, string? value) =>
        new("expression", "Expression", BpmnPropertyEntryKind.Text, value) { Feel = mode };

    private sealed class CountingProvider : IBpmnFeelVariableProvider
    {
        public int Calls { get; private set; }
        public ValueTask<IReadOnlyList<BpmnFeelVariable>> GetVariablesAsync(BpmnDocument document, string elementId, CancellationToken ct = default)
        {
            Calls++;
            return ValueTask.FromResult<IReadOnlyList<BpmnFeelVariable>>([new(elementId)]);
        }
    }

    private const string Xml = """
        <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL"
                     xmlns:zeebe="http://camunda.org/schema/zeebe/1.0" xmlns:c7="http://camunda.org/schema/1.0/bpmn"
                     id="Definitions_1" targetNamespace="urn:test">
          <process id="Process_1">
            <serviceTask id="Task_1">
              <extensionElements>
                <zeebe:taskDefinition type="worker" retries="3" resultVariable="notAResult" />
                <zeebe:ioMapping>
                  <zeebe:input target="alpha" source="=order.id" />
                  <zeebe:output target="zeta" source="=payload" />
                </zeebe:ioMapping>
                <zeebe:calledDecision decisionId="decision" resultVariable="decisionResult" />
                <c7:script resultVariable="ignored" />
              </extensionElements>
            </serviceTask>
            <scriptTask id="Task_2">
              <extensionElements>
                <zeebe:script expression="=1" resultVariable="scriptResult" />
                <zeebe:ioMapping><zeebe:output target="alpha" source="=1" /></zeebe:ioMapping>
              </extensionElements>
            </scriptTask>
          </process>
        </definitions>
        """;
}
