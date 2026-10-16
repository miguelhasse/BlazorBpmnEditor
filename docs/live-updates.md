# Live updates: telling an editor that someone else changed its document (preview)

An editor that has a stored document open should hear when another party saves or deletes it (an agent through the MCP server, a
colleague in another tab, a job). `BpmnDocumentBinding` already reacts: it shows `ExternalChange`, and the editor kits offer **Reload**,
**Keep mine and save as a copy** or **Dismiss**. This page is about how the news arrives. The design is in
[webmcp-design.md](webmcp-design.md), "External change notification"; storage itself is in [document-storage.md](document-storage.md).

There is **no polling**. News is pushed, and the binding's save-time revision check stays the authority: a lost notification can delay a warning but can
never cause an overwrite.

## Pick a transport

| Where the editor runs | Use | Packages |
|---|---|---|
| Blazor Server, one instance | Nothing on the wire: the circuit's binding subscribes straight to the in-process bus | Core: `AddBpmnDocumentChanges()` |
| Blazor WebAssembly, or any remote .NET client | Server-Sent Events: one stream per document, plain HTTP, easy behind proxies | `…Storage.ServerSentEvents` (server) + `…Storage.ServerSentEvents.Client` |
| The same, where SignalR is already in use | A hub with one subscription per document | `…Storage.SignalR` (server) + `…Storage.SignalR.Client` |

The client packages run in WebAssembly and need no ASP.NET Core; the server packages need it. A host-supplied transport implements
`IBpmnDocumentChangeBus` and/or `IBpmnDocumentChangeSubscriber` and registers them.

```csharp
// Server
builder.Services.AddBpmnInMemoryDocumentStore();                  // or any store: it publishes to the bus after each successful write
builder.Services.AddBpmnChangeEvents(options => options.TenantClaimType = "tid");
// …
app.UseAuthentication();
app.UseAuthorization();
app.MapBpmnDocumentChangeEvents("/bpmn/changes");

// Blazor WebAssembly client
builder.Services.AddSingleton<IBpmnDocumentChangeSubscriber>(provider => new SseBpmnDocumentChangeSubscriber(provider.GetRequiredService<HttpClient>()));
```

(SignalR: `AddBpmnChangeHub` / `MapBpmnDocumentChangeHub` on the server, `new SignalRBpmnDocumentChangeSubscriber(hubUri)` on the client.)

## What the server guarantees

- **Authenticated, and the tenant is the caller's.** The tenant is the `TenantClaimType` claim, or `SingleTenant` when everyone shares a space; a host must
  choose one or mapping throws, and a caller without the claim is refused. No message can name a tenant.
- **Only what the caller may read.** The path is parsed and `IBpmnDocumentAuthorizer` is asked; a path that is not a document and one the caller may not
  read are answered identically, so a caller cannot probe for what exists.
- **One document per subscription**, never a folder or "everything". Per-caller (16) and per-host (10,000) limits; a client that falls behind gets the latest
  change only.
- **A subscription never outlives its credentials.** A stream or connection ends after `MaxStreamLifetime` (30 minutes), and the client reconnects and is
  authenticated again. The wire message is `{ path, kind, revision, origin, occurredAt }`: no content and no tenant.
- **More than one server instance** needs a change bus that spans instances (Redis, a broker, a cloud pub/sub; an `IBpmnDocumentChangeBus`
  implementation): a stream or hub connection follows the bus of the instance it is on. SignalR's own backplane does not help, because the hub sends per
  connection rather than per group, to avoid delivering every change once per instance.

## What the client does, and what a long session needs

Both client subscribers are `ReconnectingBpmnDocumentChangeSubscriber`s. Sessions last hours and connections drop, silently or not, so:

- **Heartbeat and staleness (SSE).** The server sends a `ping` event every 30 seconds (`HeartbeatInterval`); the client gives up on a connection that is silent for
  2.5 intervals (75 s) and reconnects. Keep your proxy's idle timeout above the interval, and use the same interval on both sides. SignalR uses its own keepalive.
- **Reconnect with backoff and jitter**, from one second up to 30, including after a server restart.
- **After every gap the binding is told once (`Resubscribed`)** and makes a single state-based catch-up check: it compares the stored revision with its own. Nothing
  is replayed, because the stored revision says everything a replay could.
- **`State`** is `Live`, `Reconnecting`, `Unauthorized` or `Closed`; the kits show a quiet "live updates paused" indicator when it is not `Live`.
- **Expired credentials.** A 401 or 403 stops the retrying (`Unauthorized`) and raises `AuthorizationRequired`. Renew the credentials, then call `RetryAsync()`:
  the streams resume and the binding catches up.

## Testing a transport of your own

`Hasseware.AspNetCore.Components.Bpmn.Storage.Testing` has two abstract suites. `BpmnDocumentChangeSubscriberContractTests` checks delivery of one document's changes,
disposal, a dropped connection reported once, a burst coalescing to the latest revision, and refusal followed by resumption. `BpmnBindingLongSessionContractTests` checks that a
change made while the feed was down still reaches the binding after exactly one catch-up check. Implement `IBpmnChangeFeedHarness` for your transport; the SSE and SignalR
tests in this repository are the examples.

## In the playground

`/live-updates` is a WebAssembly page that subscribes to a document in the server's store over Server-Sent Events, shows what arrives, and counts reconnections. The playground
server ends each stream after a few seconds so that the reconnection is visible. A browser test drives it, which is how the streaming fetch and the SSE parser were
confirmed to work in WebAssembly. It is not an editor: a WebAssembly editor would also need a store that reaches the server, which a real host provides.

Writers that bypass the store can be announced to the same bus; see [storage-backends.md](storage-backends.md), "Writes that bypass the store".
