using System.Text.Json;

namespace Hasseware.AspNetCore.Components.Bpmn.Tools.Tests;

/// <summary>
/// A tool's arguments are what the agent said, no more and no less: a missing required one is a refusal, not a null that clears something.
/// </summary>
public sealed class ToolArgumentTests
{
    private static JsonElement Json(string text) => JsonElement.Parse(text);

    [Fact]
    public void ReadsArgumentsThatAreComplete()
    {
        Assert.True(BpmnToolJson.TryRead<BpmnSetPropertyRequest>(Json("""{"elementId":"T1","name":"name","value":"Review"}"""), out var request));
        Assert.Equal("Review", request!.Value);
    }

    [Fact]
    public void AnExplicitNullValueStillClearsAProperty()
    {
        Assert.True(BpmnToolJson.TryRead<BpmnSetPropertyRequest>(Json("""{"elementId":"T1","name":"name","value":null}"""), out var request));
        Assert.Null(request!.Value);
    }

    [Fact]
    public void RefusesAPropertyChangeThatNamesNoValue()
    {
        // Without a value the request used to read as "set it to nothing", which removes the property.
        Assert.False(BpmnToolJson.TryRead<BpmnSetPropertyRequest>(Json("""{"elementId":"T1","name":"name"}"""), out _));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"elementIds":null}""")]
    public void RefusesASelectionOrADeletionThatNamesNoElements(string arguments)
    {
        Assert.False(BpmnToolJson.TryRead<BpmnSelectRequest>(Json(arguments), out _));
        Assert.False(BpmnToolJson.TryRead<BpmnDeleteRequest>(Json(arguments), out _));
    }

    [Fact]
    public void AnEmptyListStillClearsTheSelection()
    {
        Assert.True(BpmnToolJson.TryRead<BpmnSelectRequest>(Json("""{"elementIds":[]}"""), out var request));
        Assert.Empty(request!.ElementIds);
    }

    [Fact]
    public void RefusesAnArgumentTheToolDoesNotTake()
    {
        // A misspelt option was silently ignored, and the tool did something other than what was asked.
        Assert.False(BpmnToolJson.TryRead<BpmnCreateElementRequest>(Json("""{"type":"task","connectFromAnchors":true}"""), out _));
    }

    [Fact]
    public void LeavesOptionalArgumentsOptional()
    {
        Assert.True(BpmnToolJson.TryRead<BpmnCreateElementRequest>(Json("""{"type":"task"}"""), out var request));
        Assert.Equal("task", request!.Type);
        Assert.True(BpmnToolJson.TryRead<BpmnGetDiagramRequest>(Json("{}"), out _));
        Assert.True(BpmnToolJson.TryRead<BpmnNoArguments>(Json("{}"), out _));
    }

    [Fact]
    public async Task TheCatalogRefusesAMissingValueAndChangesNothing()
    {
        var host = Documents.Host(Documents.Simple());
        var before = host.Document;

        var result = await BpmnToolCatalog.InvokeAsync(host, "bpmn_set_property", Json("""{"elementId":"review","name":"name"}"""), Xunit.TestContext.Current.CancellationToken);

        Assert.False(result.Ok);
        Assert.Equal(BpmnToolCodes.InvalidArgument, result.Code);
        Assert.Same(before, host.Document);
    }
}
