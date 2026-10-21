using System.Text.Json;

using static Hasseware.AspNetCore.Components.Bpmn.Tools.Tests.Documents;

namespace Hasseware.AspNetCore.Components.Bpmn.Tools.Tests;

/// <summary>The catalog every adapter registers, and the dispatcher it calls.</summary>
public sealed class CatalogTests
{
    public static TheoryData<string> ToolNames => [.. BpmnToolCatalog.All.Select(tool => tool.Name)];

    [Fact]
    public void OffersTheAgreedTools()
    {
        // A change here changes what every agent surface exposes; make it deliberately.
        Assert.Equal(
            ["bpmn_get_diagram", "bpmn_describe_element", "bpmn_get_selection", "bpmn_validate", "bpmn_export", "bpmn_list_creatable",
             "bpmn_create_element", "bpmn_connect", "bpmn_set_property", "bpmn_delete", "bpmn_select", "bpmn_undo", "bpmn_redo",
             "bpmn_get_document_info", "bpmn_save"],
            BpmnToolCatalog.All.Select(tool => tool.Name));
    }

    [Theory]
    [MemberData(nameof(ToolNames))]
    public void HasAWellFormedSchemaThatNamesNoLocation(string name)
    {
        var tool = BpmnToolCatalog.Find(name)!;
        using var schema = JsonDocument.Parse(tool.InputSchema);

        Assert.Equal("object", schema.RootElement.GetProperty("type").GetString());
        Assert.False(schema.RootElement.GetProperty("additionalProperties").GetBoolean());
        var properties = schema.RootElement.GetProperty("properties").EnumerateObject().Select(property => property.Name.ToUpperInvariant());
        Assert.DoesNotContain(properties, property => property.Contains("PATH", StringComparison.Ordinal)
            || property.Contains("URL", StringComparison.Ordinal)
            || property.Contains("CONNECTION", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(ToolNames))]
    public async Task DispatchesEveryToolByName(string name)
    {
        var result = await BpmnToolCatalog.InvokeAsync(Host(Simple(), isReadOnly: true), name, Empty(), Token);

        // Read-only tools succeed with no arguments; editing tools refuse as read-only or for missing arguments.
        Assert.NotEqual("There is no tool with that name.", result.Message);
    }

    [Fact]
    public void MarksOnlyDeletionAndSavingAsDestructiveAndOnlyReadingAsReadOnly()
    {
        Assert.Equal(["bpmn_delete", "bpmn_save"], BpmnToolCatalog.All.Where(tool => tool.Annotations.Destructive).Select(tool => tool.Name));
        Assert.All(BpmnToolCatalog.All.Where(tool => tool.Annotations.ReadOnly), tool => Assert.DoesNotContain(tool.Name, Editing));
    }

    [Fact]
    public async Task RefusesAnUnknownTool()
    {
        var result = await BpmnToolCatalog.InvokeAsync(Host(Simple()), "bpmn_launch", Empty(), Token);

        Assert.Equal(BpmnToolCodes.InvalidArgument, result.Code);
    }

    [Fact]
    public async Task RefusesMalformedArguments()
    {
        var result = await BpmnToolCatalog.InvokeAsync(Host(Simple()), "bpmn_delete", JsonElement.Parse("""{"elementIds":"review"}"""), Token);

        Assert.Equal(BpmnToolCodes.InvalidArgument, result.Code);
    }

    [Fact]
    public async Task WritesResultsInCamelCaseWithTheirData()
    {
        var result = await BpmnToolCatalog.InvokeAsync(Host(Simple()), "bpmn_describe_element", JsonElement.Parse("""{"elementId":"review"}"""), Token);
        using var json = JsonDocument.Parse(BpmnToolJson.Serialize(result));

        Assert.True(json.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("userTask", json.RootElement.GetProperty("data").GetProperty("type").GetString());
        Assert.False(json.RootElement.TryGetProperty("code", out _));
    }

    private static readonly string[] Editing = ["bpmn_create_element", "bpmn_connect", "bpmn_set_property", "bpmn_delete", "bpmn_select", "bpmn_undo", "bpmn_redo", "bpmn_save"];

    private static JsonElement Empty() => JsonElement.Parse("{}");
}
