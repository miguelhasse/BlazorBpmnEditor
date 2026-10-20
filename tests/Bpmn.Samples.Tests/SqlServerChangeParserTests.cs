using SqlServerChangeIngress;

namespace Hasseware.AspNetCore.Components.Bpmn.Samples.Tests;

public sealed class SqlServerChangeParserTests
{
    [Fact]
    public void ReadsEveryRowOfAMessage()
    {
        var changes = SqlServerChangeParser.ParseMessage(
            """[{"tenant":"","path":"claims/a.bpmn","version":3,"op":"U"},{"tenant":"acme","path":"b.bpmn","version":null,"op":"D"},{"tenant":"","path":"c.json","version":9,"op":"I"}]""");

        Assert.Equal(3, changes.Count);

        Assert.Null(changes[0].Key.Tenant);
        Assert.Equal("claims/a.bpmn", changes[0].Key.Path.ToString());
        Assert.Equal("3", changes[0].Revision);
        Assert.False(changes[0].Deleted);

        Assert.Equal("acme", changes[1].Key.Tenant);
        Assert.True(changes[1].Deleted);
        Assert.Null(changes[1].Revision);

        Assert.Equal("9", changes[2].Revision);
    }

    [Fact]
    public void LeavesOutRowsThatAreNotDocuments()
    {
        var changes = SqlServerChangeParser.ParseMessage(
            """[{"tenant":"","path":"../secret.bpmn","version":1,"op":"I"},{"tenant":"a/b","path":"a.bpmn","version":1,"op":"I"},{"tenant":"","path":"ok.bpmn","version":"1","op":"X"},7,{"tenant":"","path":"ok.bpmn","version":2,"op":"U"}]""");

        var change = Assert.Single(changes);
        Assert.Equal("2", change.Revision);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"tenant":"","path":"a.bpmn","version":1,"op":"I"}""")]
    [InlineData("[]")]
    public void ReadsNothingFromABodyThatIsNotAnArrayOfRows(string payload) => Assert.Empty(SqlServerChangeParser.ParseMessage(payload));

    [Theory]
    [InlineData("I", 5L, false, "5")]
    [InlineData("U", 6L, false, "6")]
    [InlineData("D", null, true, null)]
    [InlineData("D", 7L, true, null)]
    [InlineData("U", null, true, null)]
    public void ReadsAChangeTrackingRow(string operation, long? version, bool deleted, string? revision)
    {
        var change = SqlServerChangeParser.FromRow("", "claims/a.bpmn", version, operation);

        Assert.NotNull(change);
        Assert.Null(change.Key.Tenant);
        Assert.Equal(deleted, change.Deleted);
        Assert.Equal(revision, change.Revision);
    }

    [Theory]
    [InlineData("", "claims/", "U")]
    [InlineData("", "claims/../a.bpmn", "U")]
    [InlineData("..", "a.bpmn", "U")]
    [InlineData("", "a.bpmn", "X")]
    [InlineData("", null, "U")]
    public void RejectsARowThatIsNotADocument(string tenant, string? path, string operation) =>
        Assert.Null(SqlServerChangeParser.FromRow(tenant, path, 1, operation));

    [Theory]
    [InlineData("dbo.BpmnDocuments", "[dbo].[BpmnDocuments]")]
    [InlineData("app.Documents_2", "[app].[Documents_2]")]
    public void QuotesATwoPartName(string name, string quoted) => Assert.Equal(quoted, SqlServerIngressOptions.QuoteTwoPartName(name));

    [Theory]
    [InlineData("BpmnDocuments")]
    [InlineData("dbo.Bpmn]Documents")]
    [InlineData("dbo.[x]; DROP TABLE y")]
    [InlineData("a.b.c")]
    public void RefusesANameThatIsNotTwoIdentifiers(string name) =>
        Assert.Throws<InvalidOperationException>(() => SqlServerIngressOptions.QuoteTwoPartName(name));
}
