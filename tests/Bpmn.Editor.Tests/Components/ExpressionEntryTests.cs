using Bunit;
using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Feel;
using Hasseware.AspNetCore.Components.Bpmn.Serialization;
using Hasseware.AspNetCore.Components.Bpmn.Validation;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Components;

public sealed class ExpressionEntryTests : BunitContext
{
    [Theory]
    [InlineData(BpmnPropertyEntryKind.Text)]
    [InlineData(BpmnPropertyEntryKind.TextArea)]
    public void JuelDiagnosticsAreAccessibleAndAdvisory(BpmnPropertyEntryKind kind)
    {
        string? submitted = null;
        var entry = new BpmnPropertyEntry("assignee", "Assignee", kind, "${user}")
        { ExpressionLanguage = BpmnExpressionLanguage.Juel };
        var editor = Render<BpmnPropertyEntryView>(p => p.AddCascadingValue(StandardPropertyScope.Default).Add(c => c.Context, Context(entry, value => submitted = value)));
        var selector = kind == BpmnPropertyEntryKind.Text ? "input" : "textarea";
        editor.Find(selector).Input("${user +}");
        Assert.Null(submitted);
        Assert.Equal("true", editor.Find(selector).GetAttribute("aria-invalid"));
        var error = editor.Find("[role=alert]");
        Assert.Contains("JUEL warning", error.TextContent, StringComparison.Ordinal);
        Assert.Equal(error.Id, editor.Find(selector).GetAttribute("aria-describedby"));
        editor.Find(selector).Change("${user +}");
        Assert.Equal("${user +}", submitted);
        editor.Find(selector).Input("Hello ${user.name}");
        Assert.Empty(editor.FindAll("[role=alert]"));
        Assert.Null(editor.Find(selector).GetAttribute("aria-invalid"));
    }

    [Fact]
    public void InvalidInitialJuelValueCanBeCorrectedWithoutParentRefresh()
    {
        var entry = new BpmnPropertyEntry("value", "Value", BpmnPropertyEntryKind.Text, "${x +}")
        { ExpressionLanguage = BpmnExpressionLanguage.Juel };
        var editor = Render<BpmnTextEntry>(p => p.AddCascadingValue(StandardPropertyScope.Default).Add(c => c.Context, Context(entry)));
        Assert.Single(editor.FindAll("[role=alert]"));
        editor.Find("input").Change("${x}");
        Assert.Empty(editor.FindAll("[role=alert]"));
    }

    [Theory]
    [InlineData(BpmnPropertyEntryKind.Text, "input")]
    [InlineData(BpmnPropertyEntryKind.TextArea, "textarea")]
    public void UnchangedParentRerenderKeepsUncommittedDraft(BpmnPropertyEntryKind kind, string selector)
    {
        var entry = new BpmnPropertyEntry("value", "Value", kind, "old");
        var context = Context(entry);
        var editor = Render<BpmnPropertyEntryView>(p => p.AddCascadingValue(StandardPropertyScope.Default).Add(c => c.Context, context));
        editor.Find(selector).Input("draft");

        editor.Render(p => p.Add(c => c.Context, context));

        Assert.Equal("draft", editor.Find(selector).GetAttribute("value"));
    }

    [Fact]
    public void ExistingBindingErrorsRemainAuthoritativeAndDescribed()
    {
        var entry = new BpmnPropertyEntry("value", "Value", BpmnPropertyEntryKind.Text, "${x +}")
        { ExpressionLanguage = BpmnExpressionLanguage.Juel, Error = "Required by template" };
        var editor = Render<BpmnTextEntry>(p => p.AddCascadingValue(StandardPropertyScope.Default).Add(c => c.Context, Context(entry)));
        Assert.Equal("Required by template", editor.Find("[role=alert]").TextContent);
        Assert.Equal(editor.Find("[role=alert]").Id, editor.Find("input").GetAttribute("aria-describedby"));
    }

    [Fact]
    public void FeelUsesServerValidatorForNonBracketSyntaxErrors()
    {
        var entry = new BpmnPropertyEntry("value", "Value", BpmnPropertyEntryKind.Text, "=if x then 1")
        { Feel = BpmnFeelMode.Required, ExpressionLanguage = BpmnExpressionLanguage.Feel };
        var editor = Render<BpmnFeelEntry>(p => p.AddCascadingValue(StandardPropertyScope.Default).Add(c => c.Context, Context(entry)));
        Assert.Contains("else", editor.Find("[role=status]").TextContent, StringComparison.Ordinal);
        Assert.Equal("true", editor.Find("textarea").GetAttribute("aria-invalid"));
        editor.Find("textarea").Input("if x then 1 else 2");
        Assert.Empty(editor.FindAll("[role=status]"));
        Assert.Null(editor.Find("textarea").GetAttribute("aria-invalid"));
    }

