using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;
using Hasseware.AspNetCore.Components.Bpmn.Serialization;
using Microsoft.Extensions.DependencyInjection;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Editing;

public sealed class ScriptTaskPropertyTests
{
    private const string Script = "  var x = 1;\n  return x < 2;\n";

    [Theory]
    [InlineData(null, null)]
    [InlineData("text/javascript", Script)]
    public void DescribingAScriptTaskOffersFormatAndMultilineBody(string? format, string? script)
    {
        var document = Document(format, script);

        var snapshot = BpmnElementInspector.Describe(document, "Task_1")!;

        var formatProperty = Assert.Single(snapshot.Properties, property => property.Name == "scriptFormat");
        Assert.Equal(BpmnPropertyKind.Text, formatProperty.Kind);
        Assert.Equal(format, formatProperty.Value);
        var scriptProperty = Assert.Single(snapshot.Properties, property => property.Name == "script");
        Assert.Equal(BpmnPropertyKind.MultilineText, scriptProperty.Kind);
        Assert.Equal(script, scriptProperty.Value);
        Assert.False(formatProperty.IsReadOnly);
        Assert.False(scriptProperty.IsReadOnly);
    }

    [Theory]
    [InlineData(BpmnFlowNodeType.Task)]
    [InlineData(BpmnFlowNodeType.UserTask)]
    [InlineData(BpmnFlowNodeType.ServiceTask)]
    public void NonScriptTasksRejectScriptProperties(BpmnFlowNodeType type)
    {
        var document = new BpmnDocumentBuilder()
            .Node("Task_1", type, new Bounds(0, 0, 100, 80))
            .Build();

        var result = BpmnDocumentEditor.Apply(document, new UpdatePropertiesCommand("Task_1",
            [new("scriptFormat", "javascript"), new("script", Script)]));

        Assert.False(result.IsApplied);
        Assert.Same(document, result.Document);
        Assert.DoesNotContain(BpmnElementInspector.PropertiesOf(document, "Task_1"),
            property => property.Name is "scriptFormat" or "script");
    }

    [Theory]
    [InlineData(BpmnWriteMode.Canonical)]
    [InlineData(BpmnWriteMode.Fidelity)]
    public void EditingScriptPropertiesRoundTripsWithoutTrimming(BpmnWriteMode mode)
    {
        var source = BpmnXmlWriter.Write(Document("lua", "return 1")).Xml;
        var imported = BpmnXmlReader.Read(source).Document;
        var result = BpmnDocumentEditor.Apply(imported, new UpdatePropertiesCommand("Task_1",
            [new("scriptFormat", "text/javascript"), new("script", Script)]));

        Assert.True(result.IsApplied);
        var written = BpmnXmlWriter.Write(result.Document, new BpmnWriteOptions { Mode = mode });
        var task = BpmnXmlReader.Read(written.Xml).Document.FindFlowNode("Task_1")!;
        Assert.Equal("text/javascript", task.ScriptFormat);
        Assert.Equal(Script, task.Script);
    }

    [Theory]
    [InlineData(BpmnWriteMode.Canonical)]
    [InlineData(BpmnWriteMode.Fidelity)]
    public void NullScriptPropertiesRemoveImportedValues(BpmnWriteMode mode)
    {
        var source = BpmnXmlWriter.Write(Document("javascript", Script)).Xml;
        var imported = BpmnXmlReader.Read(source).Document;

        var result = BpmnDocumentEditor.Apply(imported, new UpdatePropertiesCommand("Task_1",
            [new("scriptFormat", null), new("script", null)]));

        Assert.True(result.IsApplied);
        var written = BpmnXmlWriter.Write(result.Document, new BpmnWriteOptions { Mode = mode, AllowLossyCanonicalExport = true });
        Assert.DoesNotContain("scriptFormat", written.Xml, StringComparison.Ordinal);
        var task = BpmnXmlReader.Read(written.Xml).Document.FindFlowNode("Task_1")!;
        Assert.Null(task.ScriptFormat);
        Assert.Null(task.Script);
    }

    [Fact]
    public void EmptyScriptPropertiesPreserveEmptyText()
    {
        var result = BpmnDocumentEditor.Apply(Document("javascript", Script), new UpdatePropertiesCommand("Task_1",
            [new("scriptFormat", ""), new("script", "")]));

        Assert.True(result.IsApplied);
        Assert.Equal("", result.Document.FindFlowNode("Task_1")!.ScriptFormat);
        Assert.Equal("", result.Document.FindFlowNode("Task_1")!.Script);
    }

    [Fact]
    public void ReadOnlyScriptTasksOfferInspectionWithoutEditing()
    {
        var snapshot = BpmnElementInspector.Describe(Document("javascript", Script), "Task_1", isReadOnly: true)!;

        Assert.True(Assert.Single(snapshot.Properties, property => property.Name == "scriptFormat").IsReadOnly);
        Assert.True(Assert.Single(snapshot.Properties, property => property.Name == "script").IsReadOnly);
    }

    [Fact]
    public async Task ScriptPropertyEditsUndoAndRedoBothValues()
    {
        var session = new BpmnEditorSession(new BpmnOptions(), new ServiceCollection().BuildServiceProvider())
        {
            Document = Document(),
        };
        session.UpdateParameters(EditorConfig.CreateDefault());
        await session.ApplyAsync(new UpdatePropertiesCommand("Task_1",
            [new("scriptFormat", "javascript"), new("script", Script)]));

        await session.UndoAsync();
        Assert.Null(session.EditedDocument!.FindFlowNode("Task_1")!.ScriptFormat);
        Assert.Null(session.EditedDocument.FindFlowNode("Task_1")!.Script);

        await session.RedoAsync();
        Assert.Equal("javascript", session.EditedDocument.FindFlowNode("Task_1")!.ScriptFormat);
        Assert.Equal(Script, session.EditedDocument.FindFlowNode("Task_1")!.Script);
    }

    private static BpmnDocument Document(string? format = null, string? script = null)
    {
        var document = new BpmnDocumentBuilder()
            .Node("Task_1", BpmnFlowNodeType.ScriptTask, new Bounds(0, 0, 100, 80))
            .Build();
        return document with
        {
            FlowNodes = [document.FlowNodes[0] with { ScriptFormat = format, Script = script }],
        };
    }
}
