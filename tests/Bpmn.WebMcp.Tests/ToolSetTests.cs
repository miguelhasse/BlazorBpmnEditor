using System.Text.Json;

using Hasseware.AspNetCore.Components.Bpmn.Tools;

namespace Hasseware.AspNetCore.Components.Bpmn.WebMcp.Tests;

/// <summary>The tools an editor offers a browser agent, and the names it offers them by.</summary>
public sealed class ToolSetTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static readonly string[] ReadOnlyTools =
    [
        "bpmn_get_diagram", "bpmn_describe_element", "bpmn_get_selection", "bpmn_validate", "bpmn_export", "bpmn_list_creatable",
    ];

    [Fact]
    public void OffersTheReadOnlyToolsUnderTheDefaultPrefix()
    {
        // A change here changes what every browser agent can see; make it deliberately.
        var tools = BpmnWebMcpToolSet.Create("bpmn_", string.Empty);

        Assert.Equal(ReadOnlyTools, tools.Descriptors.Select(tool => tool.Name));
    }

    [Fact]
    public void OffersNothingThatEditsOrDestroys()
    {
        var tools = BpmnWebMcpToolSet.Create("bpmn_", string.Empty);

        Assert.All(tools.Descriptors, tool =>
        {
            Assert.True(tool.Annotations.ReadOnly, tool.Name);
            Assert.False(tool.Annotations.Consequential, tool.Name);
        });
        Assert.DoesNotContain(tools.Descriptors, tool => tool.Name is "bpmn_create_element" or "bpmn_connect" or "bpmn_set_property" or "bpmn_delete");
    }

    [Fact]
    public void OffersTheEditingToolsOnlyWhenAllowed()
    {
        var tools = BpmnWebMcpToolSet.Create("bpmn_", string.Empty, allowMutations: true);

        Assert.Equal(
            [.. ReadOnlyTools, "bpmn_create_element", "bpmn_connect", "bpmn_set_property", "bpmn_delete", "bpmn_select", "bpmn_undo", "bpmn_redo"],
            tools.Descriptors.Select(tool => tool.Name));
        Assert.True(tools.Descriptors.Single(tool => tool.Name == "bpmn_delete").Annotations.Consequential);
    }

    [Fact]
    public void OffersThePersistenceToolsOnlyToABoundEditor()
    {
        var readOnly = BpmnWebMcpToolSet.Create("bpmn_", string.Empty, hasDocumentStore: true);
        var editing = BpmnWebMcpToolSet.Create("bpmn_", string.Empty, allowMutations: true, hasDocumentStore: true);

        Assert.Contains(readOnly.Descriptors, tool => tool.Name == "bpmn_get_document_info");
        Assert.DoesNotContain(readOnly.Descriptors, tool => tool.Name == "bpmn_save");
        Assert.Contains(editing.Descriptors, tool => tool.Name == "bpmn_save" && tool.Annotations.Consequential);
    }

    [Fact]
    public void ReplacesThePrefixAndAppendsTheInstanceSuffix()
    {
        var tools = BpmnWebMcpToolSet.Create("diagram_", "_left");

        Assert.Contains(tools.Descriptors, tool => tool.Name == "diagram_get_diagram_left");
        Assert.DoesNotContain(tools.Descriptors, tool => tool.Name.StartsWith("bpmn_", StringComparison.Ordinal));
    }

    [Fact]
    public void LeavesOutDisabledToolsWhateverThePrefix()
    {
        var tools = BpmnWebMcpToolSet.Create("diagram_", string.Empty, ["bpmn_export"]);

        Assert.DoesNotContain(tools.Descriptors, tool => tool.Name == "diagram_export");
        Assert.Equal(ReadOnlyTools.Length - 1, tools.Descriptors.Count);
    }

    [Fact]
    public void ResolvesARegisteredNameToItsCatalogTool()
    {
        var tools = BpmnWebMcpToolSet.Create("diagram_", "_2");

        Assert.Equal("bpmn_validate", tools.CatalogNameOf("diagram_validate_2"));
        Assert.Null(tools.CatalogNameOf("bpmn_validate"));
        Assert.Null(tools.CatalogNameOf("bpmn_delete"));
    }

    [Fact]
    public void MarksTheToolsThatReturnDiagramText()
    {
        var tools = BpmnWebMcpToolSet.Create("bpmn_", string.Empty);

        Assert.True(tools.Descriptors.Single(tool => tool.Name == "bpmn_get_diagram").Annotations.UntrustedContent);
        Assert.True(tools.Descriptors.Single(tool => tool.Name == "bpmn_export").Annotations.UntrustedContent);
        Assert.False(tools.Descriptors.Single(tool => tool.Name == "bpmn_list_creatable").Annotations.UntrustedContent);
    }

    [Fact]
    public void NamesNoLocationInAnySchema()
    {
        foreach (var tool in BpmnWebMcpToolSet.Create("bpmn_", string.Empty).Descriptors)
        {
            Assert.Equal("object", tool.InputSchema.GetProperty("type").GetString());
            var properties = tool.InputSchema.GetProperty("properties").EnumerateObject().Select(property => property.Name.ToUpperInvariant());
            Assert.DoesNotContain(properties, property => property.Contains("PATH", StringComparison.Ordinal)
                || property.Contains("URL", StringComparison.Ordinal)
                || property.Contains("CONNECTION", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void CarriesTheSchemaAsAnObjectTheBrowserCanRead()
    {
        var tool = BpmnWebMcpToolSet.Create("bpmn_", string.Empty).Descriptors.Single(tool => tool.Name == "bpmn_describe_element");

        // JSON the module hands to registerTool as inputSchema: an object, not a string of JSON.
        var json = JsonSerializer.Serialize(tool, Web);
        using var parsed = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Object, parsed.RootElement.GetProperty("inputSchema").ValueKind);
        Assert.True(parsed.RootElement.GetProperty("annotations").GetProperty("readOnly").GetBoolean());
    }

    [Theory]
    [InlineData("")]
    [InlineData("bad prefix")]
    [InlineData("bpmn:")]
    public void RefusesAPrefixAToolNameCannotHave(string prefix)
    {
        Assert.ThrowsAny<ArgumentException>(() => BpmnWebMcpToolSet.Create(prefix, string.Empty));
    }

    [Fact]
    public void OffersOnlyToolsTheCatalogKnows()
    {
        var catalog = BpmnToolCatalog.All.Select(tool => tool.Name).ToHashSet();

        Assert.All(ReadOnlyTools, name => Assert.Contains(name, catalog));
    }
}
