using System.Text;

using Hasseware.AspNetCore.Components.Bpmn.Serialization;
using Hasseware.AspNetCore.Components.Bpmn.Storage;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Serialization;

/// <summary>A document that reads is not always one that writes, and a file keeps the format it was in.</summary>
public sealed class BpmnUnwritableDocumentTests
{
    private const string Open = """
        <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL"
                     xmlns:x="urn:vendor" id="d" targetNamespace="t">
        """;

    private static string Nested(int depth)
    {
        var text = new StringBuilder(Open);
        text.Append("<process id=\"p\"><task id=\"t1\"><extensionElements>");
        for (var level = 0; level < depth; level++) text.Append("<x:a>");
        for (var level = 0; level < depth; level++) text.Append("</x:a>");
        text.Append("</extensionElements></task></process></definitions>");
        return text.ToString();
    }

    private static Model.BpmnDocument WithInvalidExtensionName()
    {
        var json = BpmnJsonWriter.Write(BpmnXmlReader.Read(Nested(1)).Document);
        Assert.Contains("\"localName\": \"a\"", json.Replace("\"localName\":\"a\"", "\"localName\": \"a\"", StringComparison.Ordinal), StringComparison.Ordinal);
        return BpmnJsonReader.Read(json.Replace("\"localName\":\"a\"", "\"localName\":\"not a name\"", StringComparison.Ordinal)
            .Replace("\"localName\": \"a\"", "\"localName\": \"not a name\"", StringComparison.Ordinal)).Document;
    }

    [Fact]
    public void ReportsAnExtensionNameThatIsNotAValidXmlNameAsAFailureToExport()
    {
        var document = WithInvalidExtensionName();
        var key = new BpmnDocumentKey(null, BpmnDocumentPath.Parse("a.bpmn"));

        Assert.Throws<BpmnExportException>(() => BpmnDocumentStoreExtensions.Serialize(key, document));
    }

    [Fact]
    public void ReportsATreeNestedBeyondTheJsonLimitAsAFailureToExport()
    {
        var document = BpmnXmlReader.Read(Nested(200)).Document;
        var key = new BpmnDocumentKey(null, BpmnDocumentPath.Parse("a.json"));

        Assert.Throws<BpmnExportException>(() => BpmnDocumentStoreExtensions.Serialize(key, document));
    }

    [Fact]
    public async Task KeepsTheFormatAFileWasLoadedInWhenItsNameSaysOtherwise()
    {
        var store = new InMemoryBpmnDocumentStore();
        var key = new BpmnDocumentKey(null, BpmnDocumentPath.Parse("diagram.json"));
        await store.SaveAsync(key, new BpmnDocumentContent(Encoding.UTF8.GetBytes(Nested(1)), BpmnFileFormat.Xml), null, TestContext.Current.CancellationToken);

        var loaded = await store.LoadDocumentAsync(key, TestContext.Current.CancellationToken);
        Assert.Equal(BpmnFileFormat.Xml, loaded!.Format);
        await store.SaveDocumentAsync(key, loaded.Document, loaded.Revision, loaded.Format, TestContext.Current.CancellationToken);

        var after = await store.OpenAsync(key, TestContext.Current.CancellationToken);
        Assert.StartsWith("<", Encoding.UTF8.GetString(after!.Content.Bytes.Span).TrimStart('﻿'), StringComparison.Ordinal);
    }
}
