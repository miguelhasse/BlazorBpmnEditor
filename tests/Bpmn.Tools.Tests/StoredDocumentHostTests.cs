using Hasseware.AspNetCore.Components.Bpmn.Editing;

using static Hasseware.AspNetCore.Components.Bpmn.Tools.Tests.Documents;

namespace Hasseware.AspNetCore.Components.Bpmn.Tools.Tests;

/// <summary>A stored document, loaded for a call: the tools read it, report the store's revision, and never edit it.</summary>
public sealed class StoredDocumentHostTests
{
    [Fact]
    public async Task ReportsTheStoredRevisionWithEveryResult()
    {
        var host = new BpmnStoredDocumentToolHost(Simple(), "etag-7", Agent);

        var result = await BpmnTools.ValidateAsync(host, Token);

        Assert.True(result.Ok);
        Assert.Equal("etag-7", result.Revision);
    }

    [Fact]
    public async Task ReadsTheDocumentItWasGiven()
    {
        var host = new BpmnStoredDocumentToolHost(Simple(), "r1", Agent);

        var result = await BpmnTools.DescribeElementAsync(host, new BpmnDescribeElementRequest("review"), Token);

        Assert.True(result.Ok);
    }

    [Fact]
    public async Task RefusesEveryEditBecauseTheDocumentIsReadOnly()
    {
        var host = new BpmnStoredDocumentToolHost(Simple(), "r1", Agent);

        var set = await BpmnTools.SetPropertyAsync(host, new BpmnSetPropertyRequest("review", "name", "Hacked"), Token);
        var delete = await BpmnTools.DeleteAsync(host, new BpmnDeleteRequest(["review"]), Token);

        Assert.Equal(BpmnToolCodes.ReadOnly, set.Code);
        Assert.Equal(BpmnToolCodes.ReadOnly, delete.Code);
    }

    [Fact]
    public void RefusesToActForTheUser()
    {
        Assert.Throws<ArgumentException>(() => new BpmnStoredDocumentToolHost(Simple(), "r1", BpmnEditOrigin.User));
    }
}
