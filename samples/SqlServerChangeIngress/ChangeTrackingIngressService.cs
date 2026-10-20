using System.Data;

using Hasseware.AspNetCore.Components.Bpmn.Storage;

using Microsoft.Data.SqlClient;

namespace SqlServerChangeIngress;

/// <summary>Asks Change Tracking, on an interval, which rows of the documents table changed, and forwards them to the change bus.</summary>
/// <remarks>
/// <para>
/// Change Tracking is pull-based: SQL Server records that a row changed, and this service asks. It starts from the version current when it
/// starts, because what happened before is found by the editors' own checks; it then remembers the last version it read, in memory.
/// </para>
/// <para>
/// When the service falls further behind than the table's retention (a long outage), Change Tracking can no longer say what changed. It then
/// starts again from the current version and logs it; editors learn of what was missed at their next save or catch-up check.
/// </para>
/// <para>
/// Every instance reads every change, so with several instances each forwards the same changes to its own bus; that is right for an
/// in-process bus, and harmless for one that spans instances, because the ingress and the binding ignore a revision they already have.
/// </para>
/// </remarks>
/// <param name="connectionString">The documents database.</param>
/// <param name="options">The table and timings.</param>
/// <param name="ingress">Where changes are forwarded.</param>
/// <param name="logger">Where failures are reported.</param>
public sealed partial class ChangeTrackingIngressService(
    string connectionString,
    SqlServerIngressOptions options,
    BpmnDocumentChangeIngress ingress,
    ILogger<ChangeTrackingIngressService> logger) : BackgroundService
{
    private readonly string _table = SqlServerIngressOptions.QuoteTwoPartName(options.Table);
    private long? _lastVersion;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.PollInterval);
        do
        {
            try
            {
                await using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync(stoppingToken);
                await PollAsync(connection, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (SqlException exception) when (!stoppingToken.IsCancellationRequested)
            {
                LogPollFailed(exception);
            }
        }
        while (await WaitAsync(timer, stoppingToken));
    }

    /// <summary>Reads the changes made since the last call and forwards them.</summary>
    /// <param name="connection">An open connection to the documents database.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The number of changed rows read; the first call only records where to start, and reads none.</returns>
    public async Task<int> PollAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);

        long current;
        long minimumValid;
        await using (var versions = new SqlCommand("SELECT CHANGE_TRACKING_CURRENT_VERSION(), CHANGE_TRACKING_MIN_VALID_VERSION(OBJECT_ID(@table));", connection))
        {
            versions.Parameters.Add(new SqlParameter("@table", SqlDbType.NVarChar, 261) { Value = _table });
            await using var reader = await versions.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            if (await reader.IsDBNullAsync(0, cancellationToken) || await reader.IsDBNullAsync(1, cancellationToken))
            {
                throw new InvalidOperationException($"Change Tracking is not enabled on {options.Table}; run change-tracking.sql.");
            }

            current = reader.GetInt64(0);
            minimumValid = reader.GetInt64(1);
        }

        if (_lastVersion is not { } last)
        {
            _lastVersion = current;
            return 0;
        }

        if (last < minimumValid)
        {
            LogHistoryLost(last, minimumValid);
            _lastVersion = current;
            return 0;
        }

        if (last == current) return 0;

        // Rows are read from the table as they are now, which may be newer than the change; a newer revision is what an editor needs anyway,
        // and the later change that made it is dropped by the ingress as a repeat.
        var changes = new List<SqlServerDocumentChange>();
        var read = 0;
        await using (var query = new SqlCommand(
            $"""
            SELECT c.Tenant, c.Path, d.Version, c.SYS_CHANGE_OPERATION
            FROM CHANGETABLE(CHANGES {_table}, @last) AS c
            LEFT JOIN {_table} AS d ON d.Tenant = c.Tenant AND d.Path = c.Path
            WHERE c.SYS_CHANGE_VERSION <= @current
            ORDER BY c.SYS_CHANGE_VERSION;
            """,
            connection))
        {
            query.Parameters.Add(new SqlParameter("@last", SqlDbType.BigInt) { Value = last });
            query.Parameters.Add(new SqlParameter("@current", SqlDbType.BigInt) { Value = current });

            await using var reader = await query.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                read++;
                long? version = await reader.IsDBNullAsync(2, cancellationToken) ? null : reader.GetInt64(2);
                var change = SqlServerChangeParser.FromRow(reader.GetString(0), reader.GetString(1), version, reader.GetString(3));
                if (change is not null) changes.Add(change);
            }
        }

        foreach (var change in changes)
        {
            try
            {
                await ingress.ForwardAsync(change, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogForwardFailed(exception, change.Key.ToString());
            }
        }

        _lastVersion = current;
        return read;
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken cancellationToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Reading Change Tracking failed; trying again at the next interval.")]
    private partial void LogPollFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Change Tracking no longer holds changes since version {Last} (oldest is {MinimumValid}); starting again from now.")]
    private partial void LogHistoryLost(long last, long minimumValid);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A change to {Key} could not be forwarded.")]
    private partial void LogForwardFailed(Exception exception, string key);
}
