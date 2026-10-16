using System.Net;
using System.Threading.Channels;

using Microsoft.AspNetCore.Http.Connections.Client;
using Microsoft.AspNetCore.SignalR.Client;

namespace Hasseware.AspNetCore.Components.Bpmn.Storage.SignalR.Client;

/// <summary>
/// Hears about changes other parties make to a document over SignalR: one hub connection per subscribed document, reconnected
/// with backoff, and reported after every gap so the binding makes its catch-up check.
/// </summary>
/// <remarks>
/// SignalR's own keepalive finds a dead connection, so the subscriber adds no heartbeat of its own; when the connection closes it
/// reconnects and raises <see cref="IBpmnDocumentChangeSubscriber.Resubscribed"/>. A 401 or 403 stops the retrying and raises
/// <see cref="ReconnectingBpmnDocumentChangeSubscriber.AuthorizationRequired"/>.
/// </remarks>
public sealed class SignalRBpmnDocumentChangeSubscriber : ReconnectingBpmnDocumentChangeSubscriber
{
    private readonly Uri _hubUrl;
    private readonly Action<HttpConnectionOptions>? _configureHttp;

    /// <summary>Creates a subscriber.</summary>
    /// <param name="hubUrl">The absolute address of the hub.</param>
    /// <param name="configureHttp">Sets the connection's credentials, transport and handler.</param>
    /// <param name="options">Backoff; the heartbeat check is always off, because SignalR has its own keepalive.</param>
    /// <param name="timeProvider">The clock; the system clock when null.</param>
    public SignalRBpmnDocumentChangeSubscriber(
        Uri hubUrl,
        Action<HttpConnectionOptions>? configureHttp = null,
        BpmnChangeFeedOptions? options = null,
        TimeProvider? timeProvider = null)
        : base(WithoutStaleness(options), timeProvider)
    {
        ArgumentNullException.ThrowIfNull(hubUrl);

        _hubUrl = hubUrl;
        _configureHttp = configureHttp;
    }

    /// <inheritdoc />
    protected override async ValueTask<IBpmnChangeConnection> OpenStreamAsync(BpmnDocumentKey key, CancellationToken cancellationToken)
    {
        var channel = Channel.CreateUnbounded<string>();
        var hub = new HubConnectionBuilder()
            .WithUrl(_hubUrl, http => _configureHttp?.Invoke(http))
            .Build();

        hub.On<string>("Changed", json => channel.Writer.TryWrite(json));
        hub.Closed += exception =>
        {
            channel.Writer.TryComplete(exception ?? new IOException("The change feed connection closed."));
            return Task.CompletedTask;
        };

        try
        {
            await hub.StartAsync(cancellationToken).ConfigureAwait(false);
            var answer = await hub.InvokeAsync<string>("Subscribe", key.Path.ToString(), cancellationToken).ConfigureAwait(false);

            return answer switch
            {
                "ok" => new Connection(hub, channel, key),
                "forbidden" => throw new BpmnChangeFeedUnauthorizedException("The change feed does not know this caller's tenant."),
                _ => throw new InvalidOperationException($"The change feed did not accept the subscription ({answer})."),
            };
        }
        catch (HttpRequestException exception) when (exception.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            await hub.DisposeAsync().ConfigureAwait(false);
            throw new BpmnChangeFeedUnauthorizedException("The change feed refused the caller.", exception);
        }
        catch
        {
            await hub.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static BpmnChangeFeedOptions WithoutStaleness(BpmnChangeFeedOptions? options)
    {
        var copy = options ?? new BpmnChangeFeedOptions();
        copy.StalenessFactor = double.PositiveInfinity;
        return copy;
    }

    private sealed class Connection(HubConnection hub, Channel<string> channel, BpmnDocumentKey key) : IBpmnChangeConnection
    {
        public async ValueTask<BpmnChangeMessage?> ReadAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                string json;
                try
                {
                    json = await channel.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (ChannelClosedException exception) when (exception.InnerException is not null)
                {
                    // The connection closed with an error: the same gap as any other, and the subscriber reconnects.
                    throw exception.InnerException;
                }
                catch (ChannelClosedException)
                {
                    return null;
                }

                if (BpmnDocumentChangeWire.Parse(json)?.ToChange(key) is { } change) return new BpmnChangeMessage(change);
            }
        }

        public async ValueTask DisposeAsync()
        {
            channel.Writer.TryComplete();
            await hub.DisposeAsync().ConfigureAwait(false);
        }
    }
}