    [Fact]
    public void HostValidatorReplacesManagedValidationInBothEditors()
    {
        Services.AddSingleton<IBpmnExpressionValidator>(new HostValidator());
        Services.AddBpmnEditor();
        var feel = new BpmnPropertyEntry("feel", "FEEL", BpmnPropertyEntryKind.Text, "=valid") { Feel = BpmnFeelMode.Required };
        var editor = Render<BpmnFeelEntry>(p => p.AddCascadingValue(StandardPropertyScope.Default).Add(c => c.Context, Context(feel)));
        Assert.Contains("Host engine diagnostic", editor.Find("[role=status]").TextContent, StringComparison.Ordinal);
        var juel = feel with { Feel = BpmnFeelMode.None, ExpressionLanguage = BpmnExpressionLanguage.Juel, Value = "${valid}" };
        var text = Render<BpmnTextEntry>(p => p.AddCascadingValue(StandardPropertyScope.Default).Add(c => c.Context, Context(juel)));
        Assert.Contains("Host engine diagnostic", text.Find("[role=alert]").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void OptionalFeelLiteralDoesNotInvokeValidator()
    {
        var validator = new HostValidator();
        Services.AddSingleton<IBpmnExpressionValidator>(validator);
        var entry = new BpmnPropertyEntry("value", "Value", BpmnPropertyEntryKind.Text, "literal")
        { Feel = BpmnFeelMode.Optional, ExpressionLanguage = BpmnExpressionLanguage.Feel };
        var editor = Render<BpmnFeelEntry>(p => p.AddCascadingValue(StandardPropertyScope.Default).Add(c => c.Context, Context(entry)));
        Assert.Empty(editor.FindAll("[role=alert]"));
        Assert.Equal(0, validator.Calls);
    }

    [Fact]
    public void RichFallbackAlsoUsesServerSyntaxValidation()
    {
        Services.AddSingleton<IJSRuntime>(new UnavailableRuntime());
        var entry = new BpmnPropertyEntry("value", "Value", BpmnPropertyEntryKind.Text, "=if x then 1")
        { Feel = BpmnFeelMode.Required };
        var editor = Render<BpmnFeelCodeEditor>(p => p.AddCascadingValue(StandardPropertyScope.Default).Add(c => c.Context, Context(entry)));
        Assert.Contains("else", editor.Find("[role=status]").TextContent, StringComparison.Ordinal);
        Assert.Equal("true", editor.Find("textarea").GetAttribute("aria-invalid"));
        editor.Find("textarea").Change("if x then 1 else 2");
        Assert.Empty(editor.FindAll("[role=status]"));
    }

    [Fact]
    public async Task RichEditorReceivesServerDiagnosticsAndAccessibleAttributesOnCommit()
    {
        var module = JSInterop.SetupModule("./_content/Hasseware.AspNetCore.Components.Bpmn.Feel/feel-editor.js");
        var handle = module.SetupModule("create", _ => true);
        foreach (var name in new[] { "setValue", "setVariables", "setReadOnly", "setAttributes", "dispose" })
            handle.SetupVoid(name, _ => true).SetVoidResult();
        var entry = new BpmnPropertyEntry("value", "Value", BpmnPropertyEntryKind.Text, "=if x then 1")
        { Feel = BpmnFeelMode.Required };
        string? submitted = null;
        var editor = Render<BpmnFeelCodeEditor>(p => p.AddCascadingValue(StandardPropertyScope.Default).Add(c => c.Context, Context(entry, value => submitted = value)));
        Assert.Empty(editor.FindAll("textarea"));
        Assert.True((bool)handle.Invocations["setAttributes"][^1].Arguments[2]!);
        Assert.Equal(editor.Find("[role=status]").Id, handle.Invocations["setAttributes"][^1].Arguments[1]);
        await editor.Instance.CommitExpressionAsync("if x then 1 else 2", 1);
        Assert.Equal("=if x then 1 else 2", submitted);
        Assert.Empty(editor.FindAll("[role=status]"));
        Assert.False((bool)handle.Invocations["setAttributes"][^1].Arguments[2]!);
    }

    [Fact]
    public void JuelCollectionCellsValidateWithoutLosingRowIdentity()
    {
        IReadOnlyList<IReadOnlyDictionary<string, string?>>? submitted = null;
        var entry = new BpmnPropertyEntry("inputs", "Inputs", BpmnPropertyEntryKind.List)
        {
            Columns = [new("name", "Name"), new("#text", "Value") { ExpressionLanguage = BpmnExpressionLanguage.Juel }],
            Rows = [new Dictionary<string, string?> { ["name"] = "target", ["#text"] = "${x}", ["$index"] = "4" }],
        };
        var context = Context(entry) with
        { OnRowsChanged = EventCallback.Factory.Create<IReadOnlyList<IReadOnlyDictionary<string, string?>>>(this, rows => submitted = rows) };
        var editor = Render<BpmnListEntry>(p => p.AddCascadingValue(StandardPropertyScope.Default).Add(c => c.Context, context));
        editor.Find("td:nth-child(2) input").Change("${x +}");
        Assert.Single(editor.FindAll("[role=alert]"));
        Assert.Equal("${x +}", Assert.Single(submitted!)["#text"]);
        Assert.Equal("4", Assert.Single(submitted!)["$index"]);
    }

    [Fact]
    public void CamundaSchemaMarksExpressionCapableScalarsAndColumns()
    {
        var options = new BpmnOptions();
        options.Extensions.AddCamunda7();
        var document = BpmnXmlReader.Read(CamundaXml).Document;
        var snapshot = BpmnElementInspector.Describe(document, "Task", extensionOptions: options.Extensions)!;
        var assignment = snapshot.ExtensionGroups.Single(group => group.Id == "camunda:assignment");
        Assert.All(assignment.Entries, entry => Assert.Equal(BpmnExpressionLanguage.Juel, entry.ExpressionLanguage));
        var inputs = snapshot.ExtensionGroups.Single(group => group.Id == "camunda:inputs").Entries.Single();
        Assert.Equal(BpmnExpressionLanguage.Juel, inputs.Columns!.Single(column => column.Name == "#text").ExpressionLanguage);
        Assert.Equal(BpmnExpressionLanguage.None, inputs.Columns!.Single(column => column.Name == "name").ExpressionLanguage);
        var condition = new BpmnGeneralPropertiesProvider().GetGroups(snapshot with
        { Properties = [new("conditionExpression", "Condition", BpmnPropertyKind.MultilineText, "${x +}")] }, []).Single().Entries.Single();
        Assert.Equal(BpmnExpressionLanguage.Juel, condition.ExpressionLanguage);
    }

    [Fact]
    public void ZeebeGenericExpressionAttributesReceiveFeelMetadata()
    {
        var options = new BpmnOptions();
        options.Extensions.AddZeebe();
        var document = BpmnXmlReader.Read(ZeebeXml).Document;
        var snapshot = BpmnElementInspector.Describe(document, "Task", extensionOptions: options.Extensions)!;
        var expression = snapshot.ExtensionGroups.SelectMany(group => group.Entries).Single(entry => entry.Id == "expression");
        Assert.Equal(BpmnFeelMode.Required, expression.Feel);
        Assert.Equal(BpmnExpressionLanguage.Feel, expression.ExpressionLanguage);
        var result = snapshot.ExtensionGroups.SelectMany(group => group.Entries).Single(entry => entry.Id == "resultVariable");
        Assert.Equal(BpmnExpressionLanguage.None, result.ExpressionLanguage);
    }

    [Fact]
    public void ReadOnlyExpressionEntriesNeverCommit()
    {
        var commits = 0;
        var entry = new BpmnPropertyEntry("value", "Value", BpmnPropertyEntryKind.Text, "${x +}")
        { ExpressionLanguage = BpmnExpressionLanguage.Juel };
        var editor = Render<BpmnTextEntry>(p => p.AddCascadingValue(StandardPropertyScope.Default).Add(c => c.Context, Context(entry, _ => commits++) with { IsReadOnly = true }));
        editor.Find("input").Input("${y}");
        editor.Find("input").Change("${y}");
        Assert.Equal(0, commits);
        Assert.Equal("${x +}", editor.Find("input").GetAttribute("value"));
    }

    private BpmnPropertyEntryContext Context(BpmnPropertyEntry entry, Action<string?>? submit = null) =>
        new(entry, EventCallback.Factory.Create<string?>(this, value => submit?.Invoke(value)), default, false, 0);

    private sealed class UnavailableRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            ValueTask.FromException<TValue>(new JSException("Module unavailable"));
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            InvokeAsync<TValue>(identifier, args);
    }

    private sealed class HostValidator : IBpmnExpressionValidator
    {
        public int Calls { get; private set; }
        public IReadOnlyList<BpmnExpressionDiagnostic> Validate(BpmnExpressionLanguage language, string expression)
        { Calls++; return [new("Host engine diagnostic", 0, 1)]; }
    }

    private const string CamundaXml = """
        <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL" xmlns:c="http://camunda.org/schema/1.0/bpmn" id="D" targetNamespace="urn:test">
          <process id="P"><userTask id="Task" c:assignee="${user}">
            <extensionElements><c:inputOutput><c:inputParameter name="target">${x}</c:inputParameter></c:inputOutput></extensionElements>
          </userTask></process>
        </definitions>
        """;
    private const string ZeebeXml = """
        <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL" xmlns:z="http://camunda.org/schema/zeebe/1.0" id="D" targetNamespace="urn:test">
          <process id="P"><scriptTask id="Task"><extensionElements><z:script expression="=1 +" resultVariable="result" /></extensionElements></scriptTask></process>
        </definitions>
        """;
}
