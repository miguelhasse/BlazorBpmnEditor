# WebMCP module — design draft

Status: draft. Milestone 1 (Core storage, step 0a–0g below), milestone 2 (step 1: edit origin,
placement, agent confirmation), milestone 3 (`Bpmn.Tools`), milestone 4 (`Bpmn.WebMcp`, read-only), milestone 5 (editing, confirmation, persistence) and milestone 6 (file store, read-only MCP server, `bpmn-mcp`) milestone 7 (MCP writing tools, batch, diff, HTTP host) and milestone 8a (live-update transports) and milestone 8b (Azure Blob and EF Core stores, change ingress) are implemented; see
[document-storage.md](document-storage.md) for the shipped storage behaviour, [webmcp.md](webmcp.md) for what ships in the
browser, and "Milestone 2/3/4/5/6/7/8a/8b as built" below; [live-updates.md](live-updates.md) is what the transports ship; [mcp-server.md](mcp-server.md) is what the server ships. Milestone 9 (an AG-UI chat over the editor) is a proposal only, see "Proposed milestone 9". The rest is not implemented.
Based on the deep-research run of 2026-10-12.

## Goal

Let a browser agent drive the open diagram through WebMCP (`document.modelContext.registerTool`)
without Core or either UI kit knowing WebMCP exists. Opt-in per host, no-op where unsupported.

The module drives the document **held by the editor session**. It does not know where that document
came from or where it is stored: a file, a blob, a database row, or nothing at all (a demo). It
reaches storage only through the Core document store and binding described next, never through paths,
URLs or connection details supplied by an agent.

## Shared prerequisite: `IBpmnDocumentStore` in Core

Decision: the Blazor host's own load and save go through `IBpmnDocumentStore`, and the interface
lives in Core. WebMCP and the server-side MCP package are then two more consumers of one persistence
seam and one revision scheme, instead of each inventing its own. Core must stay free of cloud and
database dependencies.

### What goes in Core (`Hasseware.AspNetCore.Components.Bpmn.Storage`)

| Type | Purpose |
|---|---|
| `IBpmnDocumentStore` | `OpenAsync`, `StatAsync` (revision/size/modified without content), `SaveAsync(ifMatchRevision)`, `ListAsync` (hierarchical, paged), `DeleteAsync`. Signature as in the server-side section below |
| `BpmnDocumentPath` | Validated, normalized, **hierarchical** document id: `/`-separated segments such as `claims/2026/intake.bpmn`. One parser in Core for every store (see "Hierarchical ids") |
| `BpmnDocumentKey`, `BpmnStoredDocument`, `BpmnDocumentContent`, `BpmnDocumentFormat`, `BpmnSaveResult`, `BpmnDocumentQuery`, `BpmnDocumentPage`, `BpmnDocumentEntry` | Plain records. Bytes + format, opaque revision string, no backend types |
| `BpmnDocumentChange`, `IBpmnDocumentChangeBus`, `IBpmnDocumentChangeSubscriber`, `BpmnDocumentChangeFilter` | The server-to-client change-notification abstraction: a server-side bus and a client-side subscriber, with transports (SignalR, SSE, in-process, brokers) in optional packages. No polling (see "External change notification") |
| `InProcessBpmnDocumentChangeBus`, `BusBpmnDocumentChangeSubscriber`, `NotifyingBpmnDocumentStore` | BCL-only defaults: single-instance bus, direct subscriber for Blazor Server, and the store decorator that publishes after successful writes |
| `InMemoryBpmnDocumentStore` | BCL-only implementation for tests, demos and the playground; atomic compare-and-write under a lock |
| `BpmnDocumentStoreExtensions` | `LoadAsync(key)` → parsed `BpmnDocument` + import diagnostics (via `BpmnFileFormats.Read`); `SaveAsync(key, document, ifMatch, format)` (via `BpmnXmlWriter` / `BpmnJsonWriter`). Parsing and writing stay out of the stores |
| `BpmnDocumentBinding`, `IBpmnDocumentBindingFactory` | A **per-editor object**, created by the host through a scoped factory with `Create()` and passed to the scope's `DocumentBinding` parameter (not a DI-scoped singleton: a scope is a circuit, and one page can hold several editors; and not created from the session, which only exists once the scope has initialized). The scope attaches and detaches it; the host disposes it. Ties one editor session to one stored document: `Key`, `StoredRevision`, `IsDirty`, `ExternalChange`, `LoadAsync(key)`, `ReloadAsync()`, `CheckForChangesAsync()`, `SaveAsync()` (checks the stored revision first), `SaveAsAsync(newKey)`, `FeedState` (live/reconnecting/unauthorized/closed), `Changed` event, conflict reported as a result, never an exception. Subscribes to external changes while bound; disposes the subscription with the session |
| `AddBpmnDocumentStore<TStore>()` / `AddBpmnInMemoryDocumentStore()` | DI registration. `AddBpmnComponents()` registers no store by default, so hosts that never persist pay nothing |

### What stays out of Core

- Anything that references Azure, AWS, EF Core, ADO.NET providers, or `HttpClient` conventions.
  Those are separate optional packages (`Bpmn.Storage.AzureBlob`, `Bpmn.Storage.EfCore`, and so on,
  as listed in the server-side section).
- The **file system store.** Core targets the browser (`SupportedPlatform Include="browser"`) and
  `System.IO` file access is meaningless there, so `FileBpmnDocumentStore` lives in a small
  `Bpmn.Storage.FileSystem` package, referenced by the Blazor Server host that wants it and by the
  `bpmn-mcp` tool.
- Auth and authorization policy. `BpmnDocumentKey.Tenant` is data; filling it from an authenticated
  principal is the host's or the MCP layer's job. An optional `IBpmnDocumentAuthorizer` lives in
  `Bpmn.Mcp` until the editor needs it too.

### Keeping Core clean (enforced, not hoped for)

- Add an architecture test in `tests/Bpmn.Tests`: Core's referenced assemblies and its `.csproj`
  package references must not include `Azure.*`, `AWSSDK.*`, `Microsoft.EntityFrameworkCore*`,
  `Microsoft.Data.*`, `System.Data.*`, `Npgsql*`, `Microsoft.Extensions.Http*`, `Microsoft.AspNetCore.SignalR*`, `StackExchange.Redis*`, broker clients and similar. Transports for change notification (SignalR, SSE, brokers) are therefore always separate packages. The test
  fails the build on a violation.
- The store types use only `System`, `System.Collections.Generic`, `System.Threading` and
  `System.Text.Json`-free records, so Core's dependency list (`Components.Web`, `Logging`,
  `Localization`) does not grow.
- Core follows its existing conventions (nullable, implicit usings, XLIFF/resx for any user-visible
  strings, code-behind for components). `BpmnDocumentBinding` is a plain class, not a component.

### Blazor host integration

The playground and the kits stay storage-agnostic. A host:

```csharp
builder.Services.AddBpmnEditor();
builder.Services.AddBpmnDocumentStore<MyBlobStore>();      // or AddBpmnInMemoryDocumentStore()
```

```razor
<BpmnEditorScope DocumentBinding="@binding" ...>   @* instead of Document; setting both is an error *@
    ...
</BpmnEditorScope>

@* code-behind *@
binding = Bindings.Create();                                 // IBpmnDocumentBindingFactory
await binding.LoadAsync(new BpmnDocumentKey(null, BpmnDocumentPath.Parse("claims/2026/intake.bpmn")));
await binding.SaveAsync();                                   // writes with the held revision; conflict => result
```

- On Blazor Server the store implementation talks to storage directly. On WebAssembly it is a thin
  client of the host's own API (an `HttpClient`-backed store written by the host); the contract is
  identical, and the server behind that API can be the same `Bpmn.Mcp` store implementation.
- **Dirty tracking needs no change to the session or history** (checked against the code). Core's
  ownership model is already right: the **host owns `Document`** and passes it to `BpmnEditorScope`; the
  session edits it through `BpmnEditHistory` and publishes results through `OnDocumentChanged`,
  remembering what it published so a document handed back is not mistaken for a new file. The binding
  therefore holds the `SavedDocument` it last loaded or saved, and
  `IsDirty = !ReferenceEquals(session.EditedDocument, SavedDocument)`. Undoing back to the saved snapshot
  returns the same reference and is clean again; `session.StateChanged` drives re-evaluation.
- **Loading** is the host's existing action: the binding exposes `Document` and `Changed`; the host binds
  `Document="@binding.Document"` on the scope. A document that is not the one the session last published
  is a different file to the session, so history resets, which is exactly the wanted behaviour for
  `LoadAsync` and `ReloadAsync`. The binding never reaches into the session to replace a document.
- **Saving** writes `session.EditedDocument` through the store, then records the returned revision and
  sets `SavedDocument` to that same reference.
- A **revision conflict** during `SaveAsync` leaves the editor state untouched and reports the
  stored revision. The host decides: reload, save as a copy (`SaveAsAsync`), or overwrite after the
  user confirms. Core never overwrites on its own.

### Hierarchical ids

Decision: document ids are hierarchical paths, so listings can be browsed by folder and tenants or
teams can partition their documents. The hierarchy is part of the **Core contract**, because
changing it later would be a breaking Core API change.

**`BpmnDocumentPath`** (a `readonly record struct` with `Parse`, `TryParse`, `Parent`, `Name`,
`Combine`, `IsUnder(prefix)`, `ToString()`):

- `/`-separated segments, no leading or trailing slash, no empty segments. Backslash is rejected,
  not converted. Segments `.` and `..` are rejected, not resolved.
