using System.Text.RegularExpressions;

using Hasseware.AspNetCore.Components.Bpmn.Storage;

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;

using SqlServerChangeIngress;

namespace Hasseware.AspNetCore.Components.Bpmn.Samples.Tests;

/// <summary>
/// Both ingress modes against a real SQL Server, run only when <c>BPMN_SQLSERVER_CONNECTION</c> names a database the tests may create a table,
/// Service Broker objects and Change Tracking on. Rows are written directly, as a writer that bypasses the store would.
/// </summary>
public sealed partial class SqlServerChangeIngressTests : IAsyncDisposable
{
    private static readonly string? Connection = Environment.GetEnvironmentVariable("BPMN_SQLSERVER_CONNECTION");

    private readonly string _id = Guid.NewGuid().ToString("N");
    private readonly InProcessBpmnDocumentChangeBus _bus = new();
    private readonly List<BpmnDocumentChange> _heard = [];
    private SqlConnection? _connection;
    private bool _brokerCreated;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string Table => "BpmnDocuments_" + _id;

    [Fact]
    public async Task ServiceBrokerForwardsRowsWrittenDirectly()
    {
        var connection = await OpenAsync();
        if (!await ScalarAsync<bool>(connection, "SELECT is_broker_enabled FROM sys.databases WHERE database_id = DB_ID();"))
        {
            Assert.Skip("Service Broker is not enabled on the test database.");
        }

        _brokerCreated = true;
        await RunScriptAsync(connection, "service-broker.sql");

        var options = new SqlServerIngressOptions
        {
            Table = "dbo." + Table,
            Queue = "dbo.Queue_" + _id,
            MessageType = $"//Hasseware/Bpmn/{_id}/DocumentChanged",
            ReceiveTimeout = TimeSpan.FromSeconds(5),
        };
        var service = new ServiceBrokerIngressService(Connection!, options, new BpmnDocumentChangeIngress(_bus), NullLogger<ServiceBrokerIngressService>.Instance);
        await SubscribeAsync("a.bpmn", "b.bpmn");

        await ExecuteAsync(connection, $"INSERT INTO [{Table}] VALUES (N'', N'a.bpmn', N'Xml', 0x00, 1, 10, SYSUTCDATETIME()), (N'', N'b.bpmn', N'Xml', 0x00, 1, 20, SYSUTCDATETIME());");
        await ExecuteAsync(connection, $"UPDATE [{Table}] SET Version = 11 WHERE Path = N'a.bpmn';");
        await ExecuteAsync(connection, $"DELETE FROM [{Table}] WHERE Path = N'b.bpmn';");

        // Three messages, then the three EndDialog messages that ending them sends to the initiator side.
        var received = 0;
        for (var attempt = 0; attempt < 10 && received < 6; attempt++) received += await service.ReceiveAsync(connection, Token);

        Assert.Equal(6, received);
        // Each statement is its own conversation, and rows within one are unordered: like every backend signal, order is not promised.
        Assert.Equal(
            ["a.bpmn Updated 10", "a.bpmn Updated 11", "b.bpmn Deleted ", "b.bpmn Updated 20"],
            _heard.Select(change => $"{change.Key.Path} {change.Kind} {change.Revision}").Order(StringComparer.Ordinal));
        Assert.Equal(0, await ScalarAsync<int>(connection, $"SELECT COUNT(*) FROM sys.conversation_endpoints WHERE far_service LIKE N'//Hasseware/Bpmn/{_id}/%' AND state <> 'CD';"));
    }

    [Fact]
    public async Task ChangeTrackingForwardsRowsWrittenDirectly()
    {
        var connection = await OpenAsync();
        await RunScriptAsync(connection, "change-tracking.sql");

        var options = new SqlServerIngressOptions { Mode = SqlServerIngressMode.ChangeTracking, Table = "dbo." + Table };
        var service = new ChangeTrackingIngressService(Connection!, options, new BpmnDocumentChangeIngress(_bus), NullLogger<ChangeTrackingIngressService>.Instance);
        await SubscribeAsync("a.bpmn", "b.bpmn");

        await ExecuteAsync(connection, $"INSERT INTO [{Table}] VALUES (N'', N'b.bpmn', N'Xml', 0x00, 1, 20, SYSUTCDATETIME());");
        Assert.Equal(0, await service.PollAsync(connection, Token));

        await ExecuteAsync(connection, $"INSERT INTO [{Table}] VALUES (N'', N'a.bpmn', N'Xml', 0x00, 1, 10, SYSUTCDATETIME());");
        await ExecuteAsync(connection, $"DELETE FROM [{Table}] WHERE Path = N'b.bpmn';");
        Assert.Equal(2, await service.PollAsync(connection, Token));

        await ExecuteAsync(connection, $"UPDATE [{Table}] SET Version = 11 WHERE Path = N'a.bpmn';");
        Assert.Equal(1, await service.PollAsync(connection, Token));
        Assert.Equal(0, await service.PollAsync(connection, Token));

        Assert.Equal(
            ["a.bpmn Updated 10", "b.bpmn Deleted ", "a.bpmn Updated 11"],
            _heard.Select(change => $"{change.Key.Path} {change.Kind} {change.Revision}"));
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is null) return;

