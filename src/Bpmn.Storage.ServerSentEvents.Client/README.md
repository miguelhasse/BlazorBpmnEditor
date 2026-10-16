# Hasseware.AspNetCore.Components.Bpmn.Storage.ServerSentEvents.Client (preview)

The client of the [BPMN Server-Sent Events change feed](../Bpmn.Storage.ServerSentEvents/README.md), for Blazor WebAssembly and any
.NET client. It is the `IBpmnDocumentChangeSubscriber` that a `BpmnDocumentBinding` uses to learn that someone else changed the document
that is open.

```csharp
builder.Services.AddSingleton<IBpmnDocumentChangeSubscriber>(provider =>
    new SseBpmnDocumentChangeSubscriber(provider.GetRequiredService<HttpClient>()));
```

- One stream per subscribed document. A connection that stays silent for 2.5 heartbeat intervals (75 s by default) is given up on, and
  reconnection backs off with jitter up to 30 s. After every gap the subscriber raises `Resubscribed`, and the binding makes one catch-up check.
- `State` is `Live`, `Reconnecting`, `Unauthorized` or `Closed`; the editor kits show a quiet "live updates paused" indicator when it is not `Live`.
- A 401 or 403 stops the retrying (`Unauthorized`) and raises `AuthorizationRequired`; renew the credentials, then call `RetryAsync()`.
- The `HttpClient` carries the credentials: a cookie in the same origin, or a bearer token its handler adds. If you set
  `BpmnChangeFeedOptions.HeartbeatInterval` on the server, set the same on the client.
- Nothing is replayed after a gap: the binding compares the stored revision with its own, which is right whatever was missed.
