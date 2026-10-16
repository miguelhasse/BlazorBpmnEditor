using PostgresNotifyIngress;

namespace Hasseware.AspNetCore.Components.Bpmn.Samples.Tests;

public sealed class PostgresNotificationParserTests
{
    [Fact]
    public void ReadsAnUpdateInTheDefaultSpace()
    {
        var notification = PostgresNotificationParser.Parse("""{"tenant":"","path":"claims/a.bpmn","version":3,"op":"UPDATE"}""");

        Assert.NotNull(notification.Key);
        Assert.Null(notification.Key.Value.Tenant);
        Assert.Equal("claims/a.bpmn", notification.Key.Value.Path.ToString());
        Assert.Equal("3", notification.Revision);
        Assert.False(notification.Deleted);
    }

    [Fact]
    public void ReadsATenantAndADeletion()
    {
        var notification = PostgresNotificationParser.Parse("""{"tenant":"acme","path":"a.bpmn","version":7,"op":"DELETE"}""");

        Assert.Equal("acme", notification.Key?.Tenant);
        Assert.True(notification.Deleted);
    }

    [Theory]
    [InlineData("""{"tenant":"","path":"../secret.bpmn","version":1,"op":"INSERT"}""")]
    [InlineData("""{"tenant":"","path":"claims/","version":1,"op":"INSERT"}""")]
    [InlineData("""{"tenant":"a/b","path":"a.bpmn","version":1,"op":"INSERT"}""")]
    [InlineData("""{"tenant":"..","path":"a.bpmn","version":1,"op":"INSERT"}""")]
    [InlineData("""{"tenant":"","version":1}""")]
    [InlineData("""[1,2]""")]
    [InlineData("not json")]
    public void RejectsAPayloadThatIsNotADocument(string payload) => Assert.Null(PostgresNotificationParser.Parse(payload).Key);
}
