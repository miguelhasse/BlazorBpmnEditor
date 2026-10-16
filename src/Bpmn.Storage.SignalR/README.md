# Hasseware.AspNetCore.Components.Bpmn.Storage.SignalR (preview)

Tells editors that the document they have open was changed by someone else, over SignalR: an authenticated hub, one subscription
per document. The client for Blazor WebAssembly and other .NET callers is `Hasseware.AspNetCore.Components.Bpmn.Storage.SignalR.Client`.

```csharp
builder.Services.AddBpmnChangeHub(options => options.TenantClaimType = "tid");   // also registers the change bus if there is none
// …
app.UseAuthentication();
app.UseAuthorization();
app.MapBpmnDocumentChangeHub("/bpmn/changes");
```

- **Authenticated, and the tenant is the caller's.** The tenant is the `TenantClaimType` claim (or `SingleTenant` for one shared space; you must
  choose, or mapping throws). `Subscribe(path)` answers `ok`, `notfound` (not a document, or not readable by the caller: the same answer),
  `toomany` or `forbidden`.
- **Bounded.** `MaxSubscriptionsPerCaller` (16) and `MaxSubscriptions` (10,000); a client that falls behind is given the latest change only;
  a connection ends after `MaxStreamLifetime` (30 minutes) so it never outlives the credentials it was opened with, and the client reconnects.
- **Keepalive is SignalR's own** (its server and client defaults); the client subscriber maps SignalR's disconnects onto
  `Live`/`Reconnecting`/`Unauthorized` and tells the binding after every gap. Tune `KeepAliveInterval` and `ClientTimeoutInterval` through `AddSignalR`.
- **More than one server instance:** each connection follows the change bus of the instance it is on, so a multi-instance host needs a bus
  that spans instances. (A group send through SignalR's backplane would deliver every change once per instance, so the hub does not use groups.)
- The message is the change as JSON text with `{ path, kind, revision, origin, occurredAt }`: never content, never a tenant.
