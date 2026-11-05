using System.Globalization;
using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Editing;

public sealed class ExtensionDescriptorTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(64)]
    public void DescriptorTraversalMatchesSiblingScanWithMixedNamespacesAndNestedContent(int count)
    {
        var children = Enumerable.Range(0, count).Select(index => new BpmnExtensionElement(
            index % 2 == 0 ? "urn:first" : "urn:second",
            (index % 4) switch { 0 => "input", 1 => "Input", 2 => "input", _ => $"unique{index}" },
            new Dictionary<string, string> { ["z"] = "last", ["a"] = "first" },
            index % 3 == 0
                ? [new("urn:other", "input", new Dictionary<string, string>(), [], ""),
                    new("urn:third", "input", new Dictionary<string, string>(), [], "nested")]
                : [],
            index % 2 == 0 ? "text" : null)).ToList();
        BpmnExtensionElement[] roots =
        [
            new("urn:root", "mapping", new Dictionary<string, string>(), children, "container text"),
            new("urn:another", "mapping", new Dictionary<string, string>(), children, null),
        ];
        var node = new BpmnFlowNode("task", BpmnFlowNodeType.Task, "process")
        {
            Extras = new BpmnElementExtras { Extensions = roots },
        };
        var expected = new List<(string Name, string Label, BpmnPropertyKind Kind, string? Value)>();
        Scan(roots, "", "", expected);

        var actual = BpmnElementInspector.ExtensionProperties(node);

        Assert.Equal(expected, actual.Select(property => (property.Name, property.Label, property.Kind, property.Value)));
        Assert.All(actual, property => Assert.False(property.IsReadOnly));
    }

    [Fact]
    public void FreshTraversalObservesHostSiblingListMutations()
    {
        var children = Enumerable.Range(0, 9).Select(index =>
            new BpmnExtensionElement("urn:test", "input", new Dictionary<string, string>(), [], $"value{index}")).ToList();
        var node = new BpmnFlowNode("task", BpmnFlowNodeType.Task, "process")
        {
            Extras = new BpmnElementExtras { Extensions = children },
        };
        Assert.Equal("input[9] text", BpmnElementInspector.ExtensionProperties(node)[^1].Label);
        children.RemoveAt(0);
        children[0] = children[0] with { LocalName = "unique" };

        var actual = BpmnElementInspector.ExtensionProperties(node);

        Assert.Equal("unique text", actual[0].Label);
        Assert.Equal("input[1] text", actual[1].Label);
        Assert.Equal("input[7] text", actual[^1].Label);
        children.Add(new("urn:other", "input", new Dictionary<string, string>(), [], "new"));
        Assert.Equal("input[8] text", BpmnElementInspector.ExtensionProperties(node)[^1].Label);
    }

    private static void Scan(
        IReadOnlyList<BpmnExtensionElement> elements, string path, string trail,
        List<(string Name, string Label, BpmnPropertyKind Kind, string? Value)> found)
    {
        for (var index = 0; index < elements.Count; index++)
        {
            var element = elements[index];
            var here = path.Length == 0 ? index.ToString(CultureInfo.InvariantCulture)
                : $"{path}/{index.ToString(CultureInfo.InvariantCulture)}";
            var matches = elements.Where(item => item.LocalName == element.LocalName).ToList();
            var ordinal = elements.Take(index + 1).Count(item => item.LocalName == element.LocalName);
            var name = matches.Count > 1
                ? $"{element.LocalName}[{ordinal.ToString(CultureInfo.InvariantCulture)}]" : element.LocalName;
            var label = trail.Length == 0 ? name : $"{trail} / {name}";
            foreach (var attribute in element.Attributes.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                found.Add(($"extension:{here}@{attribute.Key}", $"{label} @{attribute.Key}", BpmnPropertyKind.Text, attribute.Value));
            }
            if (element.Children.Count == 0 && element.Text is not null)
            {
                found.Add(($"extension:{here}#text", $"{label} text", BpmnPropertyKind.MultilineText, element.Text));
            }
            Scan(element.Children, here, label, found);
        }
    }
}
