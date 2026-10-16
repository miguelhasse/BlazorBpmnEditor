# Hasseware.AspNetCore.Components.Bpmn.Storage.SignalR.Client (preview)

The client of the [BPMN SignalR change feed](../Bpmn.Storage.SignalR/README.md), for Blazor WebAssembly and any .NET client. It is the
`IBpmnDocumentChangeSubscriber` that a `BpmnDocumentBinding` uses to learn that someone else changed the document that is open.

```csharp
builder.Services.AddSingleton<IBpmnDocumentChangeSubscriber>(provider =>
    new SignalRBpmnDocumentChangeSubscriber(new Uri(provider.GetRequiredService<NavigationManager>().ToAbsoluteUri("/bpmn/changes").ToString())));
```

- One hub connection per subscribed document. SignalR's own keepalive finds a dead connection; when it drops, the subscriber reconnects
  with backoff and jitter and raises `Resubscribed`, so the binding makes one catch-up check. `State` is `Live`, `Reconnecting`, `Unauthorized` or `Closed`.
- A 401 or 403 stops the retrying (`Unauthorized`) and raises `AuthorizationRequired`; renew the credentials, then call `RetryAsync()`.
- Credentials: `configureHttp` receives the connection's options, for an access token provider or headers.
- Nothing is replayed after a gap: the binding compares the stored revision with its own.
