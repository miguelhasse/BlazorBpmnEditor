using System.Data;

using Hasseware.AspNetCore.Components.Bpmn.Storage;

using Microsoft.Data.SqlClient;

namespace SqlServerChangeIngress;

/// <summary>Waits on the Service Broker queue the trigger in <c>service-broker.sql</c> sends to, and forwards each change to the change bus.</summary>
/// <remarks>
/// <para>
/// <c>WAITFOR (RECEIVE …)</c> returns as soon as a message arrives, so this is push, not polling; the timeout only bounds how long one command
/// runs. Messages are queued in the database, so changes made while this service is down are delivered when it returns.
/// </para>
/// <para>
/// A message is removed from the queue only when the transaction that received it commits, after its changes have been forwarded: delivery is
/// at least once, and the ingress drops a repeat. A change that cannot be forwarded is logged and the message is still committed, because a
/// message rolled back five times in a row disables the queue.
/// </para>
/// <para>
/// With several instances, each message reaches one of them (the queue has competing readers), so the bus must span instances.
/// </para>
/// </remarks>
/// <param name="connectionString">The documents database.</param>
/// <param name="options">The queue, the message type and timings.</param>
/// <param name="ingress">Where changes are forwarded.</param>
/// <param name="logger">Where failures are reported.</param>
public sealed partial class ServiceBrokerIngressService(
    string connectionString,
    SqlServerIngressOptions options,
    BpmnDocumentChangeIngress ingress,
    ILogger<ServiceBrokerIngressService> logger) : BackgroundService
{
    private const string EndDialog = "http://schemas.microsoft.com/SQL/ServiceBroker/EndDialog";
    private const string Error = "http://schemas.microsoft.com/SQL/ServiceBroker/Error";
    private const int BatchSize = 100;

    private readonly string _queue = SqlServerIngressOptions.QuoteTwoPartName(options.Queue);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync(stoppingToken);

                while (!stoppingToken.IsCancellationRequested) await ReceiveAsync(connection, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (SqlException exception) when (!stoppingToken.IsCancellationRequested)
            {
                LogConnectionFailed(exception);
                await DelayAsync(options.RetryDelay, stoppingToken);
            }
        }
    }

    /// <summary>Receives one batch, waiting up to <see cref="SqlServerIngressOptions.ReceiveTimeout"/> for it, and forwards it.</summary>
    /// <param name="connection">An open connection to the documents database.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The number of messages received.</returns>
    public async Task<int> ReceiveAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);

        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        var received = new List<(Guid Conversation, string MessageType, string? Body)>();
        var timeout = (int)options.ReceiveTimeout.TotalMilliseconds;
        await using (var receive = new SqlCommand(
            $"WAITFOR (RECEIVE TOP ({BatchSize}) conversation_handle, message_type_name, CAST(message_body AS nvarchar(max)) FROM {_queue}), TIMEOUT @timeout;",
            connection,
            transaction))
        {
            receive.Parameters.Add(new SqlParameter("@timeout", SqlDbType.Int) { Value = timeout });
            receive.CommandTimeout = (int)options.ReceiveTimeout.TotalSeconds + 30;

            await using var reader = await receive.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                received.Add((reader.GetGuid(0), reader.GetString(1), await reader.IsDBNullAsync(2, cancellationToken) ? null : reader.GetString(2)));
            }
        }

        foreach (var conversation in received.Select(message => message.Conversation).Distinct())
        {
            await using var end = new SqlCommand("END CONVERSATION @conversation;", connection, transaction);
            end.Parameters.Add(new SqlParameter("@conversation", SqlDbType.UniqueIdentifier) { Value = conversation });
            await end.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var (_, messageType, body) in received)
        {
            if (string.Equals(messageType, options.MessageType, StringComparison.Ordinal) && body is not null) await ForwardAsync(body, cancellationToken);
            else if (string.Equals(messageType, Error, StringComparison.Ordinal)) LogConversationError(body);
            else if (!string.Equals(messageType, EndDialog, StringComparison.Ordinal)) LogUnexpectedMessage(messageType);
        }

        await transaction.CommitAsync(cancellationToken);
        return received.Count;
    }

    private async Task ForwardAsync(string body, CancellationToken cancellationToken)
    {
        foreach (var change in SqlServerChangeParser.ParseMessage(body))
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
    }

    private static async Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Stopping.
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Service Broker connection failed; trying again.")]
    private partial void LogConnectionFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A change to {Key} could not be forwarded.")]
    private partial void LogForwardFailed(Exception exception, string key);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A Service Broker conversation ended with an error: {Error}")]
    private partial void LogConversationError(string? error);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Ignored a message of type {MessageType}.")]
    private partial void LogUnexpectedMessage(string messageType);
}
