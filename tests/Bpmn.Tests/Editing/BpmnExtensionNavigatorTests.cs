using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;
using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions;
using Hasseware.AspNetCore.Components.Bpmn.Serialization;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Editing;

public sealed class BpmnExtensionNavigatorTests
{
    private const string Zeebe = "http://camunda.org/schema/zeebe/1.0";
    private const string Ns = "urn:vendor:test";

    [Fact]
    public void PathParseAndFormatRoundTripEscapedSelectorValues()
    {
        const string text = "{http://camunda.org/schema/zeebe/1.0}ioMapping/{http://camunda.org/schema/zeebe/1.0}input[target=a\\]b\\/c\\=d]@source";
        var parsed = BpmnExtensionPath.Parse(text);

        Assert.Equal(text, parsed.ToString());
        Assert.True(BpmnExtensionPath.TryParse(text, out var second));
        Assert.Equal(parsed.ToString(), second!.ToString());
        Assert.False(BpmnExtensionPath.TryParse("{urn:x}root[bad]@", out _));
    }

    [Fact]
    public void WriteCreatesKeyedSpineAndPrunesItOnRemove()
    {
        var extras = BpmnElementExtras.None;
        var path = BpmnExtensionPath.Parse($"{{{Zeebe}}}ioMapping/{{{Zeebe}}}input[target=orderId]@source");

        var written = BpmnExtensionNavigator.Write(extras, path, "=orderId");
        var mapping = Assert.Single(written.Extensions);
        var input = Assert.Single(mapping.Children);
        Assert.Equal("orderId", input.Attributes["target"]);
        Assert.Equal("=orderId", BpmnExtensionNavigator.Read(written, path));

        var withoutSource = BpmnExtensionNavigator.Write(written, path, string.Empty);
        Assert.Equal("orderId", withoutSource.Extensions[0].Children[0].Attributes["target"]);
        var clearSelector = BpmnExtensionPath.Parse($"{{{Zeebe}}}ioMapping/{{{Zeebe}}}input[target=orderId]@target");
        var cleared = BpmnExtensionNavigator.Write(withoutSource, clearSelector, string.Empty);
        Assert.Empty(cleared.Extensions);
    }

    [Fact]
    public void WriteOptionsControlPruningAndCommandInsertionOrder()
    {
        var original = WithExtras(Element(Ns, "kept"));
        var task = BpmnExtensionPath.Parse($"{{{Zeebe}}}ioMapping/{{{Zeebe}}}input[target=x]@source");
        var written = BpmnExtensionNavigator.Write(BpmnElementExtras.None, task, "value");
        var extras = BpmnExtensionNavigator.Write(
            written, task, null, new BpmnExtensionWriteOptions(PruneEmpty: false));
        Assert.Single(extras.Extensions);
        Assert.Null(BpmnExtensionNavigator.Read(extras, task));

        var options = EditingOptions.Default with { ExtensionChildOrder = new FrontOrder() };
        var result = BpmnDocumentEditor.Apply(original,
            new AddExtensionElementCommand("Task_1", null, Element(Zeebe, "taskDefinition")), options);

        Assert.True(result.IsApplied, Diagnostics(result));
        Assert.Equal("taskDefinition", result.Document.FindFlowNode("Task_1")!.Extras.Extensions[0].LocalName);
        Assert.Equal("kept", result.Document.FindFlowNode("Task_1")!.Extras.Extensions[1].LocalName);
    }

    [Fact]
    public void AttributeSelectorTracksItemWhenSiblingOrderChanges()
    {
        var path = BpmnExtensionPath.Parse($"{{{Zeebe}}}ioMapping/{{{Zeebe}}}input[target=orderId]@source");
        var first = Element(Zeebe, "input", ("target", "orderId"), ("source", "old"));
        var second = Element(Zeebe, "input", ("target", "amount"), ("source", "amountSource"));
        var extras = new BpmnElementExtras
        {
            Extensions = [new BpmnExtensionElement(Zeebe, "ioMapping", Attrs(), [second, first])],
        };

        var changed = BpmnExtensionNavigator.Write(extras, path, "new");

        Assert.Equal("new", BpmnExtensionNavigator.Read(changed, path));
        Assert.Equal("amountSource", changed.Extensions[0].Children[0].Attributes["source"]);
        Assert.Equal("new", changed.Extensions[0].Children[1].Attributes["source"]);
    }

