using BlobEventGridIngress;

namespace Hasseware.AspNetCore.Components.Bpmn.Samples.Tests;

public sealed class BlobEventGridParserTests
{
    internal static string Created(string container, string blob, string etag = "0x8DD1") =>
        $$$"""[{"eventType":"Microsoft.Storage.BlobCreated","subject":"/blobServices/default/containers/{{{container}}}/blobs/{{{blob}}}","data":{"eTag":"{{{etag}}}"}}]""";

    [Fact]
    public void AnswersTheSubscriptionHandshake()
    {
        var events = BlobEventGridParser.Parse("""[{"eventType":"Microsoft.EventGrid.SubscriptionValidationEvent","data":{"validationCode":"abc123"}}]""", "bpmn", null);

        var only = Assert.Single(events);
        Assert.Equal(BlobEventKind.Validation, only.Kind);
        Assert.Equal("abc123", only.ValidationCode);
    }

    [Fact]
    public void ReadsACreatedBlobAsAnUpdateWithTheEtagQuotedAsTheStoreReportsIt()
    {
        var only = Assert.Single(BlobEventGridParser.Parse(Created("bpmn", "claims/a.bpmn"), "bpmn", null));

        Assert.Equal(BlobEventKind.Updated, only.Kind);
        Assert.Equal("claims/a.bpmn", only.Key?.Path.ToString());
        Assert.Equal("\"0x8DD1\"", only.Revision);
    }

    [Fact]
    public void KeepsAnEtagThatIsAlreadyQuoted() =>
        Assert.Equal("\"0x1\"", Assert.Single(BlobEventGridParser.Parse(Created("bpmn", "a.bpmn", "\\\"0x1\\\""), "bpmn", null)).Revision);

    [Fact]
    public void ReadsADeletedBlob()
    {
        var json = """[{"eventType":"Microsoft.Storage.BlobDeleted","subject":"/blobServices/default/containers/bpmn/blobs/a.bpmn","data":{}}]""";

        var only = Assert.Single(BlobEventGridParser.Parse(json, "bpmn", null));

        Assert.Equal(BlobEventKind.Deleted, only.Kind);
        Assert.Equal("a.bpmn", only.Key?.Path.ToString());
    }

    [Fact]
    public void MapsATenantFromTheBlobName()
    {
        var only = Assert.Single(BlobEventGridParser.Parse(Created("bpmn", "~tenants/acme/a.bpmn"), "bpmn", null));

        Assert.Equal("acme", only.Key?.Tenant);
    }

    [Fact]
    public void AppliesThePrefixTheStoreWasGiven()
    {
        Assert.Equal("a.bpmn", Assert.Single(BlobEventGridParser.Parse(Created("bpmn", "docs/a.bpmn"), "bpmn", "docs")).Key?.Path.ToString());
        Assert.Equal(BlobEventKind.Ignored, Assert.Single(BlobEventGridParser.Parse(Created("bpmn", "other/a.bpmn"), "bpmn", "docs")).Kind);
    }

    [Fact]
    public void IgnoresAnotherContainerAnotherEventTypeAndAStrayFile()
    {
        Assert.Equal(BlobEventKind.Ignored, Assert.Single(BlobEventGridParser.Parse(Created("elsewhere", "a.bpmn"), "bpmn", null)).Kind);
        Assert.Equal(BlobEventKind.Ignored, Assert.Single(BlobEventGridParser.Parse("""[{"eventType":"Microsoft.Storage.BlobTierChanged","subject":"/containers/bpmn/blobs/a.bpmn"}]""", "bpmn", null)).Kind);
        Assert.Equal(BlobEventKind.Ignored, Assert.Single(BlobEventGridParser.Parse(Created("bpmn", "../a.bpmn"), "bpmn", null)).Kind);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("")]
    public void GivesNothingForAnUnreadableDelivery(string json) => Assert.Empty(BlobEventGridParser.Parse(json, "bpmn", null));
}