- Per segment: 1 to 128 characters; allowed set is a conservative portable subset (letters, digits,
  space, `- _ . ( )` and common Unicode letters, after Unicode NFC normalization). No control
  characters, no characters invalid in Windows file names or blob names (`: * ? " < > | \`), no
  trailing dot or space, no Windows device names (`CON`, `NUL`, ...). Whole path at most 512
  characters and 16 segments. Limits are options with these defaults.
- **Case:** paths compare **ordinal, case-sensitive** in the contract. Stores on case-insensitive
  media (the Windows file system, some SQL collations) must reject a save whose path differs from an
  existing one only by case, rather than silently overwrite or create a lookalike. The contract suite
  tests this.
- The extension rule is **not** part of path validity, because a folder is a path too
  (`BpmnDocumentQuery.Folder`). `BpmnDocumentPath.IsDocumentName` checks it, and `BpmnDocumentKey`
  enforces it: a key's last segment must end in `.bpmn`, `.xml` or `.json` (the format is inferred from
  it, as `BpmnFileFormats.Read` already does by extension or content).
- Every store, tool and binding goes through `BpmnDocumentPath`. There is no second parser, so
  traversal handling is implemented, fuzzed and reviewed in exactly one place; stores still apply
  their own backend-specific checks (file system full-path containment) as defense in depth.

**Folders are implicit.** A folder exists while at least one document lies beneath it, as with blob
prefixes. v1 has no create-folder or delete-folder operation. `SaveAsync` to `a/b/c.bpmn` creates
whatever intermediate structure the backend needs (directories, nothing for blobs, nothing for a
path column). `DeleteAsync` removes a document; empty directories are a file-store cleanup detail.

**Listing:**

```csharp
public sealed record BpmnDocumentQuery(
    BpmnDocumentPath? Folder = null,          // null = root of the tenant
    bool Recursive = false,
    string? NamePrefix = null,                // filters the last segment
    string? Cursor = null,
    int Limit = 100);                          // capped by options

public sealed record BpmnDocumentEntry(
    BpmnDocumentPath Path,                     // full path; for a folder, ends at the folder name
    bool IsFolder,
    long? Size,                                // documents only
    DateTimeOffset? Modified);                 // documents only

public sealed record BpmnDocumentPage(IReadOnlyList<BpmnDocumentEntry> Entries, string? NextCursor);
```

- Non-recursive listing returns the folder's documents plus **virtual sub-folder entries** (the
  blob "delimiter" model). Recursive listing returns only documents.
- Order is ordinal by path, folders before documents within a level; the cursor is an opaque token
  derived from the last returned path, so it stays valid when documents are added or removed between
  pages. Pages are bounded by `Limit`; stores never enumerate unbounded sets.
- Listing returns no content and no revisions (use `StatAsync`/`OpenAsync`); it must not load document bodies.

**Backend mapping:** file system: directories under `Root`; blob: `/` is the native delimiter, the
tenant prefix is prepended internally; database: a `Path` column (normalized) with an index; folder
listing uses a range query on the ordinal prefix, never `LIKE` on user input (a prefix range
`[prefix + "/", prefix + "0")` works because `'0'` follows `'/'` in ordinal order), and non-recursive
folder entries are produced by grouping on the next segment. Tenant is a separate column or key part.

**Out of v1:** move/rename (use `SaveAsAsync` then `DeleteAsync`; an atomic `MoveAsync` can be added
later as an optional member with a default implementation), folder metadata or permissions, and
wildcard search. Per-folder authorization is possible through `IBpmnDocumentAuthorizer` taking a path
prefix; the default stays tenant-scoped.

### External change notification

Decision: the editor learns when another party (for example an MCP client) changes the document it
has open, through **server-to-client push**. There is **no polling**. The push mechanism sits behind an
extensible abstraction, so SignalR, Server-Sent Events, or something else can be plugged in without
touching Core, the editor or the MCP package.

#### Three seams, each replaceable

```
writer (editor, MCP tool, host code)
   │ SaveAsync / DeleteAsync succeeded
   ▼
NotifyingBpmnDocumentStore  ──►  IBpmnDocumentChangeBus.PublishAsync(change)        (server side)
                                      │
            ┌─────────────────────────┼───────────────────────────┐
            ▼                         ▼                           ▼
   in-process subscriber      SignalR hub adapter         SSE endpoint adapter       (wire adapters)
   (Blazor Server binding)           │                           │
                                     ▼                           ▼
                              SignalR client              SSE client                 (WASM / remote)
                                     └──────────┬────────────────┘
                                                ▼
                                  IBpmnDocumentChangeSubscriber  ──►  BpmnDocumentBinding
```

1. **`IBpmnDocumentChangeBus`** (server side, Core): where changes are published and where server-side
   consumers subscribe. Default `InProcessBpmnDocumentChangeBus` (BCL only). Replace it with a
   backplane-backed bus (Redis, a message broker, a cloud pub/sub) for multi-instance servers or for an
   MCP process that is separate from the Blazor host.
2. **Wire adapters** (server side, optional packages): thin ASP.NET Core components that subscribe to the
   bus and forward changes to connected clients: a SignalR hub, an SSE endpoint. They own connection
   authentication, authorization and fan-out. They contain no storage logic.
3. **`IBpmnDocumentChangeSubscriber`** (client side, Core): what the binding uses. One implementation per
   environment, chosen by the host.

```csharp
// Core, namespace ...Bpmn.Storage. BCL only.
public sealed record BpmnDocumentChange(
    BpmnDocumentKey Key,                       // includes tenant
    BpmnDocumentChangeKind Kind,               // Updated | Deleted
    string? Revision,                          // opaque; null when Deleted
    string? Origin,                            // writer's instance/client id, for echo suppression; may be null
    DateTimeOffset OccurredAt);

public interface IBpmnDocumentChangeBus
{
    ValueTask PublishAsync(BpmnDocumentChange change, CancellationToken ct);
    // Server-side subscription to ONE document, used by wire adapters and by the in-process
    // subscriber. There is no folder or wildcard subscription.
    ValueTask<IAsyncDisposable> SubscribeAsync(BpmnDocumentKey key,
        Func<BpmnDocumentChange, ValueTask> handler, CancellationToken ct);
}

public interface IBpmnDocumentChangeSubscriber
{
    // What the binding calls. The handler gets key + revision (or Deleted), never content.
    ValueTask<IAsyncDisposable> SubscribeAsync(BpmnDocumentKey key,
        Func<BpmnDocumentChange, ValueTask> handler, CancellationToken ct);

    // Live/Reconnecting/Unauthorized/Closed. The editor shows "live updates paused" when not Live.
    BpmnChangeFeedState State { get; }
    event Func<BpmnChangeFeedState, ValueTask>? StateChanged;

    // Raised after ANY gap in delivery (reconnect, detected stale connection, bus restart, token
    // renewal). The binding reacts with ONE state-based catch-up check, not a loop.
    event Func<ValueTask>? Resubscribed;
}

public enum BpmnChangeFeedState { Live, Reconnecting, Unauthorized, Closed }
```

Subscriptions are **per document only**: a binding is bound to one document, so folder and wildcard
subscriptions are not part of the contract (decided). That keeps authorization, fan-out and limits
simple, and a future document browser can be added as a separate capability without changing these
interfaces.

`AddBpmnDocumentStore<TStore>()` wraps the store in `NotifyingBpmnDocumentStore` whenever a bus is
registered. That is decided when the store is **resolved**, not when it is registered, so calling
`AddBpmnDocumentChanges()` before or after the store makes no difference, and the wrapper has the
store's own lifetime (a scoped store gets a scoped wrapper). The decorator publishes **after** a successful write or delete, never before, and never
for a rejected conflict. A host that registers no bus gets the plain store and no notifications.

#### Transports

| Package | Role | Notes |
|---|---|---|
| Core | `InProcessBpmnDocumentChangeBus`, `BusBpmnDocumentChangeSubscriber` | Blazor Server on one instance: the circuit's binding subscribes straight to the bus. No wire at all. Also the test and playground default |
| `Bpmn.Storage.SignalR` | Hub adapter (server) + `SignalRBpmnDocumentChangeSubscriber` (client) | Maps `MapBpmnDocumentChangeHub("/bpmn/changes")`. Groups per tenant and path. Scale-out through SignalR's own backplane (Redis, Azure SignalR) |
| `Bpmn.Storage.ServerSentEvents` | Endpoint adapter (server) + `SseBpmnDocumentChangeSubscriber` (client) | `MapBpmnDocumentChangeEvents("/bpmn/changes")`, built on ASP.NET Core's SSE support in .NET 10 (**verify** the exact API). One-directional, simple behind proxies. Multi-instance needs a bus that spans instances, since SSE has no backplane of its own |
| `Bpmn.Storage.Bus.*` (later: Redis, service bus) | Replacement `IBpmnDocumentChangeBus` | For multi-instance servers and for a separate MCP process that must reach editors |
| host-supplied | Any other transport | Implement `IBpmnDocumentChangeBus` and/or `IBpmnDocumentChangeSubscriber` and register them |

Subscription requests from clients are small: "tell me about this document". The wire protocol is the
adapter's business; the contract between layers is only the three types above.

#### Writers outside any decorated store: options

A decorated store tells the bus about every write that goes through it: the editor, `Bpmn.Mcp` tools and
any host code using the registered store. It cannot know about writes that bypass it: someone uploads a
blob in a portal, an ETL job updates a table, another application writes the same container. Two ways to
handle that were compared.

| | **A. "Found at next save"** (no ingress) | **B. First-party ingress** (blob, EF Core) | **C. Extension point + reference samples** (**chosen for v1**) |
|---|---|---|---|
| What it is | Only decorated writes notify. Anything else is caught by the save-time revision check, plus cheap event-driven checks (below) | We ship ingress packages that watch the backend's change signal and publish to the bus | We ship `IBpmnDocumentChangeIngress` helpers and documented, tested reference ingresses in `samples/`, not supported packages |
| Coverage of non-decorated writers | None until save, focus or reconnect | Yes, where the backend offers a signal | Yes, for hosts that adopt a reference ingress |
| Latency to the user | Up to the next save or focus | Seconds, depending on backend | As B, for adopters |
| Infrastructure the host must run | None | Event subscription, queue or endpoint, firewall and identity setup, a hosted consumer | Same as B, but only for hosts that choose it |
| Provider portability | Complete | Poor: one ingress per backend and per database provider | Complete for the core; ingress is per host |
| Security surface | None added | New inbound surface (webhook validation, queue credentials) we would own and patch | Host owns it; we document the hardening |
| Maintenance on us | None | High: vendor APIs and event schemas change | Low: samples and one small helper |
| Failure mode | Late warning; never an overwrite | Missed or duplicated events (at-least-once, unordered); misconfiguration is silent | Same as B, documented |
| Testing | Covered by the store contract suite | Needs emulators (Azurite lacks Event Grid), cloud resources or fakes | Reference ingress tested with fakes; real wiring left to adopters |

**What "a first-party ingress" would mean per backend.** Facts below come from general knowledge of the
services and have **not** been verified in this research. Confirm each (**verify**) before committing.

- *Azure Blob* (verified against Microsoft Learn, 2026-10). Event Grid's `BlobCreated` fires when a blob
  is "created or replaced" (`PutBlob`, `PutBlockList`, `CopyBlob`), so an overwrite is signalled, and the
  event data carries the blob's `etag`, which maps directly to our revision. Events are pushed in real
  time but may be unordered and are delivered at least once, so the ingress publishes "changed, revision
  X" and the binding compares, as designed. The **blob change feed** carries the same information (also
  with `etag`) and orders changes per blob, but it appears "within a few minutes", must be enabled on
  the account, is not supported on accounts with a hierarchical namespace, and only logs requests to the
  Blob endpoint (not the Data Lake `dfs` endpoint). For an editor warning, Event Grid is the right
  signal; the change feed suits audit and replication. Needs: an Event Grid subscription, an endpoint
  or queue, an identity for the consumer, and in scaled-out hosts a shared bus (one webhook hits one
  instance). Azurite does not emulate Event Grid (**verify**; not checked), so tests need fakes.
- *EF Core / databases* (partly verified). EF Core has **no** change feed (not confirmed by a primary source; the existence of third-party packages that generate triggers from EF models is consistent with it). A `SaveChangesInterceptor` sees only writes
  made through that application's own contexts, which the decorator already covers, so it adds nothing for
  external writers. External-writer signals are database-specific: PostgreSQL `LISTEN`/`NOTIFY` from a
  trigger is real push (**verify**; not confirmed in this research); SQL Server offers Change Tracking and Change Data Capture (Microsoft documents both as ways for an application to *query* what changed, i.e. pull-based, which
  conflicts with the no-polling decision) and Service Broker or query-notification based push (heavier,
  with operational caveats); MySQL and SQLite have no practical push. A generic "EF Core ingress" does
  not exist; it would be one package per provider.
- *File system.* `FileSystemWatcher` is cheap and in-process; useful in development and for single-host
  file stores. Not a cross-machine signal.

**Mitigations that make A better without any ingress** (all event-driven, none are polling):

1. The binding makes **one** `StatAsync` check right after subscribing, after each `Resubscribed`, and
   immediately before `SaveAsync` (so the user hears about a conflict before the write, not as a failed
   write).
2. `Binding.CheckForChangesAsync()` is public and **left to the host** (decided). A host that has the
   browser's focus or visibility signal calls it when the tab regains focus. Core, the kits and
   `BpmnEditorScope` add no focus or visibility handling and no JS for this, which keeps
   `BpmnEditorSession` free of `IJSRuntime`. The docs show a three-line example for hosts.
3. Transport reconnects trigger the same check (see "Long editing sessions").

**Decision: C.** Ship A's behaviour plus its mitigations as the default, ship the ingress extension
point (`IBpmnDocumentChangeIngress` helper: backend name to `BpmnDocumentKey` mapping and a hosted-service
base class that calls `bus.PublishAsync`), and publish reference ingresses for blob (Event Grid) and
PostgreSQL (`LISTEN`/`NOTIFY`) as samples. Reasons:

- Most v1 deployments write through the decorated store (editor and MCP), so A already covers them.
- A first-party ingress commits us to vendor APIs, new inbound surfaces and per-provider code for a
  population of writers we cannot yet size.
- The extension point means a host with real external writers is not blocked and does not need a fork.

**Promote C to B when** feedback shows external writers are routine (ETL jobs, a second application,
manual uploads), at which point the blob ingress is the likelier first package: its event signal is
uniform and already carries the revision, whereas database signals differ per provider.

#### Delivery semantics

- **Best effort, at most once, unordered, per document.** Revisions are opaque, so the binding never
  orders them; any notification whose revision differs from the binding's own is treated as "changed since
  I loaded it". Duplicates are harmless (same revision, no new state).
- **Lost messages are covered by the revision check.** A save always compares revisions, so a missed
  notification can delay the warning but can never cause an overwrite.
- **Catch-up is state-based, not event-based, and not polling.** After the first subscription, after any
  `Resubscribed`, and before a save, the binding makes one `StatAsync` call and compares the revision.
  No timers, no intervals.
- Notifications carry only key, kind, revision, origin and time. Never content.

#### Long editing sessions: do we need a replayable stream?

Editing sessions can last hours. Connections drop (proxy idle timeouts, a laptop sleeping, a deployment
restart, a token expiring), and some drops are silent (a half-open connection delivers nothing and raises
no error). The question is whether a dropped client needs to receive the events it missed, in order.

**It does not, because the subscription is per document and the binding only needs one fact: "is the
stored revision still mine?"** A replayed log of every intermediate change would tell the editor nothing
more than the current revision does, and the revision is available at any time from `StatAsync`. So the
design uses a **state-based catch-up** and keeps event replay as an optional transport optimisation.

| Option | How it works | Fit |
|---|---|---|
| **1. State-based catch-up** (chosen) | After any gap the binding compares the stored revision with its own, with one `StatAsync` | Works with every transport and backend, across instances, with no buffering. Loses history of intermediate changes, which nothing needs |
| 2. Transport-native resume | SignalR stateful reconnect (buffers messages for a short outage); SSE `id:` plus `Last-Event-ID` with a bounded per-instance ring buffer | Free efficiency for short drops. Buffers are per instance and per connection, so a reconnect to a different instance has nothing to replay. Never the only mechanism |
| 3. Per-document sequence log in the store | Every write appends a monotonic sequence number; clients ask "changes since N" | Needs store support and a new contract (the revision is opaque, not ordered), more storage and pruning. Only useful if we later show a change history in the UI |
| 4. Durable stream in a broker (streams, event log) | Consumers resume from an offset | Strongest guarantees, heaviest infrastructure. Overkill for "has my document changed" |

**Decision:** option 1 is the contract. Transports may add option 2 where it is free, but the binding
must be correct without it. Options 3 and 4 are out of scope; revisit 3 only if a change-history feature
is planned.

**What long sessions do require is reliable gap detection**, since a silent drop produces no event to
catch up from. Therefore the transport contract adds:

- **Keepalive** (defaults confirmed, see below). Every adapter sends a small heartbeat every 30 seconds;
  this is transport keepalive, not a storage poll and not a change signal. The client subscriber treats
  2.5 missed intervals (75 seconds) as a dead connection: state moves to `Reconnecting`, it resubscribes, and raises
  `Resubscribed` so the binding performs its single catch-up check.
- **Automatic reconnect with backoff and jitter** in each client subscriber, including after a server
  restart. The state is exposed (`Live`, `Reconnecting`, `Unauthorized`, `Closed`) and the kits show a quiet
  "live updates paused" indicator when it is not `Live`, so users know why a warning might be late.
- **Token and session expiry.** When authentication expires the adapter closes the stream with an
  authorization failure; the subscriber enters `Unauthorized` and raises an event the host can use to
  re-authenticate. After renewal it resubscribes and catches up. Subscriptions never outlive their
  principal.
- **Bus restarts and instance failover** count as gaps: the in-process and bus-backed subscribers raise
  `Resubscribed` when the bus reconnects.
- **Limits.** Each subscription's server-side state is O(1) (key, connection). Bursts for one document
  are coalesced to the latest revision within a short window, so a chatty writer cannot flood a client.

**Confirmed defaults and why.** 30 s heartbeat, dead after 2.5 missed intervals (75 s), all configurable:

- It sits below the idle timeouts commonly found on intermediaries (typically 60 s for AWS ALB and
  nginx `proxy_read_timeout`, 100 s on Cloudflare, about 4 minutes on Azure load balancers and
  Application Gateway), so a heartbeat every 30 s keeps an otherwise quiet stream from being cut by an
  idle timer. Hosts behind a shorter timeout lower the interval; the staleness factor scales with it.
- SignalR already ships its own keepalive (client and server defaults of 15 s and a 30 s client timeout).
  The SignalR adapter **uses SignalR's native settings** rather than a second heartbeat and maps its
  disconnect/reconnect events onto `State` and `Resubscribed`; the 30 s / 2.5x defaults apply to the SSE
  adapter and any host-supplied transport, which must emit a comment or ping line at that interval.
- 2.5 intervals tolerates one lost heartbeat plus jitter without flapping, while bounding the worst-case
  blind window after a silent drop to about 75 s. Gap detection only delays the *warning*; the save-time
  revision check remains authoritative, so a longer window never risks an overwrite.
- Heartbeats cost one tiny frame per connection per 30 s. At 10,000 concurrent editors that is about
  330 frames per second server-wide, which is negligible next to the editing traffic itself.

**Tests for long sessions:** a fake transport that drops messages silently, one that reconnects to a
different "instance", an expired token mid-session, a burst of 1,000 writes to one document, and a stalled
connection that never errors. Each ends with the binding showing the correct `ExternalChange` after exactly
one catch-up call.

#### Behaviour in the binding

All decisions here are the user's; Core never replaces the editor's document by itself unless the policy
says so.

- Ignore a change whose `Revision` equals the binding's own `StoredRevision` (the editor's own save echoing
  back). `Origin` is an extra hint, not the rule.
- **The editor's own save must be held back explicitly.** The decorator publishes before the store's
  `SaveAsync` returns, and the in-process bus delivers at once, so the notification arrives before the
  binding has recorded the new revision. While its own save runs, the binding holds incoming
  notifications for that document and replays them once the new revision is known, dropping the one
  that matches it.
- Otherwise set `Binding.ExternalChange` to `{ Kind, Revision, DetectedAt }` and raise `Changed`.
- Policy option `ExternalChangePolicy`:
  - `Notify` (default): show the offer, change nothing.
  - `AutoReloadWhenClean`: if `IsDirty` is false, reload the new revision silently (with a visible
    "reloaded" status and a cleared undo history); if dirty, behave as `Notify`.
  - `Ignore`: only the next save reports the conflict.
- The offer is a kit-implemented part, `BpmnPart.ExternalChangeOffer` (like `ImportOffer`), with **Reload**
  (confirmation first when dirty), **Keep mine and save as a copy**, and **Dismiss**. A `Deleted` change
  offers **Save as new** and **Close**.
- While a change is pending, a plain `SaveAsync()` still goes through the revision check and fails with the
  conflict; no code path overwrites without an explicit user choice.
- Reload uses `OpenAsync` through the store's normal authorization, so a notification can never deliver
  content the user could not open.
- The binding subscribes when it binds a document and disposes the subscription when it unbinds, loads
  another document, or the session ends (circuit disconnect, tab close).

#### Security and cost

- **Authentication and authorization are the wire adapter's job.** It authenticates the connection, takes
  the tenant from the principal (never from a client message) and checks read permission on the requested
  path through the store or `IBpmnDocumentAuthorizer` before subscribing. A user cannot observe documents
  they cannot open, nor learn that a forbidden path exists.
- **Bounded:** per-connection and per-host caps on subscriptions, a maximum message size, and
  rate-limited fan-out for write bursts (coalesce to the latest revision per key within a short window).
- Subscriptions are scoped to exactly one document, never a folder, a tenant or "everything".
- Disposal with the session is mandatory; the adapter drops state when a connection closes.
- On Blazor WebAssembly, the SignalR or SSE endpoint is the same-origin host API, protected like the rest
  of the host's API.

#### Relationship to the MCP server

The server-side MCP package is stateless and does not push to its own MCP clients; MCP-side resource
subscriptions would need stateful mode and remain out of scope. It is the *source* of changes that editors
observe: its saves go through the same `NotifyingBpmnDocumentStore`. When it runs in a **separate process**
(stdio tool, CI) it only reaches live editors if both processes share a bus package or the backend has an
ingress; otherwise editors find out at their next save. That is the correct and documented default for
CI use, where no editor is listening.

## Why a separate package

- The spec is a Community Group draft; Chrome's origin trial (149-156) ends 2026-11-17 and the API
  changed in Chrome 153, 155 and 156. Churn must not reach Core's versioning.
- The repo already ships optional modules the same way: `Bpmn.Scripting.{Lua,JavaScript,CSharp}`
  (`AddBpmnLuaScripting`) and `Bpmn.Feel` (`AddBpmnFeel...`, with a paired `.Scripts` esproj).
  WebMCP follows the Feel shape: a Razor class library plus a small TypeScript bundle.
- Core's `BpmnEditorSession` deliberately holds no `IJSRuntime`. The module is therefore a
  *consumer* of the session, never part of it.

## Projects

| Project | Role |
|---|---|
| `src/Bpmn.WebMcp` → `Hasseware.AspNetCore.Components.Bpmn.WebMcp` | RCL: tool catalog, host component, DI extension, interop contract |
| `src/Bpmn.WebMcp.Scripts` → `...Bpmn.WebMcp.Scripts.esproj` | TS source for the ES module; esbuild output feeds the RCL's static web assets (copy `Bpmn.Feel.Scripts` config) |
| `tests/Bpmn.WebMcp.Tests` | Tool handlers against a headless session (no browser) |
| `tests/Bpmn.E2E.Tests` (extend) | One test with a polyfilled `document.modelContext` |

Dependencies: `Bpmn.WebMcp` → `Bpmn` and `Bpmn.Tools` only. It never references a UI kit, so it works with the
standard and Fluent kits and with custom layouts. Add the project to `BlazorBpmnEditor.slnx`
under `/Components/`.

Core change: none required. If a tool needs something the session hides (for example a
`DiagramId`-targeted create that the stencil API does not expose), add it to Core as a normal
public API, not an `InternalsVisibleTo`.

## Public surface

```csharp
namespace Hasseware.AspNetCore.Components.Bpmn.WebMcp;

public static class BpmnWebMcpServiceCollectionExtensions
{
    public static IServiceCollection AddBpmnWebMcp(
        this IServiceCollection services,
        Action<BpmnWebMcpOptions>? configure = null);
}

public sealed class BpmnWebMcpOptions
{
    public bool AllowMutations { get; set; } = false;      // first preview: read-only tools only; flips to true once confirmation ships
    public bool RequireConfirmationForConsequential { get; set; } = true;
    public string ToolPrefix { get; set; } = "bpmn_";      // avoids clashes with other registrants
    public ISet<string> DisabledTools { get; } = new HashSet<string>();
    public int MaxElementsPerCall { get; set; } = 200;
    public int MaxXmlBytes { get; set; } = 1_000_000;
    // public bool AllowOpenDocument { get; set; }   // reserved: bpmn_open_document is not in v1
    public bool RequireConfirmationForPersistence { get; set; } = true;
}

// Extension seam: hosts add or replace tools.
public interface IBpmnWebMcpTool
{
    string Name { get; }
    string Description { get; }
    JsonElement InputSchema { get; }
    BpmnWebMcpAnnotations Annotations { get; }
    ValueTask<BpmnWebMcpResult> ExecuteAsync(
        BpmnEditorSession session, JsonElement arguments, CancellationToken cancellationToken);
}

public sealed record BpmnWebMcpAnnotations(
    bool ReadOnly, bool Consequential = false, bool UntrustedContent = false);

```

**No WebMCP-specific persistence interface.** The module takes Core's `BpmnDocumentBinding` (and
through it the host's `IBpmnDocumentStore`) from DI. Nothing to register beyond what the host already
registered for its own load and save:

```csharp
services.AddBpmnEditor();
services.AddBpmnDocumentStore<MyBlobStore>();
services.AddBpmnWebMcp();               // persistence tools appear because a binding and store exist
```

- If no `IBpmnDocumentStore` is registered, or the editor has no binding, the persistence tools are
  not registered at all and the agent sees only `bpmn_export` for getting content out.
- The module reads `BpmnDocumentBinding` (`Key`, `StoredRevision`, `IsDirty`) for
  `bpmn_get_document_info` and calls `Binding.SaveAsync()` for `bpmn_save`. It adds no storage logic.
- Because the store contract contains no paths, containers or SQL, one module serves every backend
  and runs on both Blazor Server and WebAssembly; on WASM the host's store is an `HttpClient` client
  of the host's API and the agent never sees that.
- The binding's data that reaches the agent is limited to a logical path, a display name (untrusted
  text), the opaque revision and the dirty flag.

Host usage, one component placed inside the scope (adds no DOM, like `BpmnEditorScope`):

```razor
<BpmnEditorScope ...>
    ...
    <BpmnWebMcp />
</BpmnEditorScope>
```

`BpmnWebMcp` (code-behind `.razor.cs`, per repo convention) does the following:

1. Takes the session through the scope's cascading value.
2. Calls `OnAfterRenderAsync(firstRender)`, imports the module and calls `isSupported()`.
3. If unsupported: sets `State = Unsupported`, raises `OnUnavailable`, registers nothing.
4. Otherwise creates a `DotNetObjectReference<BpmnWebMcpBridge>` and calls `registerAll(ref, descriptors)`.
5. In `DisposeAsync`: calls `unregisterAll()`, disposes the module and the reference.

Putting it in a component, not a hosted service, ties tool lifetime to the editor instance,
which matches WebMCP's tab-bound, ephemeral model and handles several editors on one page
(each gets its own session and its own tool instances).

### Multiple editors on one page

Tool names are global per document. The component takes `ToolPrefix` plus an optional
`InstanceName` parameter; with more than one `BpmnWebMcp` on a page, a missing `InstanceName`
logs a warning and suffixes `_2`, `_3`. Duplicate-name registration rejects with
`InvalidStateError`, so the bridge must treat that as a handled outcome.

## JS module (`Bpmn.WebMcp.Scripts/src/webmcp.ts`)

```ts
export function isSupported(): boolean {
  return !!(globalThis.document as any)?.modelContext ?? !!(globalThis.navigator as any)?.modelContext;
}
const ctx = () => (document as any).modelContext ?? (navigator as any).modelContext;

export async function registerAll(dotNetRef, descriptors) {
  const controller = new AbortController();
  for (const d of descriptors) {
    await ctx().registerTool({
      name: d.name,
      description: d.description,
      inputSchema: d.inputSchema,
      annotations: d.annotations,
      execute: async (args, agent) => {
        const json = await dotNetRef.invokeMethodAsync("ExecuteAsync", d.name, JSON.stringify(args));
        return JSON.parse(json);           // return the object: the browser serializes it (see "Result shape")
      },
    }, { signal: controller.signal });
  }
  return { dispose: () => controller.abort() };   // returned as IJSObjectReference
}
```

Notes:

- Detect `document.modelContext` first, `navigator.modelContext` second. Do not call `unregisterTool`;
  the current draft has none. Abort the signal.
- Registration is async (`registerTool` returns a Promise in the draft). Await it and report
  per-tool failures back to .NET instead of throwing out of the loop.
- `execute` receives an `AbortSignal`. Wire it to a `CancellationToken` through a second
  `[JSInvokable]` `Cancel(callId)` so a cancelled agent call stops a long export.
- Async interop only, so Server and WebAssembly both work. On Blazor Server the tool call
  round-trips the SignalR circuit; it is acceptable at agent speed.
- Bundle size: no dependencies. Do not bundle a polyfill. Document `@mcp-b/webmcp-polyfill` as
  something the host may add before Blazor starts.

## .NET bridge

`BpmnWebMcpBridge` is a plain class with one `[JSInvokable] Task<string> ExecuteAsync(string tool, string argsJson)`.

- Looks the tool up in a dictionary built from `IEnumerable<IBpmnWebMcpTool>`.
- Applies `AllowMutations`, `DisabledTools` and `IsReadOnly` (`BpmnEditorState.IsReadOnly`) before dispatch.
- Serializes with `System.Text.Json` source generation (trim-safe for WASM).
- Marshals onto the Blazor sync context with `InvokeAsync` before touching the session, since the
  call arrives from JS outside a render cycle. The session raises `StateChanged`, so the canvas
  updates without extra work.
- Never throws to JS. Failures become `{ ok:false, code, message }`.

## Result shape

The WebMCP draft (checked 2026-10 at webmachinelearning.github.io/webmcp) types `execute` as
`Promise<any>` and **serializes the fulfilled value to a JSON string itself**; `executeTool()` resolves to
that string. It defines no content-block or structured-result type (those belong to server-side MCP).
So the module **returns a plain object, never a pre-serialized string** (a string would be double-encoded
and arrive as an escaped quoted blob). A rejected promise reaches the caller as a `DOMException` with the
reason hidden, so the module never rejects: failures are ordinary results with `ok:false`. A value that
cannot be serialized fails the call, so results contain only JSON-safe data.

The result object is:

```json
{ "ok": true, "applied": true, "createdIds": ["Task_3"], "diagnostics": [], "revision": "r42", "isDirty": true }
{ "ok": false, "code": "EDIT-022", "message": "Pool deletion is refused", "diagnostics": [ ... ] }
{ "ok": false, "code": "PERSIST-CONFLICT", "message": "Stored document changed since it was loaded" }
```

`diagnostics` come straight from `BpmnEditResult.Diagnostics` (stable codes such as `EDIT-022`,
`CT-001`, `DI-011`). Agents can act on the codes.

`revision` is an **opaque string**, shared with the server-side module's result record. Here it is
derived from a counter the bridge increments per applied command, so an agent can detect that the user
edited in between; render it as a string (`"r42"`) and tell agents not to compare it numerically. It
describes the in-memory session and is unrelated to the **stored** revision, which only
`bpmn_get_document_info` and `bpmn_save` report as `storedRevision`. `isDirty` says whether the
session has unsaved edits.

Result records live in the shared handler assembly so both modules emit identical shapes (see
"Relationship to server-side MCP").

## Tool catalog (v1)

All commands go through `session.ApplyAsync`, so they share undo/redo and validation with the UI.
A multi-step tool uses `CompoundCommand` so an agent action is one history entry.

| Tool | Annotations | Maps to |
|---|---|---|
| `bpmn_get_diagram` | readOnly, untrusted | Summary: diagrams, elements (id, type, name, bounds), connectors. Names are user text. Paged by `MaxElementsPerCall` |
| `bpmn_describe_element` | readOnly, untrusted | `session.Describe(id)` snapshot, incl. properties and diagnostics |
| `bpmn_get_selection` | readOnly | `SelectionSnapshots` |
| `bpmn_validate` | readOnly | `ValidateAsync()` → report |
| `bpmn_export` | readOnly, untrusted | `format: "xml" \| "json"` through `BpmnXmlWriter` / `BpmnJsonWriter`; refuses over `MaxXmlBytes` |
| `bpmn_list_creatable` | readOnly | `StencilGroups` for the active container, so the agent learns what is legal and why a tool is disabled |
| `bpmn_create_element` | mutating | `CreateElementCommand`. Args: `type`, `name`, optional `containerId`, optional `afterElementId` + `direction` (layout is computed; agents are poor at coordinates) |
| `bpmn_connect` | mutating | `CreateConnectorCommand`, waypoints computed by a Core router helper |
| `bpmn_rename` / `bpmn_set_property` | mutating | `UpdatePropertiesCommand` (name, documentation, listed scalar properties only; no extension paths in v1) |
| `bpmn_select` | mutating (UI only) | `SetSelectionAsync`; lets an agent show the user what it means |
| `bpmn_undo` / `bpmn_redo` | mutating | `UndoAsync` / `RedoAsync`, bounded to changes made via WebMCP (see below) |
| `bpmn_delete` | consequential | `DeleteElementsCommand`; refusals such as `EDIT-022` pass through |
| `bpmn_import` | consequential | Previews with `PreviewImport`, then waits for human acceptance (see below). Never `AcceptImportAsync` directly |
| `bpmn_get_document_info` | readOnly, untrusted | Only when a store and binding are registered. From `BpmnDocumentBinding`: logical path, display name, stored revision, dirty flag, and `externalChange` (`none`, `updated`, `deleted`). Lets an agent know whether its edits are durable and whether the document moved underneath it |
| `bpmn_save` | consequential | Only when a store and binding are registered. Calls `Binding.SaveAsync()`. Returns `PERSIST-CONFLICT` when the stored revision moved; the agent must not retry blindly and should tell the user |
| `bpmn_open_document` | consequential | **Not in v1 (decided); reserved for later.** Only when enabled in options and a store is registered. Takes a logical document path (`BpmnDocumentPath`); refuses with `UNSAVED-CHANGES` when `isDirty`, and otherwise stages `Binding.LoadAsync` for human confirmation |

**Persistence tools are optional and thin.** They exist only when the host has registered an
`IBpmnDocumentStore` and the editor has a `BpmnDocumentBinding`; otherwise the editor is treated as an
in-memory scratch document and the agent sees only `bpmn_export` for getting content out. No tool
takes a path, URL, container or connection string. `bpmn_open_document` is off by default
(`BpmnWebMcpOptions.AllowOpenDocument`), accepts only paths the store recognises, and the store decides
whether an id is allowed for the current user. Document *listing* is deliberately not a v1 WebMCP
tool: the browser agent usually works on what the user already opened, and listing a tenant's
documents is the server-side module's job. A host that wants it can add its own `IBpmnWebMcpTool`
over `IBpmnDocumentStore.ListAsync`.

**Stale documents.** When `Binding.ExternalChange` is set (an MCP client or another user changed
the stored document), every *mutating* WebMCP tool, including `bpmn_save`, refuses with
`STALE-DOCUMENT` and a message saying the user must choose Reload, Save as copy or Dismiss in the
editor. Read-only tools keep working and include `externalChange` in their result, so the agent can
explain the situation. The agent can never resolve the change itself; resolution is the user's
action on `BpmnPart.ExternalChangeOffer`. This stops an agent from piling edits onto a document
that is already out of date.

Saving is its own consequential action rather than a side effect of each edit. Editing stays
local and undoable; `bpmn_save` is the point where the change leaves the browser session. If the
host autosaves, `isDirty` simply stays false and `bpmn_save` becomes a no-op that returns the current
stored revision.

Deliberately excluded from v1: pool/process deletion workarounds, template application,
extension-command editing, execution animation, script content edits (Script Tasks run user code
elsewhere; an agent-authored script is a separate trust decision).

`bpmn_create_element` needs layout. The `CreateElementCommand` requires `Bounds`. Add to Core a
small public `BpmnPlacement.NextTo(document, diagramId, anchorId, direction)` helper, with DI-011
clearance, rather than duplicate placement logic inside the module.

## Security and consent

Threat: indirect prompt injection. A diagram's names, documentation and imported XML are
attacker-controllable text that flows into tool output.

1. **Annotations are metadata, not enforcement.** The browser agent may ignore them, so the module
   enforces its own rules.
2. **Untrusted outputs.** Every tool returning user text sets `untrustedContent`. The result
   wraps labels in a `{ "untrustedText": "..." }` field and never concatenates them into the
   `message` field, so instructions in a label cannot read as tool commentary.
3. **Consequential tools.** With `RequireConfirmationForConsequential`, `bpmn_delete`,
   `bpmn_import`, `bpmn_save` and `bpmn_open_document` do not act immediately. They stage the change
   and return `{ ok:true, pendingConfirmation:true, summary }`.
   The editor shows a confirmation surface and the user accepts. For import this reuses
   `PreviewImport` and `BpmnImportOffer`, which already exist. For the others, add a
   `BpmnPart.AgentConfirmation` part (kit-implemented, like `ImportOffer`). Saving and switching
   documents involve storage outside the browser, so they should confirm by default even for hosts
   that relax confirmation for ordinary edits: expose a separate
   `RequireConfirmationForPersistence` option (default `true`).
4. **Read-only and limits.** `IsReadOnly` makes every mutating tool return `READONLY`. Size and count
   limits bound output and input.
5. **Agent edits are undoable and attributable.** Wrap every mutating call's command in a
   `CompoundCommand` labelled `"Agent: <tool>"` so the history list shows its source.
   `bpmn_undo` may undo only the head entry if it carries that label; otherwise it refuses, so an
   agent cannot unwind the user's own work.
6. **Same-origin only.** Never set `exposedTo`. Cross-origin exposure is a host decision and out of scope.
7. **No secrets.** Tools never expose configuration, template registries with credentials, or
   `Configuration` internals.
8. **Server-side script validation** (`Bpmn.Scripting.*`) is unaffected; nothing here executes scripts.
9. **Storage stays behind the host.** The agent cannot choose a storage target, path, URL or
   credential. Stored-document metadata (display names) is user text and goes in `untrustedText`.
   Storage credentials are never read by the module; the host's persistence implementation holds
   them. A save runs with the *user's* authorisation (the host's `IBpmnDocumentStore`
   resolves it from the ambient principal), so an agent cannot write anything the user could not.
10. **Conflicts are surfaced, not resolved.** A `PERSIST-CONFLICT` is never retried or force-overwritten
    by the module, and the tool descriptions say so; resolving it is the user's decision.

## Graceful degradation

| Environment | Behavior |
|---|---|
| No WebMCP | `BpmnWebMcp` renders nothing, `State == Unsupported`, no error, no console noise beyond one `debug` log |
| Prerender | JS interop is skipped (registration happens in `OnAfterRenderAsync`); no `IJSRuntime` call during prerender |
| Server circuit lost | Tools are unregistered on dispose; on reconnect a new component instance registers again |
| API changes again | Only `webmcp.ts` changes; descriptor JSON and the C# tool catalog are stable. Gate by feature detection, never UA sniffing |
| Origin trial token | Host's concern (meta tag or header). Document in the package README |

## Relationship to server-side MCP

Separate, later package (`Bpmn.Mcp`, designed in the server-side section below, using the C# ModelContextProtocol SDK) for headless and CI
use over a persisted document store (file, blob or database). Design the tool catalog so
`IBpmnWebMcpTool` implementations depend only on an `IBpmnToolHost` abstraction (document access +
`ApplyAsync`) rather than `BpmnEditorSession`, so a later server package can reuse the handlers. For
v1, keep the abstraction internal and implemented by a thin adapter over the session; do not publish
it until a second consumer exists.

How the two modules differ on storage:

| | WebMCP (this half) | Server-side MCP |
|---|---|---|
| Document | Held by the open editor session | Loaded per call from the store |
| Persistence seam | Core `IBpmnDocumentStore` via `BpmnDocumentBinding`, optional | Core `IBpmnDocumentStore`, required |
| Identity | Whatever the user has open; logical path from the binding | Logical path (`documentId`) plus tenant |
| Concurrency | Session `revision` for in-browser edits; stored revision enforced by the store on save | Store revision on every write |
| Listing | Not a v1 tool | `bpmn_list_documents`, paged |

Both modules, and the Blazor host's own load/save, use the **same store instance type and the same
revision tokens**. A host registers its store once; the editor, WebMCP and `Bpmn.Mcp` all use it, and a
document saved by an agent through MCP is seen with a new revision by the editor, which then reports a
conflict rather than overwriting. The binding is the "document I have open" view of the store, so no
adapter between two seams is needed.

Shared pieces, in whichever assembly ends up holding the handlers: the result record (with opaque
`revision`), the diagnostic and error codes (including `PERSIST-CONFLICT` / `MCP-CONFLICT`, which
should become one code with one meaning), the `untrustedText` convention, and the size limits.

## Testing

- **Unit:** every tool against a headless `BpmnEditorSession` (no JS), asserting results, codes, history labels, read-only and limit behavior. Prompt-injection fixtures: labels containing instructions must appear only in `untrustedText`.
- **Contract:** a snapshot test of the descriptor JSON (names, schemas, annotations) so catalog changes are deliberate.
- **TS (vitest):** `isSupported` for document, navigator and neither; abort unregisters; duplicate-name rejection handled; per-tool failure reporting.
- **E2E (Playwright, see the `verify` skill):** inject a minimal `document.modelContext` stub before load, call `create_element` then `export`, and assert the canvas and exported XML agree. Run once with the stub absent to confirm no-op.
- **Persistence:** run the tools against `InMemoryBpmnDocumentStore` (plus a wrapper that can inject a concurrent external write, faults and timeouts) through a real `BpmnDocumentBinding`, covering: persistence absent (persistence tools not registered), clean save, save after a concurrent external change (`PERSIST-CONFLICT`, session untouched), dirty-flag transitions, `bpmn_open_document` refused while dirty, unknown or unauthorised id, and an implementation that throws or times out (tool returns `ok:false`, never throws to JS). Also a test that no tool's schema has a `path`, `url` or `connection` property.
- No test depends on real Chrome WebMCP, since it is an origin trial.

## Docs and playground

- `docs/webmcp.md` (user guide) and a section in `docs/composing-an-editor.md` listing `BpmnWebMcp` as an optional part.
- Playground toggle on `EditorDemo` that calls `AddBpmnWebMcp` and shows the support state.
- README for the package with the origin-trial note and the date it was checked.

## Implementation order

0. Core storage (prerequisite for everything below, and valuable on its own), in this order:
   a. `BpmnDocumentPath` with exhaustive unit and fuzz tests (everything else depends on it);
   b. `Storage` namespace: `IBpmnDocumentStore` (including `StatAsync` and hierarchical `ListAsync`), records, `InMemoryBpmnDocumentStore`;
   c. the Core dependency-purity architecture test and the shared store contract test suite;
   d. `BpmnDocumentBinding`, session saved-point/`IsDirty`;
   e. change-notification abstraction: `IBpmnDocumentChangeBus`, `IBpmnDocumentChangeSubscriber`, in-process bus, `NotifyingBpmnDocumentStore`, binding `ExternalChange` state and `ExternalChangePolicy`;
   f. `BpmnPart.ExternalChangeOffer` in both kits;
   g. playground load/save through the in-memory store, with a button that simulates an external change.
   h. keepalive/gap-detection contract in the subscriber, the `IBpmnDocumentChangeIngress` helper and a blob/Event Grid and PostgreSQL reference ingress in `samples/`.
   Then, as optional packages after Core: `Bpmn.Storage.SignalR`, `Bpmn.Storage.ServerSentEvents`, and a shared-bus package (Redis or similar) when multi-instance or separate-process delivery is needed.
1. Core: `BpmnPlacement` helper, `BpmnPart.AgentConfirmation`, history label support for agent commands (check what `BpmnEditHistory` already offers).
2. `Bpmn.WebMcp` read-only tools plus the TS module and `BpmnWebMcp` component. Ship behind `AllowMutations=false` by default for the first preview.
3. Mutating tools, then consequential tools with confirmation.
3a. `bpmn_get_document_info` and `bpmn_save` over Core's binding (`bpmn_open_document` is post-v1); no new WebMCP-specific persistence type.
4. Tests, docs, playground (the playground gets an in-memory persistence implementation to demonstrate the flow).
5. Reassess against the spec before 2026-11-17 and whenever Chrome 157+ lands.

## Milestone 2 as built

- **Edit origin.** `BpmnEditCommand.Origin` (a `BpmnEditOrigin`: `User` by default, or
  `BpmnEditOrigin.FromAgent("webmcp")`) is recorded with each history entry and exposed as
  `BpmnEditHistory.UndoOrigin`/`RedoOrigin` and `BpmnEditorSession.UndoOrigin`/`RedoOrigin`. Labels stay
  display-only.
- **Placement.** `BpmnPlacement.NextTo(document, anchorId, type, direction, options, diagramId)` and
  `BpmnPlacement.Inside(document, containerId, type, options, diagramId)` return a
  `BpmnPlacementResult` (bounds, container, pool, diagram) or a problem: `PL-001` (anchor missing or not
  drawn), `PL-002` (not a container that can hold Flow Nodes, or not drawn expanded), `DI-011` (no clear
  space). Default sizes are ported from the canvas. Free space uses the editor's own DI-011 rule with the
  configured clearance (stricter than the canvas's interactive nudge), so a placement always passes the
  `CreateElementCommand` check.
- **Agent confirmation.** Rather than staging an action and returning `pendingConfirmation`, Core offers an
  awaitable question: `BpmnEditorSession.RequestConfirmationAsync(BpmnConfirmationRequest, token)` completes
  with the user's answer. One question at a time (a second is declined at once); cancelling the token
  withdraws it; closing the editor answers no. The `AgentConfirmation` part shows it as a modal
  `alertdialog` in both kits, with quoted diagram text in a separate plain-text list. The WebMCP tool will
  await the answer under its `execute` abort signal and return `ok:false`, `code:"DENIED"` on a no, which
  is simpler for agents than polling for a staged outcome. This adds public members to
  `BpmnEditorSession` (`PendingConfirmation`, `RequestConfirmationAsync`, `ResolveConfirmation`,
  `UndoOrigin`, `RedoOrigin`); the session is the only object every part can reach, so it owns the
  question.

## Milestone 3 as built

- **`Bpmn.Tools`** (`Hasseware.AspNetCore.Components.Bpmn.Tools`, public, marked preview) holds the
  handlers (`BpmnTools`), the result record (`BpmnToolResult`, `BpmnToolResult<T>`), codes
  (`BpmnToolCodes`), `BpmnUntrustedText`, `BpmnToolLimits`, the catalog with JSON Schemas and a
  name-based dispatcher (`BpmnToolCatalog`), source-generated JSON (`BpmnToolJson`) and the id-based
  diff (`BpmnDocumentDiff`). It references Core only.
- **`IBpmnToolHost` is one host per document**, not a host taking a `documentId`: the browser module wraps
  the session; the MCP server will create a host per document it loads. `BpmnDocumentToolHost` wraps a
  document held in memory.
- **Confirmation lives in adapters.** Handlers never ask; `bpmn_delete` is annotated destructive and the
  browser module asks (`RequestConfirmationAsync`) before calling it.
- **Connection legality is enforced headlessly.** The editor's create-connector command checks ids and
  geometry, not BPMN connection rules, which the canvas applies during a drag. `BpmnConnectionRules` in
  Core now evaluates the same `live` rules from the embedded `rules/bpmn-rules.json`, as a port of the
  TypeScript evaluator, and `rules/connection-cases.json` is a case table both runtimes test against.
  `bpmn_connect` and `bpmn_create_element` with `connectFromAnchor` refuse a forbidden connection with
  `ILLEGAL-CONNECTION` and the rule ids, creating nothing.
- **`bpmn_diff` is not in the catalog**: its baseline is something only the adapter can name (a stored
  revision, another document). `BpmnTools.DiffAsync(host, baseline)` is the shared handler.
- **`bpmn_delete` lists every deleted id**, not just the editor's `EDIT-023` count of cascaded removals.

## Milestone 4 as built

- **`Bpmn.WebMcp`** (`Hasseware.AspNetCore.Components.Bpmn.WebMcp`, preview) references Core and `Bpmn.Tools`, never a
  kit, and ships `src/Bpmn.WebMcp.Scripts` (one esbuild module, no dependencies) as its static web asset. Both kits
  take `<BpmnWebMcp />` in their `Dialogs` slot, which is inside the editor's scope.
- **Read-only: six tools** — `bpmn_get_diagram`, `bpmn_describe_element`, `bpmn_get_selection`, `bpmn_validate`,
  `bpmn_export`, `bpmn_list_creatable`. The tool set is built from `BpmnToolCatalog`, filtered to read-only, so the
  editing tools are not registered at all. **There is no `AllowMutations` option yet**: with nothing for it to enable
  it would be a dead switch, and "off by default" holds by construction. It arrives with the editing tools, default
  false, together with the confirmation they need.
- **No `IBpmnWebMcpTool`.** The "Public surface" sketch above had tools taking a `BpmnEditorSession`; milestone 3
  superseded it. `BpmnSessionToolHost : IBpmnToolHost` wraps the session (edits carry `FromAgent("webmcp")` and join the
  user's undo history; `Revision` moves whenever the session's document is a different one, so a user edit between two
  agent calls is visible), and `BpmnWebMcpBridge` calls `BpmnToolCatalog.InvokeAsync` and `BpmnToolJson.Serialize`.
- **`bpmn_get_selection` is in the shared catalog** (the tenth tool), with the optional `IBpmnSelectionHost`. A host
  that loads a stored document has no selection and reports an empty one, so the catalog stays the single list of
  names for the browser and server surfaces. Core gained one public member, `BpmnEditorSession.Profiles`; nothing else.
- **The module's contract, corrected against the draft** (read 2026-10-15): `modelContext` is on `Document`, and
  `isSupported` uses `||` (the sketch's `!!a ?? !!b` never reached `navigator`). `execute` receives
  `{ signal }` as its second argument, not an agent object. `ToolAnnotations` are `readOnlyHint`,
  `untrustedContentHint`, `consequentialHint`; `exposedTo` is never set. There is no unregister, so
  `registerAll` registers each tool separately under one `AbortSignal`, reports per-tool failures as data
  (`{ id, registered, failures }`) and `unregisterAll(id)` aborts it. A call never rejects: a failing bridge becomes
  `{ ok: false, code: "INTERNAL-ERROR" }`.
- **Cancellation.** The agent's signal is forwarded as `Cancel(callId)`, which can arrive before the call it names;
  the bridge remembers such a cancellation (bounded) and answers `CANCELLED` without running the tool.
- **Component.** `BpmnWebMcp` derives from `BpmnEditorComponentBase`, does not re-render on state changes and draws
  nothing. `Status` (not `State`, which the base class owns), `RegisteredTools`, `OnRegistered`, `OnUnavailable`,
  `ToolPrefix`, `InstanceName`. A scoped `BpmnWebMcpRegistry` gives each instance on a page a suffix of its own.
- **Tests.** `tests/Bpmn.WebMcp.Tests` runs the host, bridge, tool set and component against a fake browser;
  vitest covers the module against a stub model context; `tests/Bpmn.E2E.Tests/WebMcpTests.cs` installs a minimal
  `document.modelContext` before the page loads and calls the tools through it on both kits, and once with it absent.

## Milestone 5 as built

- **Editing is opt-in.** `BpmnWebMcpOptions.AllowMutations` (default false) decides whether any tool that changes the
  diagram is registered; `<BpmnWebMcp AllowMutations="…">` overrides it per editor. With it off, behaviour is exactly
  milestone 4. `RequireConfirmationForConsequential` and `RequireConfirmationForPersistence` (both default true) cover
  `bpmn_delete` and `bpmn_save`; the latter is separate, as decided above.
- **Tools.** The catalog grew from ten to fifteen: `bpmn_select`, `bpmn_undo`, `bpmn_redo`, `bpmn_get_document_info`,
  `bpmn_save`. They need more than a document, so they work through two optional host interfaces beside
  `IBpmnSelectionHost`: `IBpmnInteractiveHost` (history origins, undo, redo, select) and `IBpmnPersistenceHost`
  (`IsBound`, name, stored revision, `IsDirty`, `ExternalChange`, `SaveAsync`). `BpmnSessionToolHost` implements both over the
  session and its `DocumentBinding`; a host without them gets `NOT-FOUND` from those tools. The two persistence tools are
  registered only for an editor that has a binding when `BpmnWebMcp` registers.
- **The guard** (`BpmnWebMcpGuard`) runs in the bridge before the handler of every tool that changes anything: stale
  document ⇒ `STALE-DOCUMENT` (not for `bpmn_select`); then, for `bpmn_delete` and `bpmn_save`, the editor's confirmation
  prompt through `session.RequestConfirmationAsync`, linked to the call's cancellation so an abandoned call withdraws
  its question; no answer path or a no ⇒ `DENIED`. A bridge built without a guard refuses every changing tool. The
  question's title and summary are the module's own localized words (five new Core strings, translated in the 13
  languages); element names go only in `Details`.
- **Deviation: undo by origin.** The sketch labelled every agent action `"Agent: <tool>"` in a compound command. History
  already records the origin (milestone 2), so `bpmn_undo` and `bpmn_redo` compare it with the host's: only the agent's own
  entry may be stepped over, and the user's work is never unwound. No label is needed.
- **`isDirty`** is on every result of a bound editor (null, so omitted, otherwise), as the "Result shape" sketch had it.
- **Demo.** A second toggle, `.demo-webmcp-edit`, appears under the WebMCP one and re-creates the component with editing
  on (the component reads the option once, at registration).
- **Tests.** `SessionToolTests` (Tools, 19), `EditingTests` and tool-set cases (WebMcp, 22), and E2E cases on both kits
  (create then Ctrl+Z, delete with Deny and Allow). Mutation checks: dropping the confirmation, the stale check or the
  origin guard each fails a test.
- **Still not here:** `bpmn_import`, `bpmn_open_document`, `bpmn_apply_batch`, and the server-side module.

## Milestone 6 as built

- **Projects** (plain libraries, none references a kit or Razor): `Bpmn.Storage.Testing`, `Bpmn.Storage.FileSystem`,
  `Bpmn.Mcp` (on `ModelContextProtocol` 2.2.0) and `Bpmn.Mcp.Tool` (the `bpmn-mcp` dotnet tool). The shared store
  contract suite moved out of `tests/Bpmn.Tests` into `Bpmn.Storage.Testing` as an abstract xUnit v3 class; Core's
  in-memory and notifying stores derive from it unchanged, and the file store runs it too. Core gained one public
  helper, `BpmnDocumentPaging.Page`, so every store orders folders first and builds cursors the same way (the in-memory
  store now uses it).
- **SDK facts, confirmed against 2.2.0** (these were "verify" items): the tool attribute has `ReadOnly`, `Destructive`,
  `Idempotent`, `OpenWorld`, `Title`, `UseStructuredContent`; `CallToolResult` carries `Content`, `StructuredContent`
  and `IsError`; `McpServer.Create` with `StreamServerTransport`/`StreamClientTransport` over `Pipe`s is the in-process
  test setup; `StdioClientTransport` launches a child process for the smoke test; `WithTools<T>()` is used (no assembly
  scan). Tools return a `CallToolResult` whose text block is the shared result JSON and whose structured content is the
  same JSON; no output schema is advertised in v1.
- **Read-only tools:** `bpmn_list_documents`, `bpmn_open` (the diagram summary plus the reader's findings as
  `IMPORT-INFO/DEGRADED/DROPPED/SCHEMA-ERROR` diagnostics and the store revision), `bpmn_validate`,
  `bpmn_describe_element`, `bpmn_export`. They take `documentId` (a logical path), parse it first (`INVALID-PATH`), `StatAsync`
  against `MaxXmlBytes` before reading (`TOO-LARGE`), load, and call the shared handlers through a new
  `BpmnStoredDocumentToolHost` (read-only, its `Revision` is the stored revision). New codes: `INVALID-PATH`,
  `INVALID-DOCUMENT`, `STORE-UNAVAILABLE`. The tenant comes from an `IBpmnMcpTenantResolver` (a fixed option by default),
  never an argument. `bpmn_diff`, `bpmn_get_diagram`, writing tools and `apply_batch` are milestone 7.
- **File store.** Default space under the root, tenants under `<root>/~tenants/<tenant>` (a document path cannot contain
  `~`). It refuses to follow any symbolic link or junction on the way (a deliberately stricter rule than the sketch's
  "leaving the root"), allows only `.bpmn`, `.xml` and `.json`, writes a temporary file and moves it over the target under
  a striped lock that also works across processes (64 lock files under `~work`), and uses a revision that is a hash of
  length and last-write time, which a save always moves forward. On Windows and macOS it treats names that differ only by
  case as one file and never overwrites across them. Moves are retried briefly because Windows refuses a replace while a
  scanner holds the target. It does not watch the folder.
- **`bpmn-mcp`.** `serve --root <folder> [--tenant t]` is a stdio MCP server with every log line on stderr; unknown
  options (including `--allow-writes`) are refused. `validate <file|folder|glob>... [--json] [--warnings-as-errors]` runs
  the editor's rules through `BpmnTools.ValidateAsync`, so a build fails on what an agent is told; exit 0, 1 (findings or a
  file that is not BPMN) or 2 (usage or an unreadable file). The CLI has no argument-parsing dependency.
- **Tests.** The file store (36, link, case and cross-store race cases, with the symlink case using a junction where
  symbolic links are not permitted) and `Bpmn.Mcp.Tests` (69): every tool over the in-memory and file stores through a real
  client and server, bad ids never reaching the store, the size gate, tenant isolation, injection text, XXE and an entity
  bomb, store failure without leaking its message, the `validate` exit codes and a real child process over stdio.
  Mutation checks: dropping the size gate, putting a document id in a message, sending logs to stdout, or dropping the link
  check each fails a test.
- **Still not here:** mutating tools, `bpmn_apply_batch`, `bpmn_diff` as a tool, the HTTP host with authentication,
  and the blob, EF Core and network-transport packages.

## Milestone 7 as built

- **Read, edit in memory, write back at the revision read.** Every writing tool parses the id, asks the authorizer, applies
  the `StatAsync` size gate, loads, edits in a fresh `BpmnDocumentToolHost` with the shared handlers, serializes (size-gated
  again) and calls the store's conditional `SaveAsync`. The `ifMatch` is the argument `expectedRevision` when the agent passes
  one (the revision it saw), otherwise the revision this call read, so the window is always atomic and never a session.
  `PERSIST-CONFLICT` carries the current revision and writes nothing; a store failure while saving is `STORE-UNAVAILABLE`
  with a message that the write may or may not have happened, and the call is never retried. The result's `revision` is the
  new stored revision (the handlers' per-host counter is replaced). The sketch's `MCP-CONFLICT` is the shared
  `PERSIST-CONFLICT`.
- **Tools.** `bpmn_create_element`, `bpmn_connect`, `bpmn_set_property`, `bpmn_delete`, `bpmn_apply_batch`,
  `bpmn_create_document` (writing) and `bpmn_diff` (reading, over `BpmnTools.DiffAsync` with two stored documents). Writing is
  opt-in twice: `WithBpmnWriteTools()` registers the class and `AllowWrites` lets it act; with `AllowWrites` false every call is
  `READONLY`. `bpmn-mcp serve --allow-writes` does both. Deleting documents, `save_as` and import stay out of scope.
- **Batch.** `operations: [{ op, arguments, as }]`, at most `MaxBatch` (50), run in order through the same handlers on one
  in-memory host. `as` names what a step creates and a later step writes `@name` for an element id (anchor, container,
  source, target, element, deleted ids). All or nothing: the first refusal rejects the batch with `failedAt`, the handler's code
  and its diagnostics; success is one save and one revision. `bpmn_create_document` saves a blank document create-only.
- **Authorization.** `IBpmnDocumentAuthorizer` (read, write, create) is consulted before any store access; a refusal is
  answered as `NOT-FOUND` (no existence leak), except that a refused create is `FORBIDDEN`. Listing hides unreadable
  documents but shows folders, which are implied by paths. `IBpmnMcpTenantResolver.GetCallerId()` (additive, default null)
  feeds the audit line: caller, tool, document id and revision before and after, never content.
- **HTTP host `Bpmn.Mcp.AspNetCore`.** `AddBpmnMcpHttp` sets `SessionMode = Stateless` explicitly and replaces the tenant
  resolver with one that reads the principal; `MapBpmnMcp` requires an authenticated caller (plus the tenant claim when
  `TenantClaimType` is set, so a caller without it is 403 and never gets the shared space) and an optional named policy, and
  wraps the endpoint to refuse a `Host` that is not loopback unless listed (DNS rebinding), a foreign `Origin`, and a body
  over `MaxRequestBodyBytes` (413). A rate-limiter policy can be named. The host must choose `TenantClaimType` or
  `SingleTenant` or `MapBpmnMcp` throws; `AllowAnonymous` needs `SingleTenant`. Checked in a spike against SDK 2.2.0:
  unauthenticated is 401, the principal is visible to tools through `IHttpContextAccessor`, and there is no session id.
  Body-size enforcement on a chunked request depends on the server's `IHttpMaxRequestBodySizeFeature` (Kestrel has it, the
  in-process test server does not).
- **Tests.** 121 in `Bpmn.Mcp.Tests` (the writing path over the in-memory and file stores through a real client and
  server: persistence and revisions, stale `expectedRevision`, one winner among six writers, no edit reported saved that was
  overwritten, batch aliases and all-or-nothing, oversize, store failure on save, authorizer, injection text, audit line,
  diff) and 12 in `Bpmn.Mcp.AspNetCore.Tests` (401, tenant isolation and missing claim, stateless, cross-connection
  conflicts, per-document authorizer, Host, Origin, body limit, startup validation). Mutation checks: ignoring
  `expectedRevision`, skipping the authorizer, dropping the tenant-claim requirement, the Host check or the Origin check each
  fails a test.
- **Still not here:** the blob, EF Core and network-transport packages and the ingress samples (milestone 8), resources and
  prompts, and a WebAssembly test of the server-sent-events transport.

## Milestone 8a as built

- **Packages.** Server and client are separate by runtime: `Bpmn.Storage.ServerSentEvents` (+ `.Client`) and `Bpmn.Storage.SignalR` (+ `.Client`). The servers
  need ASP.NET Core, the clients must run in WebAssembly. Everything transport-independent is in Core: `BpmnDocumentChangeWire` (the one wire shape, source-generated
  JSON), `BpmnChangeFeedServer` (parse the path, ask the authorizer, count subscriptions, one coalescing slot per subscription), `BpmnChangeFeedServerOptions`, and
  `ReconnectingBpmnDocumentChangeSubscriber`. `IBpmnDocumentAuthorizer` and `BpmnDocumentAction` moved from `Bpmn.Mcp` to Core's `Storage` namespace so that transports
  can use them.
- **The reconnect engine.** A transport implements one method, `OpenStreamAsync`, returning an `IBpmnChangeConnection` that yields changes and heartbeats. The base keeps
  one stream per subscribed document, gives up on a stream silent for `HeartbeatInterval` x `StalenessFactor` (30 s x 2.5 = 75 s by default; zero or infinity turns it off),
  reconnects with exponential backoff and equal jitter (1 s to 30 s), raises `Resubscribed` after every gap, and on a 401/403 stops, moves to `Unauthorized` and raises
  `AuthorizationRequired`; `RetryAsync()` resumes. Verified with a fake clock: silence, failure, end of stream, backoff growth and cap, unauthorized stop and resume, disposal, a
  throwing handler.
- **SSE.** `MapBpmnDocumentChangeEvents` serves `GET ?path=` with `ready`, `ping` and `change` events. **Heartbeats are events, not `: ping` comments** as the sketch said,
  because `SseParser` does not surface comment lines to the client (found in a spike). The client uses the browser's streaming fetch (the
  `WebAssemblyEnableStreamingResponse` request option) and `SseParser`, which the playground's browser test confirms run in WebAssembly. Without `MaxStreamLifetime` a stream outlives
  its credentials, so the server ends streams after 30 minutes by default and the client reconnects with fresh credentials.
- **SignalR.** The hub (`MapBpmnDocumentChangeHub`) has `Subscribe(path)` answering `ok`, `notfound`, `toomany` or `forbidden`, and `Unsubscribe`; the server sends `Changed` with the wire JSON as
  text. **Deviation from the sketch: no groups.** Each connection subscribes to the bus directly, because the bus has no all-documents tap and a group send from per-instance
  subscriptions would deliver every change once per instance; a multi-instance host needs a shared bus either way. The client opens one hub connection per document, turns off the
  heartbeat check (SignalR has its own keepalive) and reconnects through the same engine. A hub invocation has no request of its own, so the hub sets
  `HttpContextAccessor.HttpContext` for the call, which lets an authorizer that reads the caller work.
- **Security, both servers.** Authenticated by policy; tenant from the `TenantClaimType` claim or an explicit `SingleTenant`; mapping throws when neither is chosen, when both are,
  or when anonymous access is allowed without `SingleTenant`; a caller with no tenant claim is refused; a path that is not a document and one the caller may not read answer the
  same; per-caller (16) and per-host (10,000) limits; the message carries no content and no tenant.
- **Tests.** Core: 8 reconnect-engine tests on a fake clock. `Storage.Testing` gained two suites every transport runs: the subscriber contract (delivery, deletion, other documents, disposal,
  live state, a dropped connection reported once and delivery afterwards, a 1,000-write burst delivering the latest, refusal and resumption) and the binding long-session contract (a
  change made while the feed was down shows after exactly one catch-up check; a pushed change needs none). SSE (26) and SignalR (25) run both, plus their own security tests.
  Mutation checks: removing the staleness timer, the authorizer call, or taking the tenant from the query string each fails tests. The browser test `LiveUpdatesTests` drives the
  WebAssembly page against the playground's server feed, including the reconnection.
- **Left out on purpose:** a WebAssembly *editor* over a remote store (the playground page shows the feed, not an editor), Redis and broker buses, and the SignalR backplane.

## Milestone 8b as built

- **Packages.** `Bpmn.Storage.AzureBlob` and `Bpmn.Storage.EfCore`, guide in [storage-backends.md](storage-backends.md). Both pass `BpmnDocumentStoreContractTests`.
- **Azure Blob.** The store talks to a small `IBpmnBlobContainer` (get, conditional put, conditional delete, list, metadata), so the contract suite runs against an in-memory fake
  with real ETag semantics; `AzureBlobContainer` adapts the SDK. The ETag is the revision (read with `ToString("H")`); a create is `If-None-Match: *`, an update `If-Match`.
  Tenants live under `~tenants/<tenant>/`, which no document path can contain. Listing collects up to `MaxListEntries` (100,000) and then pages with `BpmnDocumentPaging`.
  `AzureBlobBpmnDocumentStore.TryKeyOf` maps a blob name back to a key, for ingress. **Deviation from the sketch:** the abstraction is public (`IBpmnBlobContainer`), not an internal
  gateway, so a host can test against it.
- **EF Core.** `EfCoreBpmnDocumentStore<TContext>` over `IDbContextFactory`; one table keyed `(Tenant, Path)`; `ApplyBpmnDocuments(table, schema, pathCollation)`. The revision is a
  version number the store increments, so it is identical on every provider; an update is `ExecuteUpdate … WHERE Version = @held` and a create an insert the key refuses on a duplicate.
  Folder queries are the ordinal range `[folder/, folder0)`, never `LIKE`, which is why SQL Server needs a `BIN2` collation on the path column. Listing projects only path, size and time.
- **Ingress.** `BpmnDocumentChangeIngress` in Core turns a backend signal into a bus change and drops a repeated revision (bounded memory: 4,096 keys). **Deviation from the sketch:**
  no hosted-service base class; the helper is the whole contract, and the samples show two hosts around it. `samples/BlobEventGridIngress` (webhook with the validation handshake,
  `aeg-sas-key` checked in constant time, body cap) and `samples/PostgresNotifyIngress` (`LISTEN`/`NOTIFY` service, trigger script) are built in CI and tested by `tests/Bpmn.Samples.Tests`.
- **Tests without containers.** AzureBlob: 64 (20 gated), EfCore on SQLite: 34 run (20 gated), ingress: 6, samples: 25. The Azurite class runs when `BPMN_AZURITE_CONNECTION` is set,
  the SQL Server class when `BPMN_SQLSERVER_CONNECTION` is set; both compile always and skip otherwise. Mutation checks: dropping `If-Match` in the blob store and dropping the
  `Version` predicate in EF Core each fail the concurrency tests.
- **Left out on purpose:** a Postgres or SQL Server store package of their own (EF Core covers them), broker-backed buses, blob change-feed ingestion, and any ingress for SQL Server
  (Change Tracking and CDC are pull-based).

## Proposed milestone 9: AG-UI chat over the editor (not started)

Status: proposal, researched 2026-10-15 and not implemented. Sources: the .NET blog post "Build agentic UI with Blazor"
(<https://devblogs.microsoft.com/dotnet/build-agentic-ui-blazor/>), the Microsoft Agent Framework AG-UI pages on Microsoft Learn, and the AG-UI docs
(<https://docs.ag-ui.com>). The Blazor package is experimental and needs the .NET 11 RC1 SDK, so every API name below is to be confirmed against the shipping
package before work starts.

**What AG-UI is, here.** An open, event-based protocol (typed events over HTTP POST and Server-Sent Events) between an agent backend and a user-facing app. It sits
beside the protocols this design already uses, not instead of them: MCP stays how an agent reaches tools and documents (the server in milestones 6 and 7), WebMCP stays how a
browser-resident agent drives the open diagram (milestones 4 and 5), and AG-UI would carry the conversation, tool-call and approval events, and shared state between an agent
the host runs and the Blazor page. A host with no agent of its own, or one that is content with an external browser agent, needs none of this.

**What Blazor offers (experimental `Microsoft.AspNetCore.Components.AI`, from the post).** `AGUIChatClient` (an `IChatClient`, in the AG-UI .NET SDK) built from `AGUIChatClientOptions(httpClient, endpoint)`;
`UIAgent` / `UIAgent<TState>` and the components `ChatPage`, `MessageList`, `MessageInput`, `BlockRenderer<TBlock>`; frontend tools as ordinary `AIFunction`s in `ChatOptions.Tools`
that run in the Blazor app; `ApprovalRequiredAIFunction` surfacing as a `FunctionApprovalBlock` with `Approve` and `Reject`; shared state sent as `RunAgentInput.State` and returned as
`STATE_SNAPSHOT` or `STATE_DELTA` (RFC 6902 JSON Patch); predictive state (`AcceptPredictiveState` / `RejectPredictiveState`) for a proposal the user commits or discards. The server side is
`Microsoft.Agents.AI.Hosting.AGUI.AspNetCore` (`AddAGUIServer`, `MapAGUIServer`) over a Microsoft Agent Framework agent; those packages are prerelease and were renamed recently.

### Fit with what exists

| AG-UI concept | Existing piece | Notes |
|---|---|---|
| Frontend tools | `Bpmn.Tools` handlers, behind `IBpmnToolHost` | A third thin adapter next to `Bpmn.WebMcp` and `Bpmn.Mcp`: wrap each handler as an `AIFunction`. Same tool names, schemas and result shape, so no tool logic is duplicated. |
| Approval of consequential edits | `BpmnPart.AgentConfirmation`, `bpmn_save` confirmation in WebMCP | Either keep the editor's confirmation as the single prompt, or map it to `ApprovalRequiredAIFunction` so it appears in the chat. Never both: the user would be asked twice. |
| Undo and "an agent did this" | `BpmnEditCommand.Origin`, labelled history entries | Edits run through the same commands with `BpmnEditOrigin.FromAgent("ag-ui")`; one tool call, one undo step. |
| Predictive state | `bpmn_apply_batch` (all-or-nothing) | A batch as a proposal the user accepts or rejects as one step is the natural fit; to be confirmed that the editor can show a pending proposal (preview) before committing. |
| Shared state | `BpmnDocumentBinding`, document store, change events | See "State" below. |
| Concurrent edits | revision check on save, `ExternalChange` | AG-UI defines no conflict handling; this stays the editor's. |
| Server-side document access | `Bpmn.Mcp` and the HTTP host | The agent host reaches stored documents through MCP, as an external agent does; AG-UI carries nothing about storage. |
| Auth | `Bpmn.Mcp.AspNetCore` (claims, tenant from the principal, `IBpmnDocumentAuthorizer`) | The AG-UI endpoint uses ordinary ASP.NET authentication; the agent calls MCP with the caller's identity so tenant and authorizer rules apply unchanged. |

### State

`STATE_DELTA` needs a JSON-addressable model. Raw BPMN XML is not one. Two options, to be decided by a spike:

1. **Snapshot-only first (recommended).** Share a small state: the document key and revision, the selection, validation diagnostics and a summary of the diagram. The agent changes the diagram only by tool calls, so
   the editor remains the one writer of the model, and revisions and undo behave as today.
2. **A JSON projection of the model** (the existing JSON interchange format could serve) with `STATE_DELTA` patches. More faithful and more work; it would let the agent edit "through" state, which
   bypasses the tool boundary, validation and confirmation, so it should not be the default.

### Proposed shape

- A new package, **`Bpmn.AgUi`** (`Hasseware.AspNetCore.Components.Bpmn.AgUi`), referencing Core, `Bpmn.Tools` and the experimental Blazor AI package. Core, the kits and every existing package stay free of the dependency.
- `AddBpmnAgUiTools(...)` registering the `AIFunction` adapters over the page's `IBpmnToolHost`, with `AllowMutations` and confirmation settings mirroring `Bpmn.WebMcp`.
- A small component that places a chat beside an editor (`ChatPage` or the lower-level components) and wires the tools to that editor's session.
- A playground page with a stub agent (a scripted `IChatClient`), so tests and the demo need no model and no key.
- The package targets the .NET 11 preview only while the Blazor package is experimental; the rest of the repository keeps `net10.0`.

### Open questions (decide before building)

- Does the Blazor AI package work in Blazor Server, WebAssembly or both, and does `AGUIChatClient` surface frontend tool calls and `STATE_*` events cleanly to a component? The post does not say.
- Can the existing WebMCP/MCP tool schemas be reused unchanged as `AIFunction` schemas? (Likely yes through the tool-handler abstraction; to verify.)
- The exact wire shape for frontend-tool calls and their results in the current AG-UI spec: verification of one source disagreed with the usual `TOOL_CALL_START/ARGS/END/RESULT` description, so read the spec, not a summary.
- Confirmation: one prompt, which one, and how a refusal reaches the agent.
- Whether a pending proposal can be previewed on the canvas without entering history.
- Multi-target and packaging policy for a preview-only dependency, and what happens when the Blazor package is renamed or moves into the framework.

### Suggested order

1. Spike (scratchpad): a .NET 11 RC1 Blazor app, `AGUIChatClient` against a stub `MapAGUIServer` agent, one frontend tool, one approval, one state snapshot; settle the open questions above.
2. The tool adapter and its tests against `IBpmnToolHost` (no Blazor AI dependency needed to test it).
3. The component, the playground page with the stub agent, and a browser test: ask, see a proposal, accept, undo.
4. Docs and a "Milestone 9 as built" section; reassess against the Blazor AI package's release.

## Open questions

- Decided (checked `src/Bpmn/Editing/BpmnEditHistory.cs`): **saved point needs no history change.**
  `BpmnDocument` is immutable and history stores snapshots, so the binding holds the saved document and
  `IsDirty = !ReferenceEquals(history.Document, savedDocument)`; undoing back to the saved snapshot
  yields the same reference and is clean again. `Reset` (loading another document) re-baselines it.
- Decided: **edit origin is a small additive Core change.** History entries already carry a display
  `Label` (`UndoLabel`/`RedoLabel`) but no author. Add an optional `BpmnEditCommand.Origin` (default
  `User`, plus an agent value carrying the module name), copied into each history entry and exposed as
  `UndoOrigin`/`RedoOrigin`. WebMCP wraps each agent edit in a `CompoundCommand` with the agent origin,
  and `bpmn_undo` refuses unless `UndoOrigin` is the agent's, so an agent can never unwind user work.
  The label stays display-only (still localizable); no code matches on label text. Additive, so
  existing commands and tests are unaffected.
- Decided: `bpmn_open_document` is **not in v1**. Persistence tools are `bpmn_get_document_info` and `bpmn_save` only; the agent works on the document the user opened, and switching documents stays a user action. The option and the tool row are reserved so it can be added later without a breaking change.
- Decided: `RequireConfirmationForPersistence` defaults to `true` and a host **may set it to `false`**
  (sandboxes, demos, kiosks with their own review step). The option's documentation states that it
  lowers a safety control; a save still runs with the user's authorization, is still refused with
  `STALE-DOCUMENT` or `PERSIST-CONFLICT` when the stored document moved, and is still audited. Setting
  it to `false` while `RequireConfirmationForConsequential` is `true` is allowed; the host logs a
  warning at startup naming the relaxed control.
- Decided: `bpmn_create_element` takes **relative placement only** (an anchor element plus a direction
  such as right, below or inside, or just a container). It never accepts coordinates, sizes or bounds.
  A public Core `BpmnPlacement` helper computes the bounds with DI-011 clearance, so there is one
  placement implementation shared by WebMCP and the server-side MCP. If an anchor is missing or no
  free slot exists the tool refuses with the Core diagnostic, and the agent can retry with a different
  anchor. `bpmn_apply_batch` reuses the same placement, resolving anchors in order so a later step may
  anchor on an element created by an earlier one.
- Decided: `AllowMutations` defaults to **`false`** in the first preview; hosts opt in with
  `AllowMutations = true`. The default flips to `true` in the release after `BpmnPart.AgentConfirmation`
  exists and is tested in both kits, and the flip is called out in the release notes. Until then the
  options example in "Public surface" shows `false`.
- Decided: `execute` returns a **plain JSON-safe object**, which the browser serializes. Verified
  against the WebMCP draft IDL (`Promise<any>`, serialized to a JSON string; no content blocks). Earlier
  research votes that results are "strings only" were wrong, and the pre-serialized string in the first
  draft of the JS module was corrected. Server-side MCP is a different protocol and keeps its own result
  conventions through the shared result record.

---

# Server-side MCP module (`Bpmn.Mcp`)

Status: draft, based on the deep-research run of 2026-10-12. Items marked **verify** were not
confirmed by that research and must be checked against the SDK docs before implementation.

## Goal and scope

A headless MCP server over the same document and command model, for CI, scripts, agents without a
browser, and persistent shared use. WebMCP drives the diagram a user has open; this server works on
stored documents with no UI. They complement each other.

## SDK facts (confirmed)

- Official C# SDK: `ModelContextProtocol` (hosting, DI, attribute discovery) and
  `ModelContextProtocol.AspNetCore` (`WithHttpTransport`, `MapMcp`). Stable 2.x: 2.0.0 on
  2026-07-28, 2.2.0 latest as of 2026-08-13. Targets net10.0 and spec 2026-07-28, with fallback
  for 2025-11-25 clients.
- 2.0 breaking changes: HTTP is stateless by default; roots, sampling and logging are `[Obsolete]`.
  The **v2 documentation** (csharp.sdk.modelcontextprotocol.io, checked 2026-10) names the setting
  `SessionMode` with `HttpServerSessionMode.Stateless` or `HttpServerSessionMode.Stateful` on
  `HttpServerTransportOptions`; the v1 documentation used a boolean `Stateless`. Use `SessionMode`
  with the 2.x SDK and pin the version, since the setting was renamed between major lines.
- Registration in the 2.x docs is `AddMcpServer().WithTools<TTools>()` with `[McpServerToolType]` /
  `[McpServerTool]`; the pages checked do not mention `WithToolsFromAssembly`, so use
  `WithTools<T>()` and verify the assembly-scan overload before relying on it.
- Structured output: `UseStructuredContent = true` on `[McpServerTool]` (or `McpServerToolCreateOptions`)
  advertises an output schema and puts the serialized return value in `CallToolResult.StructuredContent`.
  A plain `string` return is wrapped in a `TextContentBlock`; tools may also return content blocks.
- In-process testing: `StreamServerTransport` and `StreamClientTransport` over a pair of
  `System.IO.Pipelines` `Pipe`s, recommended by the docs for tests.
- **Not found in the pages checked (still verify):** the tool-annotation property names (read-only,
  destructive, idempotent, open-world, title) on `McpServerToolAttribute`, and any dedicated
  authorization guidance beyond "HTTP auth (OAuth, headers)" for HTTP transports.
- stdio for local, CI and child-process use (logs to stderr only). Streamable HTTP for hosted use.
  Legacy SSE is off by default and obsolete; do not use it.
- Pin the SDK version in `Directory.Packages.props` and re-read the release notes on each bump.

## Projects

| Project | Role |
|---|---|
| `src/Bpmn.Storage.FileSystem` | File store. Refs `Bpmn` only. Used by the stdio tool and optionally by Blazor Server hosts |
| `src/Bpmn.Mcp` → `Hasseware.AspNetCore.Components.Bpmn.Mcp` | Plain class library (not Razor): tool classes, `AddBpmnMcp`, optional authorizer. Uses Core's `IBpmnDocumentStore`. Refs `Bpmn` and `ModelContextProtocol` |
| `src/Bpmn.Mcp.Tool` → dotnet tool `bpmn-mcp` | stdio host (`PackAsTool`, `ToolCommandName=bpmn-mcp`). Refs `Bpmn.Mcp` |
| `src/Bpmn.Mcp.AspNetCore` | Optional HTTP host: `MapBpmnMcp()`, auth wiring. Refs `Bpmn.Mcp` and `ModelContextProtocol.AspNetCore` |
| `tests/Bpmn.Mcp.Tests` | In-process client/server tests |

The HTTP host is split out so CLI and CI users do not take an ASP.NET dependency. No Blazor or JS
dependency, so it runs in a plain container. Add the projects to `BlazorBpmnEditor.slnx` under
`/Components/`.

## Shared handlers with WebMCP

The browser module and this server must not each re-implement tools.

```csharp
public interface IBpmnToolHost
{
    ValueTask<BpmnDocument> GetAsync(string documentId, CancellationToken ct);
    ValueTask<BpmnEditResult> ApplyAsync(string documentId, BpmnEditCommand command, CancellationToken ct);
}
```

- WebMCP adapter wraps `BpmnEditorSession` (one implicit document, UI-visible, undoable).
- MCP adapter wraps an `IBpmnDocumentStore` (below), with a `BpmnEditHistory` per open document.
- Tool handlers (validate, create, connect, delete, export, describe) take `IBpmnToolHost` and JSON
  arguments and return the shared result record (`ok`, `applied`, `createdIds`, `diagnostics`,
  `revision`). WebMCP wraps them in `registerTool`; this package wraps them in `[McpServerTool]`.
- Keep the abstraction `internal` (with `InternalsVisibleTo`) until a second consumer exists, or
  build the handlers in the MCP package first and have WebMCP reference them.

## Server setup

```csharp
// stdio host
builder.Services
    .AddBpmnComponents()                       // Core model services, no UI
    .AddBpmnMcp(o => { o.Root = "./diagrams"; o.AllowWrites = false; })
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<BpmnMcpTools>();

// HTTP host
builder.Services.AddMcpServer()
    .WithHttpTransport(o => o.SessionMode = HttpServerSessionMode.Stateless)   // set explicitly, per the 2.x docs
    .WithTools<BpmnMcpTools>();
app.MapMcp("/mcp").RequireAuthorization();
```

Tools are `[McpServerToolType]` classes with `[McpServerTool]` methods; services are injected by
constructor or method parameter. Most identifiers here are confirmed against the 2.x docs (see "SDK
facts"); the remaining ones are flagged **verify**, as is whether `AddBpmnComponents()` works without Razor services (the scripting
modules reference only Core, so it should).

## State model: stateless, store-backed

Stateless HTTP and stdio-per-process mean tools cannot rely on an in-memory session.

- Every tool takes a `documentId`, a hierarchical `BpmnDocumentPath` such as `claims/2026/intake.bpmn`
  (see "Hierarchical ids" at the top). No hidden "current document". The id is a *logical* path inside
  the caller's tenant: no tool ever sees a file-system path, container name, connection string or SQL
  key. How a logical path maps to storage is the store's business, so the same tools work whether
  documents live on disk, in blob storage or in a database.
- `IBpmnDocumentStore` is the only persistence seam, and it is **defined in Core** (see "Shared
  prerequisite" at the top), not in this package; the Blazor editor uses the same interface. It deals
  in content and revisions, not paths:

```csharp
public interface IBpmnDocumentStore
{
    ValueTask<BpmnStoredDocument?> OpenAsync(BpmnDocumentKey key, CancellationToken ct);
    ValueTask<BpmnDocumentStat?> StatAsync(BpmnDocumentKey key, CancellationToken ct);   // revision, size, modified; no content
    ValueTask<BpmnSaveResult> SaveAsync(BpmnDocumentKey key, BpmnDocumentContent content,
        string? ifMatchRevision, CancellationToken ct);       // null = create only if absent
    ValueTask<BpmnDocumentPage> ListAsync(BpmnDocumentQuery query, CancellationToken ct); // hierarchical, paged
    ValueTask<bool> DeleteAsync(BpmnDocumentKey key, string? ifMatchRevision, CancellationToken ct);
}

public sealed record BpmnDocumentStat(string Revision, long Size, DateTimeOffset Modified);
public readonly record struct BpmnDocumentKey(string? Tenant, BpmnDocumentPath Path);   // Tenant from the principal, never from a tool argument
public sealed record BpmnStoredDocument(BpmnDocumentContent Content, string Revision, DateTimeOffset Modified);
public sealed record BpmnDocumentContent(ReadOnlyMemory<byte> Bytes, BpmnDocumentFormat Format); // xml | json
```

  The store returns raw bytes plus a format; parsing stays in the handlers (`BpmnFileFormats.Read`),
  so every backend is a thin byte store and import diagnostics behave identically everywhere.
  Where a database has no byte column, it stores the XML/JSON text; the contract is unchanged.
- **Implementations.** In Core: `InMemoryBpmnDocumentStore` (tests, demos). In the small
  `Bpmn.Storage.FileSystem` package: `FileBpmnDocumentStore` (default for the stdio tool and CI; also
  usable by a Blazor Server host). Backends that pull in cloud or database packages ship as separate
  optional packages, which depend on Core only, so neither Core nor `Bpmn.Mcp` takes those dependencies
  (Core's purity is enforced by an architecture test):

| Package | Backend | Revision maps to |
|---|---|---|
| `Bpmn.Mcp.Storage.AzureBlob` | Azure Blob Storage | Blob ETag, with `If-Match` / `If-None-Match: *` |
| `Bpmn.Mcp.Storage.S3` (later) | S3-compatible | ETag, conditional writes |
| `Bpmn.Mcp.Storage.EfCore` | Any EF Core database | Row-version / concurrency token |
| host-supplied | Anything else | Whatever the host's optimistic-concurrency primitive is |

  Hosts that already own storage implement the interface (a few methods) and register it with
  `services.AddBpmnDocumentStore<TStore>()` (the same registration the editor uses). Package names and the exact SDK choices are a proposal.
- **Concurrency:** optimistic and delegated to the backend. Reads return an opaque `revision`
  (treat as a string; it is a blob ETag, a rowversion, a content hash or a file timestamp+length hash);
  writes pass `ifMatchRevision`, and the store must make the compare-and-write atomic using the
  backend's native conditional primitive (conditional blob headers, `UPDATE ... WHERE RowVersion = @v`,
  etc.), never read-then-write in application code. A mismatch returns
  `{ ok:false, code:"MCP-CONFLICT" }` with the current revision, so two agents cannot silently
  overwrite each other. The store is the source of truth, so the server scales out. A backend that
  cannot offer atomic conditional writes must say so in its docs and not claim concurrency safety.
- **Revision vs `revision` in results.** The WebMCP result's `revision` is a per-session counter;
  here it is the store's opaque revision. Name them the same in the shared result record but
  document that agents must treat both as opaque tokens, not numbers.
- **Limits before bytes.** Stores should expose size in metadata so oversize documents are refused
  before a large blob or row is downloaded (`MaxXmlBytes`). Stream where the backend allows;
  otherwise cap on read.
- **Latency and retries.** Remote stores fail transiently. Retries belong in the store (use the
  backend client's built-in policy), with the tool call's `CancellationToken` honoured. A tool must
  not retry a conditional write blindly; a retried save that already succeeded would report a false
  conflict. Surface that ambiguity as `MCP-STORE-UNAVAILABLE` and let the agent re-open.
- **Undo** has no stateless equivalent, so v1 has no undo tool. Optional later: a bounded snapshot
  history per document in the store (the editor already keeps immutable snapshots).
- **Multi-step edits:** `bpmn_apply_batch` takes an ordered operation list, applies it as one
  `CompoundCommand` and does one save, replacing the need for a session across calls.
- Stateful mode (sessions, server-to-client notifications) is out of scope for v1; if added, enable it
  explicitly and document the session-affinity requirement. This server is nevertheless the usual
  *source* of changes that editors observe: a save through `bpmn_*` bumps the store revision, and any
  open editor learns of it through the change bus and its transport (see "External change
  notification" at the top).
- `StatAsync` serves "did it change?" checks and size limits without downloading the body; MCP tools
  use it for `If-Match` pre-checks and for oversize refusal.

## Tool catalog

Same names and result shape as WebMCP where they overlap.

| Tool | Annotations | Notes |
|---|---|---|
| `bpmn_list_documents` | readOnly, untrusted | Args: `folder` (default root), `recursive` (default false), `namePrefix`, `cursor`, `limit`. Returns entries (documents and virtual sub-folders) with path, size, modified time. Names are user text. Never enumerates unbounded sets, and only what the caller's tenant and authorised prefixes may see |
| `bpmn_open` | readOnly | Summary + etag via `BpmnFileFormats.Read` (XML or JSON). Surfaces import diagnostics ("Changed" / "Not kept") so the agent knows the reader normalized something |
| `bpmn_validate` | readOnly | Validation report; includes script validators registered via `AddBpmn*Scripting` |
| `bpmn_describe_element` | readOnly | Snapshot with diagnostics |
| `bpmn_diff` | readOnly, untrusted | Minimal id-based structural diff of two stored documents (added/removed/changed elements, connector endpoints, `geometryChanged` flag). Lives in `Bpmn.Tools`, no Core change; no move detection, no revision history. Useful in CI review |
| `bpmn_create_element` | mutating | Relative placement via the shared `BpmnPlacement` helper |
| `bpmn_connect` | mutating | `CreateConnectorCommand` |
| `bpmn_set_property` | mutating, idempotent | `UpdatePropertiesCommand` |
| `bpmn_apply_batch` | mutating | One compound command, one save, one etag bump |
| `bpmn_delete` | destructive | `DeleteElementsCommand`; refusals such as `EDIT-022` pass through |
| `bpmn_create_document` | mutating | Blank or from template |
| `bpmn_export` | readOnly | `xml` or `json`; returns text. Writing the result elsewhere is a separate, store-mediated `bpmn_save_as` (new logical path, create-only; folders created implicitly), never an arbitrary destination path or URL |

Set read-only, destructive and idempotent annotations through the SDK (**verify** property names).
Return the shared typed result record with `UseStructuredContent = true` so clients get an output
schema and `StructuredContent`, plus a short text summary block (confirmed available in 2.x; the exact
combination of structured content and extra text blocks is **verify**). Resources (`bpmn://documents/{id}`) and a
`review_diagram` prompt are possible later additions (**verify** support).

## Security

The research did not confirm SDK-specific auth or hardening guidance. This is design intent; check
the SDK's authorization docs when implementing (**verify**).

**All stores**
- Document ids are `BpmnDocumentPath` values, parsed by the one Core parser (segment rules, length and
  depth caps, ordinal case-sensitive, no `.`/`..`, no backslash, no device names). Tool arguments are
  parsed before any store call, so a malformed path is rejected as `INVALID-PATH` and never reaches
  a backend. Tools never concatenate ids into file paths, blob names, URLs or SQL; backends use
  parameters, SDK blob-name APIs and key lookups only, and add the tenant prefix themselves.
- Listing is bounded (`Limit` cap, paged), returns no content, and is scoped to the caller's tenant and
  authorised prefixes.
- Cap document size and element count (reuse `MaxXmlBytes`, `MaxElementsPerCall`) for every backend.
- Writes are off by default (`AllowWrites=false`); the CLI needs an explicit `--allow-writes`.
- Store credentials (connection strings, SAS tokens, managed identity) live in host configuration
  and are never returned by a tool or written to logs. Prefer managed identity or a least-privilege
  principal scoped to one container or schema; give read-only deployments a read-only credential.
- Tenant isolation is enforced by the store from `BpmnDocumentKey.Tenant`, not by tool code.

**File store**
- Map the logical path to `Root/<tenant>/<segments>`, then, as defense in depth beyond
  `BpmnDocumentPath`, resolve with `Path.GetFullPath` and require the result to start with the root
  plus a separator. Reject symlinks or junctions whose resolved target leaves the root. Allow only
  `.bpmn`, `.xml`, `.json`. Detect case-only collisions (the platform may be case-insensitive) and
  refuse them. Create directories on save; do not follow reparse points while enumerating.
- Write to a temp file and atomically replace, so a crash leaves the old file. Revision is a hash of
  length plus last-write time (or content hash), compared under a file lock.

**Blob store**
- Container is fixed by configuration; the blob name is the configured prefix, the tenant, then the
  logical path (`/` is the native delimiter, so folder listing uses the service's hierarchical
  listing). `BpmnDocumentPath` already excludes `..` and leading slashes.
- Use conditional headers for create-only and update. Treat soft-delete and versioning as the
  backend's recovery story, and document that `bpmn_delete` is not undo.

**Database store**
- Parameterized queries only; the table and schema are configuration, never tool input. The
  tenant column is part of every `WHERE`. Concurrency token on every update.
- Large XML belongs in a text/blob column with size recorded alongside, so listing never loads content.
- Path column is normalized, uniquely indexed per tenant, with an ordinal (binary) collation so case
  sensitivity matches the contract. Folder queries use an ordinal prefix **range**, not `LIKE`
  with user text, so `%` and `_` in names cannot widen a query.

**Untrusted input**
- BPMN XML is attacker-controlled. Confirm `BpmnXmlReader` disables DTDs and external entities and
  limits depth and size; add billion-laughs and external-entity fixtures.
- Prompt injection: the same `untrustedText` convention as WebMCP. Names, documentation and
  imported text never appear in `message`.
- Script Tasks are data. The server never executes scripts; validation is syntax checking only.

**HTTP**
- Require authentication. Bind to loopback by default and refuse non-TLS elsewhere.
- Use the SDK's and ASP.NET's bearer/OAuth support, not custom code. The MCP authorization model
  builds on OAuth 2.1; the exact SDK wiring is **verify**.
- Per-user isolation: store key is `(tenant, documentId)` with `tenant` from the validated
  principal (a user id, organization claim or similar), never from a tool argument. The store, not
  the tool, applies it.
- Authorization is per document, not just per caller. Add an optional `IBpmnDocumentAuthorizer`
  (`CanRead`, `CanWrite`, `CanDelete` for a key and principal) for hosts whose documents have
  owners or ACLs; default is tenant-scoped full access.
- Rate limit and cap request size. Validate `Origin` and `Host` on local HTTP servers (DNS rebinding).
- Audit log: tool, user, document id, etag before and after. Never document contents.

## Testing and CI

- In-process: connect an SDK client and server with `StreamClientTransport` and `StreamServerTransport`
  over `System.IO.Pipelines` pipes (the docs' recommended test setup) and call every tool against
  `InMemoryBpmnDocumentStore`.
- Store contract tests: one reusable test suite (a base class or shared test project) that every
  `IBpmnDocumentStore` runs: create-only collision, update with correct and stale revision, concurrent
  writers (exactly one wins), missing document, paging stability, oversize refusal, tenant isolation,
  hostile ids, **hierarchy** (nested save/open, implicit folders, non-recursive listing with virtual
  folders, recursive listing, ordering and cursor stability while documents change, case-only
  collisions, a path fuzz corpus shared with the `BpmnDocumentPath` unit tests), and **change
  signals** (a save by another client is reported with the new revision; the writer's own save is not
  echoed back as foreign; delete is reported; subscription disposal stops delivery; the decorator
  publishes only after successful writes and never for a rejected conflict; duplicate and out-of-order
  deliveries leave the binding consistent; `Resubscribed` triggers exactly one catch-up check). Each
  transport package also runs a transport suite: authentication required, tenant taken from the
  principal, forbidden paths refused without revealing existence, subscription caps, and clean disposal on
  disconnect. Run it against in-process, SignalR and SSE adapters (in-memory test servers). Run it against the in-memory and file stores in unit tests, and against the blob and
  EF Core stores using emulators or containers (Azurite, SQLite/SQL Server container) in CI.
- A fault-injecting store wrapper (timeouts, transient errors, duplicate-success) to test the
  `MCP-STORE-UNAVAILABLE` path.
- Fixtures: round-trip the repo's sample diagrams; golden tests for `bpmn_diff` and `bpmn_validate`.
- Security tests: traversal (`..`, absolute, symlink, UNC), oversize, XXE, stale-etag conflict.
- Process smoke test: launch `bpmn-mcp` over stdio, list tools, validate a file, assert only
  protocol frames reach stdout.
- CI recipe for hosts: install the tool, then validate each changed `.bpmn` and fail on error
  diagnostics. Also ship a plain `bpmn-mcp validate <path>` verb so pipelines need no MCP client.
- Contract snapshot of the descriptors (names, schemas, annotations), as for WebMCP.

## Packaging and versioning

- Package IDs follow `Hasseware.AspNetCore.Components.Bpmn.<Feature>`; the tool command is `bpmn-mcp`.
- The `bpmn-mcp` tool selects its store by configuration (`--store file --root ./diagrams`, or an
  `appsettings`/environment section naming a store type and settings). Cloud and database stores are
  loaded only when their package is referenced by a custom host; the stock global tool ships the
  file store only, so installing it never drags in Azure or database dependencies.
- `IBpmnDocumentStore` and its records are now part of **Core's** public API, so they follow Core's
  versioning. Third-party stores and every consumer depend on them: change them only additively
  (new optional members with default implementations, new records), and add them to Core's public
  API baseline if one exists.
- The reusable store contract test suite ships as an optional `Bpmn.Storage.Testing` package (abstract
  base class) so third-party stores can verify themselves; it is a test-only dependency and never
  referenced by Core.
- Version independently of Core but declare a Core range, since tools depend on command types.
- State the minimum client protocol version and the pinned SDK version in the README.
- Ship the tool framework-dependent. Do not promise trimming or AOT until the SDK's attribute
  discovery and the Core model are verified trim-safe (**verify**).

## Implementation order

1. Extract `IBpmnToolHost`, the shared handlers and the result record; point the WebMCP design at them.
2. `Bpmn.Storage.FileSystem` (passing the shared store contract suite), then `Bpmn.Mcp` over Core's `IBpmnDocumentStore` with read-only tools (`open`, `validate`, `describe`, `export`, `list`), plus path-safety tests. Core's store contract is step 0 of the WebMCP implementation order above and is a prerequisite here.
3. `bpmn-mcp` stdio tool and the `validate` verb; add the CI recipe to the docs.
4. Mutating tools, `apply_batch` and etag concurrency; the minimal id-based diff in `Bpmn.Tools` and `bpmn_diff`.
5. HTTP host package with auth and per-tenant store keys; then `Storage.AzureBlob` and `Storage.EfCore` packages passing the store contract suite.
6. Resources and prompts if there is demand.

## Open questions

- Decided: the shared tool handlers, the shared result record, the diagnostic and error codes and the
  `untrustedText` helpers live in a new plain library, **`src/Bpmn.Tools`**
  (`Hasseware.AspNetCore.Components.Bpmn.Tools`, refs `Bpmn` only, no Razor, no SDK dependency).
  `Bpmn.WebMcp` and `Bpmn.Mcp` both reference it; neither references the other. `IBpmnToolHost` is
  defined there and made public once the second consumer lands (step 1 of the server-side order). It
  must stay free of Blazor component and MCP SDK dependencies, which the architecture test for Core's
  purity is extended to cover. Add the project to `BlazorBpmnEditor.slnx` under `/Components/`.
- Decided: `bpmn_diff` ships in v1 as a **minimal, id-based structural diff inside `Bpmn.Tools`**, not
  a Core feature (no Core API commitment). It compares two stored documents by path (or the current
  document against a path) and reports: added and removed elements (id, type, name), changed elements
  (id plus the names of changed scalar properties with bounded old and new values), changed connector
  endpoints, and a per-element `geometryChanged` flag. It does **not** detect moves or renames across
  ids, does not report coordinate deltas, and compares no revision history, because stores keep none and
  revisions are opaque. Output is bounded by `MaxElementsPerCall` with a `truncated` flag, and names and
  values go in `untrustedText`. A geometry-aware semantic diff in Core is a later feature if usage
  shows it is needed; the canonical-XML export remains available for a plain text diff in CI.
- Decided: the Blazor host's load/save uses `IBpmnDocumentStore`, and the interface lives in Core, free
  of cloud and database dependencies.
- Decided: the editor learns when another party changes the document it has open, through the store's
  push notification (no polling) behind an extensible bus/subscriber abstraction, with in-process,
  SignalR and SSE transports, and the user chooses how to resolve it.
- Decided: document ids are hierarchical (`BpmnDocumentPath`), with implicit folders and listing by
  folder.
- `revision` is an opaque string in both modules (decided).
- Decided: `ExternalChangePolicy` ships `Notify` (default), `AutoReloadWhenClean` and `Ignore`. The
  default stays `Notify`, so no document is replaced without a click unless the host opts in.
  `AutoReloadWhenClean` always shows a visible "reloaded" status, clears undo history, and never fires
  while `IsDirty` or while a label edit or drag is in progress.
- Decided: option C for writers outside any decorated store: the ingress extension point plus reference
  samples for blob/Event Grid and PostgreSQL `LISTEN`/`NOTIFY`; no first-party ingress packages in v1.
  Promotion trigger (decided): a blob/Event Grid ingress becomes a supported package once two or more
  real deployments report routine external writers (ETL jobs, a second application, manual uploads).
- Decided: no event replay. Long sessions use state-based catch-up plus keepalive, reconnect and expiry
  handling; transport-native resume is an optional optimisation.
- Decided: keepalive 30 s, staleness 2.5 intervals (75 s), both configurable per adapter. Rationale
  below; the proxy and load-balancer timeouts cited are general knowledge, so treat them as **verify**
  against the deployment's actual infrastructure.
- Decided: subscriptions are per document only, with no folder or wildcard subscription.
- Decided: `Bpmn.Storage.ServerSentEvents` is viable on .NET 10 and uses no JS. Server: ASP.NET Core
  10 minimal APIs return an `IAsyncEnumerable<SseItem<T>>` through the built-in SSE result (sources
  spell it `Results.ServerSentEvents` and `TypedResults.ServerSentEvents`; check which exists), with
  `Last-Event-ID` available as a request header. Client: `HttpClient` with `ResponseHeadersRead` and
  `System.Net.ServerSentEvents` `SseParser` over the response stream. Blazor WebAssembly enables
  `HttpClient` response streaming by default from .NET 10 (before that it was opt-in per request), so
  the stream reads incrementally. Unlike the browser's `EventSource`, `HttpClient` can send an
  `Authorization` header, which suits bearer-token hosts. **Not confirmed:** that `SseParser` itself
  runs correctly on the WebAssembly runtime (no source covered it). Acceptance test: a Playwright
  E2E that receives a heartbeat and a change event in a real WebAssembly client, before the package
  is called supported. Streaming from `BrowserHttpReadStream` has no synchronous reads, which
  `SseParser`'s async enumeration should respect.
- Decided: `Binding.CheckForChangesAsync()` is left to the host; the kits add no focus handling.
- Decided: the Event Grid and blob change feed facts in the ingress comparison are now verified against
  Microsoft Learn (see that section). The database signals (PostgreSQL `LISTEN`/`NOTIFY`, SQL Server
  options) are only partly verified and remain **verify** for the PostgreSQL reference sample.
- Decided: the path segment character set is the portable Unicode subset as drafted (Unicode letters and
  digits after NFC normalization, space, and `- _ . ( )`), with the exclusions listed under
  "Hierarchical ids". It is a Core contract: it may be widened later without breaking stores, but never
  narrowed, so every backend must be able to store everything it admits. The fuzz corpus includes
  confusables, right-to-left marks, zero-width characters and mixed normalization forms, which the parser
  must reject or normalize deterministically (to be specified with the parser tests).
- Decided: no `MoveAsync` in v1. Rename or move is `SaveAsAsync` to the new path followed by
  `DeleteAsync` of the old one; it is not atomic, and a failure between the two can leave both
  documents, which the host or agent must handle (the tool result says so). A `MoveAsync` can be added
  later as an optional interface member with a default implementation, without breaking existing
  stores. Blob storage has no atomic rename, so v1 does not promise one.
- Decided (checked against `BpmnEditorSession`, `BpmnEditorScope`, `BpmnEditHistory`): Core's public
  additions are `Storage` types, `BpmnDocumentBinding` with its factory, `BpmnEditCommand.Origin` and
  `UndoOrigin`/`RedoOrigin`, and `BpmnPlacement`. **No new public members on `BpmnEditorSession`**:
  its settable members are `internal` by convention and it stays free of storage and JS. Components
  keep the code-behind convention, `BpmnDocumentBinding` is a plain sealed class, new user-visible
  strings go through the resx and XLIFF resources, and the repo has no public-API baseline files, so
  none needs updating (add one if the project later adopts a baseline).
- Decided: the SDK identifiers were checked against the 2.x documentation (see "SDK facts"); see
  there for what is confirmed and what remains **verify**. The remaining items are small and are
  resolved by reading the API reference at implementation time (tool annotation property names, the
  assembly-scan overload, OAuth wiring, `AddBpmnComponents()` without Razor services, trimming). None
  blocks the design.
