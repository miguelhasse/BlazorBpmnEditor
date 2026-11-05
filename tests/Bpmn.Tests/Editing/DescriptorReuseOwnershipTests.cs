using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;
using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions;
using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions.Schema;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Editing;

public sealed class DescriptorReuseOwnershipTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void DescribeWithMutatingBuildersPreservesCallbackAndFreshRawSemantics(bool readOnly, bool template)
    {
        var attributes = new Dictionary<string, string> { ["value"] = "before" };
        var roots = new List<BpmnExtensionElement>
        {
            new("urn:host", "opaque", attributes, []),
            new("urn:host", "nested", new Dictionary<string, string>(), [], "before text"),
        };
        var document = Document(roots, template);
        var options = new BpmnExtensionOptions().AddZeebe();
        var seen = new List<string?>();
        options.GroupBuilders.Clear();
        options.GroupBuilders.Add(new CallbackBuilder(snapshot =>
        {
            attributes["value"] = "after";
            roots[1] = roots[1] with { Text = "after text" };
            var raw = (IList<BpmnPropertyDescriptor>)snapshot.ExtensionProperties;
            if (readOnly)
                Assert.Throws<NotSupportedException>(() => raw[0] = raw[0] with { Value = "snapshot mutation" });
            else
                raw[0] = raw[0] with { Value = "snapshot mutation" };
        }));
        options.GroupBuilders.Add(new CallbackBuilder(snapshot => seen.Add(snapshot.ExtensionProperties[0].Value)));
        var templates = new BpmnTemplateOptions();
        templates.Registry.Add(new() { Id = "template", AppliesTo = ["bpmn:Task"] });

        var result = BpmnElementInspector.Describe(document, "task", readOnly, options, template ? templates : null)!;

        Assert.Equal([readOnly ? "before" : "snapshot mutation"], seen);
        Assert.Equal(template ? readOnly ? "before" : "snapshot mutation" : "after", result.ExtensionProperties[0].Value);
        Assert.Equal(template ? "before text" : "after text", result.ExtensionProperties[1].Value);
        Assert.All(result.ExtensionProperties, property => Assert.Equal(readOnly, property.IsReadOnly));
        Assert.Equal("after", attributes["value"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DescribeWithMutatingLabelsRereadsFallback(bool readOnly)
    {
        var attributes = new Dictionary<string, string> { ["value"] = "before" };
        var document = Document([new("urn:host", "opaque", attributes, [])]);
        var labels = new MutatingLabels(() => attributes["value"] = "from label");
        var options = new BpmnExtensionOptions().AddZeebe();
        options.LabelProvider = labels;

        var result = BpmnElementInspector.Describe(document, "task", readOnly, options)!;

        Assert.True(labels.Calls > 0);
        Assert.Equal("from label", Assert.Single(result.ExtensionProperties).Value);
        Assert.Equal(readOnly, result.ExtensionProperties[0].IsReadOnly);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void DescribeWithBuiltInAndForwardingBuildersProducesEquivalentOrderedSnapshots(bool readOnly, bool alwaysOffer)
    {
        var children = Enumerable.Range(0, 12).Select(index => new BpmnExtensionElement(
            BpmnExtensionSchemas.ZeebeNamespace, "input",
            new Dictionary<string, string> { ["target"] = $"target{index}", ["source"] = $"=source{index}" }, [])).ToList();
        var document = Document(
        [
            new(BpmnExtensionSchemas.ZeebeNamespace, "ioMapping", new Dictionary<string, string>(), children),
            new("urn:host", "opaque", new Dictionary<string, string> { ["value"] = "keep" }, []),
        ]);
        var options = new BpmnExtensionOptions().AddZeebe().AddCamunda7();
        options.AlwaysOfferRegisteredSchemas = alwaysOffer;
        var expected = BpmnElementInspector.Describe(document, "task", readOnly, options)!;
        options.GroupBuilders[0] = new ForwardingBuilder();

        var actual = BpmnElementInspector.Describe(document, "task", readOnly, options)!;

        Assert.Equivalent(expected, actual, strict: true);
        Assert.Equal(expected.ExtensionProperties.Select(property => (property.Name, property.Label, property.Value, property.IsReadOnly)),
            actual.ExtensionProperties.Select(property => (property.Name, property.Label, property.Value, property.IsReadOnly)));
        Assert.Equal(expected.ExtensionGroups.Select(group => group.Id), actual.ExtensionGroups.Select(group => group.Id));
    }

    [Fact]
    public void DescribeSeparateOperationsObserveMutableRootsAndChildren()
    {
        var children = new List<BpmnExtensionElement> { new("urn:host", "leaf", new Dictionary<string, string>(), [], "before") };
        var roots = new List<BpmnExtensionElement> { new("urn:host", "root", new Dictionary<string, string>(), children) };
        var document = Document(roots);
        var options = new BpmnExtensionOptions().AddZeebe();
        var first = BpmnElementInspector.Describe(document, "task", extensionOptions: options)!;
        children[0] = children[0] with { Text = "after" };
        roots.Insert(0, new("urn:host", "new", new Dictionary<string, string>(), [], "new"));

        var second = BpmnElementInspector.Describe(document, "task", extensionOptions: options)!;

        Assert.Equal("before", Assert.Single(first.ExtensionProperties).Value);
        Assert.Equal(["new", "after"], second.ExtensionProperties.Select(property => property.Value));
        Assert.Equal(["extension:0#text", "extension:1/0#text"], second.ExtensionProperties.Select(property => property.Name));
    }

    [Fact]
    public void DescribeBuiltInWideMappingStaysWithinAllocationBudget()
    {
        var children = Enumerable.Range(0, 600).Select(index => new BpmnExtensionElement(
            BpmnExtensionSchemas.ZeebeNamespace, "input",
            new Dictionary<string, string> { ["target"] = $"target{index}", ["source"] = $"=source{index}" }, [])).ToArray();
        var document = Document(
            [new(BpmnExtensionSchemas.ZeebeNamespace, "ioMapping", new Dictionary<string, string>(), children)]);
        var options = new BpmnExtensionOptions().AddZeebe();
        for (var warm = 0; warm < 3; warm++)
            BpmnElementInspector.Describe(document, "task", extensionOptions: options);
        var before = GC.GetAllocatedBytesForCurrentThread();

        var snapshot = BpmnElementInspector.Describe(document, "task", extensionOptions: options)!;
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Empty(snapshot.ExtensionProperties);
        Assert.Equal(600, snapshot.ExtensionGroups.Single(group => group.Id == "inputs").Entries[0].Rows!.Count);
        Assert.InRange(allocated, 0, 1_400_000);
    }

    private static BpmnDocument Document(IReadOnlyList<BpmnExtensionElement> roots, bool template = false)
    {
        var document = new BpmnDocumentBuilder()
            .Node("task", BpmnFlowNodeType.ServiceTask, new Bounds(0, 0, 100, 80)).Build();
        return document with
        {
            FlowNodes = [document.FlowNodes[0] with
            {
                Extras = new()
                {
                    Extensions = roots,
                    ForeignAttributes = template
                        ? new Dictionary<string, string>
                        { [$"{{{BpmnExtensionSchemas.ZeebeNamespace}}}modelerTemplate"] = "template" } : new Dictionary<string, string>(),
                },
            }],
        };
    }

    private sealed class CallbackBuilder(Action<BpmnElementSnapshot> callback) : IBpmnExtensionGroupBuilder
    {
        public BpmnExtensionGroupBuildResult Build(BpmnElementSnapshot snapshot, object element, BpmnElementExtras extras,
            BpmnExtensionRegistry registry, IBpmnPropertyLabelProvider labels)
        {
            callback(snapshot);
            return new([], []);
        }
    }

    private sealed class ForwardingBuilder : IBpmnExtensionGroupBuilder
    {
        public BpmnExtensionGroupBuildResult Build(BpmnElementSnapshot snapshot, object element, BpmnElementExtras extras,
            BpmnExtensionRegistry registry, IBpmnPropertyLabelProvider labels) =>
            new BpmnSchemaPropertiesProvider().Build(snapshot, element, extras, registry, labels);
    }

    private sealed class MutatingLabels(Action callback) : IBpmnPropertyLabelProvider
    {
        public int Calls { get; private set; }

        public string GetLabel(string key, string englishFallback)
        {
            Calls++;
            callback();
            return englishFallback;
        }
    }
}
