using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;
using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions;
using Hasseware.AspNetCore.Components.Bpmn.Serialization;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Editing;

/// <summary>
/// Editing preserved extension content (specification sections 6.1 and 7).
/// </summary>
/// <remarks>
/// <para>
/// Section 6.1 requires extension content to survive whether or not the editor understands it.
/// That is a promise not to lose it, not a reason to refuse to change it — so every attribute and
/// every leaf text value is editable, as text, with no schema and no interpretation. What a
/// vendor's <c>retries="3"</c> means is the vendor's to know.
/// </para>
/// <para>
/// The path is what these tests are really about. It addresses children by index because names
/// repeat — a <c>zeebe:ioMapping</c> holds several <c>zeebe:input</c> elements — and a scheme that
/// addressed them by name would quietly edit whichever came first.
/// </para>
/// </remarks>
public sealed class ExtensionEditingTests
{
    // --- what is on offer -------------------------------------------------------------

    [Fact]
    public void EveryAttributeAtEveryDepthIsOffered()
    {
        var snapshot = BpmnElementInspector.Describe(Vendor(), "Task_1");

        Assert.NotNull(snapshot);
        Assert.Equal(
            [
                "extension:0@retries",
                "extension:0@type",
                "extension:1/0@source",
                "extension:1/0@target",
                "extension:1/1@source",
                "extension:1/1@target",
            ],
            snapshot.ExtensionProperties.Select(property => property.Name));
    }

    [Fact]
    public void RepeatedSiblingsAreNumberedAndLoneOnesAreNot()
    {
        // The label has to say which of the two inputs you are editing, and must not say "[1]" on
        // the one element there is only one of.
        var snapshot = BpmnElementInspector.Describe(Vendor(), "Task_1");

        Assert.Equal(
            ["taskDefinition @retries", "taskDefinition @type"],
            snapshot!.ExtensionProperties.Take(2).Select(property => property.Label));

        Assert.Equal(
            ["ioMapping / input[1] @source", "ioMapping / input[1] @target"],
            snapshot.ExtensionProperties.Skip(2).Take(2).Select(property => property.Label));

        Assert.Equal(
            ["ioMapping / input[2] @source", "ioMapping / input[2] @target"],
            snapshot.ExtensionProperties.Skip(4).Take(2).Select(property => property.Label));
    }

    [Fact]
    public void NoExtensionValueAppearsAmongTheStandardProperties()
    {
        /*
         * Section 7: the snapshot "must not include stale or extension-only values as standard
         * fields". Two lists is what makes that structural rather than a rule a surface has to
         * remember, and the prefix makes a collision impossible even if they were merged.
         */
        var snapshot = BpmnElementInspector.Describe(Vendor(), "Task_1");

        Assert.DoesNotContain(snapshot!.Properties, property => property.Name.StartsWith("extension:", StringComparison.Ordinal));
        Assert.DoesNotContain(snapshot.Properties, property => property.Value == "charge-card");
        Assert.All(snapshot.ExtensionProperties, property => Assert.StartsWith("extension:", property.Name, StringComparison.Ordinal));
    }

    [Fact]
    public void LeafTextIsOfferedAndContainerWhitespaceIsNot()
    {
        // The reader's own rule: text is a value on a leaf, and whitespace between children on a
        // container. Offering the container's would present indentation as something to edit.
        var document = WithExtensions(new BpmnExtensionElement(
            Vendor_Namespace,
            "script",
            NoAttributes,
            [],
            "return true;"));

        var snapshot = BpmnElementInspector.Describe(document, "Task_1");
        var text = Assert.Single(snapshot!.ExtensionProperties);

        Assert.Equal("extension:0#text", text.Name);
        Assert.Equal("script text", text.Label);
        Assert.Equal("return true;", text.Value);
        Assert.Equal(BpmnPropertyKind.MultilineText, text.Kind);
    }

    // --- writing ----------------------------------------------------------------------