    [Fact]
    public void OrdinalSelectorAddressesSameNameSibling()
    {
        var path = BpmnExtensionPath.Parse($"{{{Zeebe}}}ioMapping/{{{Zeebe}}}input[1]@source");
        var first = Element(Zeebe, "input", ("source", "first"));
        var second = Element(Zeebe, "input", ("source", "second"));
        var extras = new BpmnElementExtras
        {
            Extensions = [new BpmnExtensionElement(Zeebe, "ioMapping", Attrs(), [first, second])],
        };

        var updated = BpmnExtensionNavigator.Write(extras, path, "changed");

        Assert.Equal("first", updated.Extensions[0].Children[0].Attributes["source"]);
        Assert.Equal("changed", updated.Extensions[0].Children[1].Attributes["source"]);
        Assert.Equal("{http://camunda.org/schema/zeebe/1.0}ioMapping/{http://camunda.org/schema/zeebe/1.0}input[1]@source", path.ToString());
    }

    [Fact]
    public void CollectionReplacementPreservesUnknownItemAttributesAndChildren()
    {
        var item = new BpmnExtensionElement(Zeebe, "input", Attrs(("source", "old"), ("custom", "keep")),
            [Element(Ns, "opaque")]);
        var extras = new BpmnElementExtras
        {
            Extensions = [new BpmnExtensionElement(Zeebe, "ioMapping", Attrs(), [item])],
        };
        var path = BpmnExtensionPath.Parse($"{{{Zeebe}}}ioMapping");
        IReadOnlyDictionary<string, string?> row = new Dictionary<string, string?> { ["source"] = "new" };

        var replaced = BpmnExtensionNavigator.ReplaceCollection(extras, path, Zeebe, "input", [row]);
        var actual = Assert.Single(BpmnExtensionNavigator.ReadCollection(replaced, path, Zeebe, "input"));
        Assert.Equal("new", actual.Attributes["source"]);
        Assert.Equal("keep", actual.Attributes["custom"]);
        Assert.Equal(item.Children, actual.Children);

        var emptied = BpmnExtensionNavigator.ReplaceCollection(replaced, path, Zeebe, "input", []);
        Assert.Empty(emptied.Extensions);
    }

