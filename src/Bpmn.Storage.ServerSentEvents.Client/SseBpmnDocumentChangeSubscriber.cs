using System.Net;
using System.Net.ServerSentEvents;

namespace Hasseware.AspNetCore.Components.Bpmn.Storage.ServerSentEvents.Client;

/// <summary>How the client reaches the change feed.</summary>
public sealed class SseChangeFeedOptions
{
    /// <summary>The feed's address, relative to the <see cref="HttpClient"/>'s base address or absolute. <c>/bpmn/changes</c> by default.</summary>
    public string Endpoint { get; set; } = "/bpmn/changes";
}

/// <summary>
/// Hears about changes other parties make to a document over Server-Sent Events: one stream per subscribed document,
/// reconnected with backoff, given up on when it goes silent, and reported after every gap so the binding makes its catch-up check.
/// </summary>
/// <remarks>
/// Works in Blazor WebAssembly (the response is streamed by the browser's fetch) and in any .NET client. The
/// <see cref="HttpClient"/> carries the caller's credentials, which is the host's business: a cookie in the same
/// origin, or a bearer token its handler adds. A 401 or 403 stops the retrying and raises
/// <see cref="ReconnectingBpmnDocumentChangeSubscriber.AuthorizationRequired"/>.
/// </remarks>
public sealed class SseBpmnDocumentChangeSubscriber : ReconnectingBpmnDocumentChangeSubscriber
{
    private readonly HttpClient _http;
    private readonly SseChangeFeedOptions _endpoint;

    /// <summary>Creates a subscriber.</summary>
    /// <param name="http">The client that reaches the host; it is not disposed with the subscriber.</param>
    /// <param name="endpoint">Where the feed is; the defaults when null.</param>
    /// <param name="options">The heartbeat the server uses, staleness and backoff; the defaults when null.</param>
    /// <param name="timeProvider">The clock; the system clock when null.</param>
    public SseBpmnDocumentChangeSubscriber(HttpClient http, SseChangeFeedOptions? endpoint = null, BpmnChangeFeedOptions? options = null, TimeProvider? timeProvider = null)
        : base(options, timeProvider)
    {
        ArgumentNullException.ThrowIfNull(http);

        _http = http;
        _endpoint = endpoint ?? new SseChangeFeedOptions();
    }

    /// <inheritdoc />
    protected override async ValueTask<IBpmnChangeConnection> OpenStreamAsync(BpmnDocumentKey key, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{_endpoint.Endpoint}?path={Uri.EscapeDataString(key.Path.ToString())}");
        request.Headers.Accept.ParseAdd("text/event-stream");

        // In the browser a response is buffered unless fetch is asked to stream it.
        request.Options.Set(new HttpRequestOptionsKey<bool>("WebAssemblyEnableStreamingResponse"), true);

        var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        try
        {
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                throw new BpmnChangeFeedUnauthorizedException($"The change feed answered {(int)response.StatusCode}.");
            }

            response.EnsureSuccessStatusCode();
            var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            return new Connection(response, stream, key);
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    private sealed class Connection : IBpmnChangeConnection
    {
        private readonly HttpResponseMessage _response;
        private readonly Stream _stream;
        private readonly IAsyncEnumerator<SseItem<string>> _items;
        private readonly BpmnDocumentKey _key;
        private CancellationTokenSource? _reading;

        public Connection(HttpResponseMessage response, Stream stream, BpmnDocumentKey key)
        {
            _response = response;
            _stream = stream;
            _key = key;
            _reading = new CancellationTokenSource();
            _items = SseParser.Create(stream, (_, bytes) => System.Text.Encoding.UTF8.GetString(bytes)).EnumerateAsync(_reading.Token).GetAsyncEnumerator(_reading.Token);
        }

        public async ValueTask<BpmnChangeMessage?> ReadAsync(CancellationToken cancellationToken)
        {
            // The parser's enumerator was started with one token; closing the stream is what makes a cancelled read return.
            using var registration = cancellationToken.Register(static state => ((Stream)state!).Dispose(), _stream);

            while (true)
            {
                bool more;
                try
                {
                    more = await _items.MoveNextAsync().ConfigureAwait(false);
                }
                catch (Exception exception) when (cancellationToken.IsCancellationRequested && exception is not OperationCanceledException)
                {
                    throw new OperationCanceledException(cancellationToken);
                }

                cancellationToken.ThrowIfCancellationRequested();
                if (!more) return null;

                var item = _items.Current;
                switch (item.EventType)
                {
                    case "change":
                        if (BpmnDocumentChangeWire.Parse(item.Data)?.ToChange(_key) is { } change) return new BpmnChangeMessage(change);

                        // Not a change for this document, or not well formed: nothing a binding could act on.
                        return BpmnChangeMessage.Heartbeat;

                    default:
                        // "ready" and "ping": the connection is alive.
                        return BpmnChangeMessage.Heartbeat;
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_reading is { } reading)
            {
                _reading = null;
                await reading.CancelAsync().ConfigureAwait(false);
                reading.Dispose();
            }

            await _stream.DisposeAsync().ConfigureAwait(false);
            _response.Dispose();
        }
    }
}