    [Fact]
    public void EditingAnAttributeWritesItAndLeavesTheRestAlone()
    {
        var result = Apply(Vendor(), new UpdatePropertiesCommand("Task_1", "extension:0@type", "charge-card-v2"));

        Assert.True(result.IsApplied, Codes(result));

        var extensions = result.Document.FindFlowNode("Task_1")!.Extras.Extensions;

        Assert.Equal("charge-card-v2", extensions[0].Attributes["type"]);
        Assert.Equal("3", extensions[0].Attributes["retries"]);

        // The untouched sibling subtree is the same instance: nothing was rebuilt that did not have
        // to be, which is the cheapest way to be sure nothing was lost rebuilding it.
        Assert.Same(Vendor().FindFlowNode("Task_1")!.Extras.Extensions[1].Children[0].LocalName, extensions[1].Children[0].LocalName);
        Assert.Equal(2, extensions[1].Children.Count);
    }

    [Fact]
    public void EditingANestedValueReachesTheRightSibling()
    {
        // The whole reason the path is by index: both children are called `input`.
        var result = Apply(Vendor(), new UpdatePropertiesCommand("Task_1", "extension:1/1@target", "totalAmount"));

        Assert.True(result.IsApplied, Codes(result));

        var mapping = result.Document.FindFlowNode("Task_1")!.Extras.Extensions[1];

        Assert.Equal("orderId", mapping.Children[0].Attributes["target"]);
        Assert.Equal("totalAmount", mapping.Children[1].Attributes["target"]);
    }

    [Fact]
    public void ClearingAnAttributeRemovesItRatherThanEmptyingIt()
    {
        // Same rule as the standard fields: an empty box means the thing is not there. An
        // attribute written as retries="" says something different from no retries attribute.
        var result = Apply(Vendor(), new UpdatePropertiesCommand("Task_1", "extension:0@retries", string.Empty));

        var definition = result.Document.FindFlowNode("Task_1")!.Extras.Extensions[0];

        Assert.False(definition.Attributes.ContainsKey("retries"));
        Assert.Equal("charge-card", definition.Attributes["type"]);
    }

    [Fact]
    public void LeafTextCanBeEdited()
    {
        var document = WithExtensions(new BpmnExtensionElement(
            Vendor_Namespace, "script", NoAttributes, [], "return true;"));

        var result = Apply(document, new UpdatePropertiesCommand("Task_1", "extension:0#text", "return false;"));

        Assert.True(result.IsApplied, Codes(result));
        Assert.Equal("return false;", result.Document.FindFlowNode("Task_1")!.Extras.Extensions[0].Text);
    }

    [Fact]
    public void AnUnknownPathIsRefused()
    {
        // The descriptors are generated from the element's own tree, so a path that is not among
        // them is not a property of this element — the same answer a bogus BPMN attribute gets.
        var result = Apply(Vendor(), new UpdatePropertiesCommand("Task_1", "extension:9@nothing", "x"));

        Assert.False(result.IsApplied);
        Assert.Equal("EDIT-070", Assert.Single(result.Rejections).Code);
    }

    [Fact]
    public void AnElementWithNoExtensionsOffersNoneAndAcceptsNone()
    {
        var plain = new BpmnDocumentBuilder()
            .Node("Task_1", BpmnFlowNodeType.Task, new Bounds(200, 120, 100, 80), "Work")
            .Build();

        Assert.Empty(BpmnElementInspector.Describe(plain, "Task_1")!.ExtensionProperties);
        Assert.False(Apply(plain, new UpdatePropertiesCommand("Task_1", "extension:0@type", "x")).IsApplied);
    }

    [Fact]
    public void ReadonlyOffersTheValuesForInspectionOnly()
    {
        var snapshot = BpmnElementInspector.Describe(Vendor(), "Task_1", isReadOnly: true);

        Assert.NotEmpty(snapshot!.ExtensionProperties);
        Assert.All(snapshot.ExtensionProperties, property => Assert.True(property.IsReadOnly));

        // And the editor agrees, rather than relying on the surface to disable the box.
        var descriptor = snapshot.ExtensionProperties[0];
        var result = Apply(Vendor(), new UpdatePropertiesCommand("Task_1", descriptor.Name, "x"));

        Assert.True(result.IsApplied, "a read-only snapshot must not make the document read-only");
    }

    // --- and out again ----------------------------------------------------------------

