using Hasseware.AspNetCore.Components.Bpmn.Storage;

using Npgsql;

namespace PostgresNotifyIngress;

/// <summary>Listens for <c>NOTIFY bpmn_documents</c> and forwards each notification to the change bus.</summary>
/// <remarks>
/// PostgreSQL pushes the notification to a connection that has issued <c>LISTEN</c>, so there is no polling. A notification sent while this connection
/// is down is lost; when the connection returns, editors learn of what they missed at their next save or when their own feed reconnects.
/// </remarks>
public sealed partial class PostgresNotifyIngressService(string connectionString, BpmnDocumentChangeIngress ingress, ILogger<PostgresNotifyIngressService> logger) : BackgroundService
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var connection = new NpgsqlConnection(connectionString);
                await connection.OpenAsync(stoppingToken);
                connection.Notification += (_, notification) => _ = ForwardAsync(notification.Payload, stoppingToken);

                await using (var listen = new NpgsqlCommand("LISTEN bpmn_documents", connection))
                {
                    await listen.ExecuteNonQueryAsync(stoppingToken);
                }

                while (!stoppingToken.IsCancellationRequested) await connection.WaitAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (NpgsqlException exception)
            {
                LogConnectionFailed(exception);
                await Task.Delay(RetryDelay, stoppingToken);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The notification connection failed; trying again.")]
    private partial void LogConnectionFailed(Exception exception);

    private async Task ForwardAsync(string payload, CancellationToken cancellationToken)
    {
        var notification = PostgresNotificationParser.Parse(payload);
        if (notification.Key is not { } key) return;

        if (notification.Deleted) await ingress.PublishDeletedAsync(key, cancellationToken);
        else await ingress.PublishUpdatedAsync(key, notification.Revision, cancellationToken);
    }
}