    [Theory]
    [InlineData("reorder")]
    [InlineData("delete")]
    [InlineData("add")]
    [InlineData("legacy")]
    [InlineData("invalid")]
    [InlineData("duplicate")]
    public void CollectionIdentityKeepsOpaqueContentWithItsRow(string operation)
    {
        var first = new BpmnExtensionElement(Zeebe, "input", Attrs(("target", "first"), ("custom", "first opaque")),
            [Element(Ns, "firstChild")], "first text");
        var second = new BpmnExtensionElement(Zeebe, "input", Attrs(("target", "second"), ("custom", "second opaque")),
            [Element(Ns, "secondChild")], "second text");
        var extras = new BpmnElementExtras { Extensions = [new(Zeebe, "ioMapping", Attrs(), [first, second])] };
        var path = BpmnExtensionPath.Parse($"{{{Zeebe}}}ioMapping");
        static IReadOnlyDictionary<string, string?> Row(string name, string? index) => index is null
            ? new Dictionary<string, string?> { ["target"] = name }
            : new Dictionary<string, string?> { ["target"] = name, ["$index"] = index };
        IReadOnlyList<IReadOnlyDictionary<string, string?>> rows = operation switch
        {
            "reorder" => [Row("second", "1"), Row("first", "0")],
            "delete" => [Row("second", "1")],
            "add" => [Row("first", "0"), Row("new", null), Row("second", "1")],
            "legacy" => [Row("second", null), Row("first", null)],
            "invalid" => [Row("new", "99"), Row("new", "bad"), Row("new", "-1"), Row("new", null), Row("second", "1")],
            _ => [Row("new", "0"), Row("new", "00"), Row("second", "1")],
        };

        var replaced = BpmnExtensionNavigator.ReplaceCollection(extras, path, Zeebe, "input", rows);
        var items = BpmnExtensionNavigator.ReadCollection(replaced, path, Zeebe, "input");
        Assert.Equal(rows.Count, items.Count);
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            Assert.False(item.Attributes.ContainsKey("$index"));
            Assert.Equal(rows[i]["target"], item.Attributes["target"]);
            if (rows[i]["target"] == "new")
            {
                Assert.Empty(item.Children);
                Assert.False(item.Attributes.ContainsKey("custom"));
                Assert.Null(item.Text);
            }
            else
            {
                var original = operation == "legacy" ? new[] { first, second }[i]
                    : rows[i]["target"] == "first" ? first : second;
                Assert.Equal(original.Attributes["custom"], item.Attributes["custom"]);
                Assert.Equal(original.Children, item.Children);
                Assert.Equal(original.Text, item.Text);
            }
        }
        var xml = BpmnXmlWriter.Write(WithExtras(replaced.Extensions[0])).Xml;
        Assert.DoesNotContain("$index", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void ZeebeSchemaRowsCarryOriginalCollectionIndices()
    {
        var document = WithExtras(new(Zeebe, "ioMapping", Attrs(),
            [Element(Zeebe, "input", ("target", "first")), Element(Zeebe, "input", ("target", "second"))]));
        var options = new BpmnOptions();
        options.Extensions.AddZeebe();
        var snapshot = BpmnElementInspector.Describe(document, "Task_1", extensionOptions: options.Extensions)!;
        var entry = snapshot.ExtensionGroups.SelectMany(group => group.Entries).Single(entry => entry.Id == "inputs");
        Assert.Equal(["0", "1"], entry.Rows!.Select(row => row["$index"]));
        Assert.DoesNotContain(entry.Columns!, column => column.Name == "$index");
    }

    [Fact]
    public void CommandsApplyUndoAndExportNamespaceDeclarations()
    {
        var document = NewDocument();
        var history = new BpmnEditHistory(document);
        var command = new AddExtensionElementCommand("Task_1", null,
            new BpmnExtensionElement(Zeebe, "taskDefinition", Attrs(("type", "worker")), []));
        var applied = history.Apply(command);

        Assert.True(applied.IsApplied, Diagnostics(applied));
        Assert.Equal("Add extension element", history.UndoLabel);
        var xml = BpmnXmlWriter.Write(history.Document).Xml;
        Assert.Contains("xmlns:zeebe=\"" + Zeebe + "\"", xml, StringComparison.Ordinal);
        Assert.Contains("zeebe:taskDefinition", xml, StringComparison.Ordinal);
        var reread = BpmnXmlReader.Read(xml).Document;
        var taskDefinition = Assert.Single(reread.FindFlowNode("Task_1")!.Extras.Extensions);
        Assert.Equal(Zeebe, taskDefinition.Namespace);
        Assert.Equal("worker", taskDefinition.Attributes["type"]);

        var restored = history.Undo();
        Assert.Empty(restored.FindFlowNode("Task_1")!.Extras.Extensions);
    }

    [Fact]
    public void CollectionAndRemoveCommandsAreUndoable()
    {
        var item = Element(Zeebe, "input", ("target", "orderId"), ("source", "old"), ("custom", "preserve"));
        var original = WithExtras(new BpmnExtensionElement(Zeebe, "ioMapping", Attrs(), [item]));
        var history = new BpmnEditHistory(original);
        IReadOnlyDictionary<string, string?> row = new Dictionary<string, string?>
        {
            ["target"] = "orderId",
            ["source"] = "new",
        };
        var collectionPath = $"{{{Zeebe}}}ioMapping";
        var replaced = history.Apply(new ReplaceExtensionCollectionCommand(
            "Task_1", collectionPath, Zeebe, "input", [row]));

        Assert.True(replaced.IsApplied, Diagnostics(replaced));
        var replacement = Assert.Single(history.Document.FindFlowNode("Task_1")!.Extras.Extensions[0].Children);
        Assert.Equal("new", replacement.Attributes["source"]);
        Assert.Equal("preserve", replacement.Attributes["custom"]);

        var itemPath = $"{{{Zeebe}}}ioMapping/{{{Zeebe}}}input[target=orderId]";
        var removed = history.Apply(new RemoveExtensionElementCommand("Task_1", itemPath));
        Assert.True(removed.IsApplied, Diagnostics(removed));
        Assert.Empty(history.Document.FindFlowNode("Task_1")!.Extras.Extensions);

        var restored = history.Undo();
        Assert.Equal("new", restored.FindFlowNode("Task_1")!.Extras.Extensions[0].Children[0].Attributes["source"]);
        restored = history.Undo();
        Assert.Same(original, restored);
    }

    [Fact]
    public void UnknownNamespacePrefixStartsAtOneAndSkipsDeclaredPrefixCollisions()
    {
        var document = NewDocument();
        var definitions = document.Definitions with
        {
            NamespaceDeclarations = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["ns1"] = "urn:occupied",
            },
        };
        var task = document.FindFlowNode("Task_1")! with
        {
            Extras = new BpmnElementExtras
            {
                Extensions = [Element("urn:occupied", "known"), Element(Ns, "opaque")],
            },
        };
        var edited = document with { Definitions = definitions, FlowNodes = [task] };

        var xml = BpmnXmlWriter.Write(edited).Xml;

        Assert.Contains("xmlns:ns1=\"urn:occupied\"", xml, StringComparison.Ordinal);
        Assert.Contains("xmlns:ns2=\"" + Ns + "\"", xml, StringComparison.Ordinal);
        Assert.Contains("ns2:opaque", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void ForeignAttributeCanBeSetAndRemoved()
    {
        var document = NewDocument();
        var set = BpmnDocumentEditor.Apply(document, new SetForeignAttributeCommand("Task_1", Zeebe, "modelerTemplate", "template-id"));
        Assert.True(set.IsApplied, Diagnostics(set));
        Assert.Equal("template-id", set.Document.FindFlowNode("Task_1")!.Extras.ForeignAttributes[$"{{{Zeebe}}}modelerTemplate"]);
        var xml = BpmnXmlWriter.Write(set.Document).Xml;
        Assert.Contains("zeebe:modelerTemplate=\"template-id\"", xml, StringComparison.Ordinal);
        var roundTrip = BpmnXmlReader.Read(xml).Document;
        Assert.Equal("template-id", roundTrip.FindFlowNode("Task_1")!.Extras.ForeignAttributes[$"{{{Zeebe}}}modelerTemplate"]);

        var removed = BpmnDocumentEditor.Apply(set.Document, new SetForeignAttributeCommand("Task_1", Zeebe, "modelerTemplate", null));
        Assert.True(removed.IsApplied, Diagnostics(removed));
        Assert.Empty(removed.Document.FindFlowNode("Task_1")!.Extras.ForeignAttributes);
    }

    [Fact]
    public void SetValueTargetsExpandedNameAndPreservesUnknownSiblingAcrossRoundTrip()
    {
        var opaque = Element(Ns, "foreign", ("unknown", "verbatim"));
        var document = WithExtras(opaque);
        var path = $"{{{Zeebe}}}taskDefinition@type";
        var command = new SetExtensionValueCommand("Task_1", path, "worker");
        var changed = BpmnDocumentEditor.Apply(document, command);

        Assert.True(changed.IsApplied, Diagnostics(changed));
        Assert.Same(opaque, changed.Document.FindFlowNode("Task_1")!.Extras.Extensions[0]);
        var roundTrip = BpmnXmlReader.Read(BpmnXmlWriter.Write(changed.Document).Xml).Document;
        Assert.Contains(opaque, roundTrip.FindFlowNode("Task_1")!.Extras.Extensions);
        Assert.Equal("worker", BpmnExtensionNavigator.Read(roundTrip.FindFlowNode("Task_1")!.Extras, BpmnExtensionPath.Parse(path)));
    }

    [Fact]
    public void ExtensionCommandsRejectUnknownMalformedUnsupportedAndUnsafeRequests()
    {
        var document = NewDocument();
        var unknown = BpmnDocumentEditor.Apply(document, new SetExtensionValueCommand("missing", "{urn:x}tag@value", "x"));
        Assert.Equal("EDIT-100", Assert.Single(unknown.Rejections).Code);

        var malformed = BpmnDocumentEditor.Apply(document, new SetExtensionValueCommand("Task_1", "not-an-expanded-name", "x"));
        Assert.Equal("EDIT-102", Assert.Single(malformed.Rejections).Code);

        var noExtras = document with { Catalog = document.Catalog with { ItemDefinitions = [new BpmnItemDefinition("Item_1")] } };
        var unsupported = BpmnDocumentEditor.Apply(noExtras, new SetForeignAttributeCommand("Item_1", Zeebe, "template", "x"));
        Assert.Equal("EDIT-101", Assert.Single(unsupported.Rejections).Code);

        var unsafeEdit = BpmnDocumentEditor.Apply(document,
            new AddExtensionElementCommand("Task_1", null, Element("http://www.w3.org/1999/xhtml", "script")));
        Assert.Equal("EDIT-103", Assert.Single(unsafeEdit.Rejections).Code);
    }

    private static BpmnDocument NewDocument() => new BpmnDocumentBuilder()
        .Node("Task_1", BpmnFlowNodeType.ServiceTask, new Bounds(200, 120, 100, 80), "Work")
        .Build();

    private static BpmnDocument WithExtras(BpmnExtensionElement extension) => NewDocument() with
    {
        FlowNodes = [NewDocument().FindFlowNode("Task_1")! with
        {
            Extras = new BpmnElementExtras { Extensions = [extension] },
        }],
    };

    private static BpmnExtensionElement Element(string ns, string name, params (string Key, string Value)[] attributes) =>
        new(ns, name, Attrs(attributes), [], null);

    private static Dictionary<string, string> Attrs(params (string Key, string Value)[] values) =>
        values.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    private static string Diagnostics(BpmnEditResult result) =>
        string.Join("; ", result.Diagnostics.Select(diagnostic => diagnostic.Message));

    private sealed class FrontOrder : IBpmnExtensionChildOrder
    {
        public int InsertionIndex(BpmnExtensionElement? parent, IReadOnlyList<BpmnExtensionElement> siblings, string namespaceUri, string localName) => 0;
    }
}