    [Fact]
    public void AnEditedExtensionSurvivesARoundTrip()
    {
        // An edit that does not reach the file is not an edit. This is the one that would catch a
        // writer that re-emitted the extension from something other than the edited model.
        var edited = Apply(Vendor(), new UpdatePropertiesCommand("Task_1", "extension:0@type", "charge-card-v2")).Document;
        var written = BpmnXmlReader.Read(BpmnXmlWriter.Write(edited).Xml).Document;

        var definition = written.FindFlowNode("Task_1")!.Extras.Extensions[0];

        Assert.Equal("charge-card-v2", definition.Attributes["type"]);
        Assert.Equal("3", definition.Attributes["retries"]);

        // Structure intact: two extension elements, the second still holding both inputs in order.
        Assert.Equal(2, written.FindFlowNode("Task_1")!.Extras.Extensions.Count);
        Assert.Equal(
            ["orderId", "amount"],
            written.FindFlowNode("Task_1")!.Extras.Extensions[1].Children.Select(child => child.Attributes["source"]));
    }

    [Fact]
    public void EditingARealZeebeFileThroughThePathsItOffers()
    {
        var path = Path.Combine(FindRepositoryRoot(), "playground", "Bpmn.Demo", "wwwroot", "samples", "zeebe-process.bpmn");
        var document = BpmnXmlReader.Read(File.ReadAllText(path)).Document;

        var snapshot = BpmnElementInspector.Describe(document, "ServiceTask_ChargeCard");
        var header = Assert.Single(
            snapshot!.ExtensionProperties,
            property => property.Label.Contains("header @value", StringComparison.Ordinal));

        var result = Apply(document, new UpdatePropertiesCommand("ServiceTask_ChargeCard", header.Name, "low"));

        Assert.True(result.IsApplied, Codes(result));

        var written = BpmnXmlReader.Read(BpmnXmlWriter.Write(result.Document).Xml).Document;
        var headers = written.FindFlowNode("ServiceTask_ChargeCard")!.Extras.Extensions
            .Single(extension => extension.LocalName == "taskHeaders");

        Assert.Equal("low", headers.Children[0].Attributes["value"]);
        Assert.Equal("priority", headers.Children[0].Attributes["key"]);
    }

    // --- helpers ----------------------------------------------------------------------

    private const string Vendor_Namespace = "http://camunda.org/schema/zeebe/1.0";

    private static readonly Dictionary<string, string> NoAttributes = new(StringComparer.Ordinal);

    private static Dictionary<string, string> Attributes(params (string Name, string Value)[] pairs) =>
        pairs.ToDictionary(pair => pair.Name, pair => pair.Value, StringComparer.Ordinal);

    private static BpmnEditResult Apply(BpmnDocument document, BpmnEditCommand command) =>
        BpmnDocumentEditor.Apply(document, command);

    private static string Codes(BpmnEditResult result) =>
        string.Join(", ", result.Rejections.Select(rejection => $"{rejection.Code}: {rejection.Message}"));

    /// <summary>A Task carrying the shape a real Zeebe file has: a leaf, and a container of repeats.</summary>
    private static BpmnDocument Vendor() => WithExtensions(
        new BpmnExtensionElement(
            Vendor_Namespace,
            "taskDefinition",
            Attributes(("type", "charge-card"), ("retries", "3")),
            []),
        new BpmnExtensionElement(
            Vendor_Namespace,
            "ioMapping",
            NoAttributes,
            [
                new BpmnExtensionElement(Vendor_Namespace, "input", Attributes(("source", "orderId"), ("target", "orderId")), []),
                new BpmnExtensionElement(Vendor_Namespace, "input", Attributes(("source", "amount"), ("target", "amount")), []),
            ]));

    private static BpmnDocument WithExtensions(params BpmnExtensionElement[] extensions)
    {
        var document = new BpmnDocumentBuilder()
            .Node("Task_1", BpmnFlowNodeType.ServiceTask, new Bounds(200, 120, 100, 80), "Work")
            .Build();

        return document with
        {
            FlowNodes = [.. document.FlowNodes.Select(node => node.Id == "Task_1"
                ? node with { Extras = new BpmnElementExtras { Extensions = extensions } }
                : node)],
        };
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "playground")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("the repository root was not found");
    }
}
