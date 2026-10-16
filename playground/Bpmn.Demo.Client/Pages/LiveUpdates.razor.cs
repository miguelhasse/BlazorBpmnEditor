using Hasseware.AspNetCore.Components.Bpmn.Storage;
using Hasseware.AspNetCore.Components.Bpmn.Storage.ServerSentEvents.Client;

using Microsoft.AspNetCore.Components;

namespace Bpmn.Demo.Client.Pages;

/// <summary>
/// Shows the Server-Sent Events change feed working in the browser: it subscribes to a document in the server's store,
/// reports what arrives and how often the connection had to be reopened, and can make a change on the server.
/// </summary>
/// <remarks>
/// This is the proof that the SSE client (the streaming fetch and the parser) runs in WebAssembly. The page is not an
/// editor: a WebAssembly editor would also need a store that reaches the server, which a real host supplies.
/// </remarks>
public sealed partial class LiveUpdates : ComponentBase, IAsyncDisposable
{
    private readonly List<string> _events = [];
    private SseBpmnDocumentChangeSubscriber? _subscriber;
    private IAsyncDisposable? _subscription;
    private string _path = "live/demo.bpmn";
    private bool _watching;
    private string _state = nameof(BpmnChangeFeedState.Closed);
    private int _gaps;

    [Inject]
    private HttpClient Http { get; set; } = default!;

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_subscription is not null) await _subscription.DisposeAsync().ConfigureAwait(false);
        if (_subscriber is not null) await _subscriber.DisposeAsync().ConfigureAwait(false);
    }

    private async Task WatchAsync()
    {
        if (!BpmnDocumentPath.TryParse(_path, out var path) || !path.IsDocumentName) return;

        _watching = true;
        _subscriber = new SseBpmnDocumentChangeSubscriber(
            Http,
            options: new BpmnChangeFeedOptions
            {
                HeartbeatInterval = TimeSpan.FromSeconds(2),
                InitialBackoff = TimeSpan.FromMilliseconds(200),
                MaxBackoff = TimeSpan.FromSeconds(1),
            });
        _subscriber.StateChanged += async state =>
        {
            _state = state.ToString();
            await InvokeAsync(StateHasChanged).ConfigureAwait(true);
        };
        _subscriber.Resubscribed += async () =>
        {
            _gaps++;
            await InvokeAsync(StateHasChanged).ConfigureAwait(true);
        };

        _subscription = await _subscriber.SubscribeAsync(new BpmnDocumentKey(null, path), async change =>
        {
            _events.Add($"{change.Kind.ToString().ToLowerInvariant()} {change.Revision}");
            await InvokeAsync(StateHasChanged).ConfigureAwait(true);
        }).ConfigureAwait(true);
        _state = _subscriber.State.ToString();
    }

    private async Task ChangeAsync() =>
        await Http.PostAsync($"/demo/live/touch?path={Uri.EscapeDataString(_path)}", content: null).ConfigureAwait(true);
}