        if (_brokerCreated)
        {
            await ExecuteAsync(_connection, $"IF OBJECT_ID(N'dbo.[{Table}]') IS NOT NULL DROP TABLE [{Table}];");
            await ExecuteAsync(_connection, $"IF EXISTS (SELECT 1 FROM sys.services WHERE name = N'//Hasseware/Bpmn/{_id}/DocumentChanges/Initiator') DROP SERVICE [//Hasseware/Bpmn/{_id}/DocumentChanges/Initiator];");
            await ExecuteAsync(_connection, $"IF EXISTS (SELECT 1 FROM sys.services WHERE name = N'//Hasseware/Bpmn/{_id}/DocumentChanges/Target') DROP SERVICE [//Hasseware/Bpmn/{_id}/DocumentChanges/Target];");
            await ExecuteAsync(_connection, $"IF OBJECT_ID(N'dbo.[Queue_{_id}]') IS NOT NULL DROP QUEUE [Queue_{_id}];");
            await ExecuteAsync(_connection, $"IF EXISTS (SELECT 1 FROM sys.service_contracts WHERE name = N'//Hasseware/Bpmn/{_id}/DocumentChanges') DROP CONTRACT [//Hasseware/Bpmn/{_id}/DocumentChanges];");
            await ExecuteAsync(_connection, $"IF EXISTS (SELECT 1 FROM sys.service_message_types WHERE name = N'//Hasseware/Bpmn/{_id}/DocumentChanged') DROP MESSAGE TYPE [//Hasseware/Bpmn/{_id}/DocumentChanged];");
        }
        else
        {
            await ExecuteAsync(_connection, $"IF OBJECT_ID(N'dbo.[{Table}]') IS NOT NULL DROP TABLE [{Table}];");
        }

        await _connection.DisposeAsync();
    }

    private async Task<SqlConnection> OpenAsync()
    {
        if (Connection is null) Assert.Skip("BPMN_SQLSERVER_CONNECTION is not set.");

        _connection = new SqlConnection(Connection);
        await _connection.OpenAsync(Token);
        await ExecuteAsync(_connection, $"CREATE TABLE [{Table}] ([Tenant] nvarchar(128) NOT NULL, [Path] nvarchar(512) COLLATE Latin1_General_100_BIN2 NOT NULL, [Format] nvarchar(8) NOT NULL, [Content] varbinary(max) NOT NULL, [Size] bigint NOT NULL, [Version] bigint NOT NULL, [ModifiedUtc] datetime2 NOT NULL, CONSTRAINT [PK_{Table}] PRIMARY KEY ([Tenant], [Path]));");
        return _connection;
    }

    private async Task SubscribeAsync(params string[] paths)
    {
        foreach (var path in paths)
        {
            await _bus.SubscribeAsync(
                new BpmnDocumentKey(null, BpmnDocumentPath.Parse(path)),
                change =>
                {
                    lock (_heard) _heard.Add(change);
                    return ValueTask.CompletedTask;
                },
                Token);
        }
    }

    // The scripts as shipped, renamed so that each test has objects of its own.
    private async Task RunScriptAsync(SqlConnection connection, string name)
    {
        var script = (await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, name), Token))
            .Replace("BpmnDocuments", Table, StringComparison.Ordinal)
            .Replace("BpmnDocumentChangeQueue", "Queue_" + _id, StringComparison.Ordinal)
            .Replace("//Hasseware/Bpmn/", $"//Hasseware/Bpmn/{_id}/", StringComparison.Ordinal);

        foreach (var batch in BatchSeparator().Split(script).Where(batch => !string.IsNullOrWhiteSpace(batch)))
        {
            await ExecuteAsync(connection, batch);
        }
    }

    private static async Task ExecuteAsync(SqlConnection connection, string statement)
    {
        await using var command = new SqlCommand(statement, connection);
        await command.ExecuteNonQueryAsync(Token);
    }

    private static async Task<T> ScalarAsync<T>(SqlConnection connection, string statement)
    {
        await using var command = new SqlCommand(statement, connection);
        return (T)(await command.ExecuteScalarAsync(Token))!;
    }

    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BatchSeparator();
}
