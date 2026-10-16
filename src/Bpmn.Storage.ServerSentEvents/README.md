# Hasseware.AspNetCore.Components.Bpmn.Storage.ServerSentEvents (preview)

Tells editors that the document they have open was changed by someone else, over
[Server-Sent Events](https://html.spec.whatwg.org/multipage/server-sent-events.html): one authenticated stream per
document, a heartbeat every 30 seconds, no JavaScript. The client for Blazor WebAssembly and other .NET callers is
`Hasseware.AspNetCore.Components.Bpmn.Storage.ServerSentEvents.Client`.

```csharp
builder.Services.AddBpmnDocumentChanges();                        // the bus the store publishes to
builder.Services.AddBpmnChangeEvents(options => options.TenantClaimType = "tid");
// …
app.UseAuthentication();
app.UseAuthorization();
app.MapBpmnDocumentChangeEvents("/bpmn/changes");                 // GET /bpmn/changes?path=claims/intake.bpmn
```

- **Authenticated, and the tenant is the caller's.** The tenant is the `TenantClaimType` claim; a caller without it is refused. Set
  `SingleTenant` instead when everyone shares one space. You must choose, or mapping throws. A client can never name a tenant.
- **Per document, and only what the caller may read.** The path is parsed, then `IBpmnDocumentAuthorizer` is asked; a path
  that is not a document and one the caller may not read both answer 404.
- **Bounded.** `MaxSubscriptionsPerCaller` (16) and `MaxSubscriptions` (10,000) answer 429; a client that falls behind is given
  the latest change only; a stream ends after `MaxStreamLifetime` (30 minutes) so that it never outlives the credentials it was
  opened with, and the client reconnects.
- **Events.** `ready` once subscribed, `ping` every `HeartbeatInterval`, `change` with `{ path, kind, revision, origin, occurredAt }`. Never
  content, never a tenant. Heartbeats are real events, not comments, because the .NET parser does not surface comments.
- **Behind a proxy** keep the idle timeout above the heartbeat interval (30 s by default; lower it for a shorter timeout), and do not
  buffer responses (`X-Accel-Buffering: no` is set).
- **More than one server instance** needs a change bus that spans instances: the stream follows the bus of the instance it is on.
