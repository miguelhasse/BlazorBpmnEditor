# Document storage

How an editor loads and saves stored documents, and how it learns that someone else changed one.

Storage is opt-in. `AddBpmnComponents()` registers nothing here, and an editor given a `Document`
works exactly as before. Hosts that keep documents somewhere — a file share, a blob container, a
database — register one store, and the editor, agent tools and the host's own code all use it.

## Register a store

```csharp
builder.Services.AddBpmnEditor();                 // or AddFluentBpmnEditor()
builder.Services.AddBpmnDocumentStore<MyStore>(); // or AddBpmnInMemoryDocumentStore()
builder.Services.AddBpmnDocumentChanges();        // optional: hear about other writers
```

`MyStore` implements `IBpmnDocumentStore`: open, stat, conditional save, paged listing and
conditional delete, in bytes and opaque revisions. Core ships `InMemoryBpmnDocumentStore` for tests and
demos; stores backed by cloud or database services live in optional packages, never in Core.

Registration order does not matter. When a change bus is registered, the store is wrapped so every
successful write through it is announced; the wrapper is decided when the store is resolved and has the
store's own lifetime. Use `ServiceLifetime.Scoped` for a store that holds a per-request resource such as
a database context.

## Paths

Documents are named by `BpmnDocumentPath`, a logical, hierarchical path such as
`claims/2026/intake.bpmn` — never a file-system path, URL or database key. A `BpmnDocumentKey` pairs a
path with an optional tenant; the tenant must come from the authenticated user or the host's
configuration, never from text a client supplied.

- Segments are separated by `/` and hold Unicode letters, combining marks and digits, plus space and
  `- _ . ( )`, after NFC normalization. Empty, `.` and `..` segments, backslashes, control and format
  characters, a leading or trailing space, a trailing dot and Windows device names are refused.
- Paths compare ordinal and case-sensitive. A store on case-insensitive media refuses a name that
  differs from an existing one only by case rather than overwriting it.
- A document's name ends in `.bpmn` or `.xml` (BPMN XML) or `.json` (the JSON interchange format).
  Folders are implicit: one exists while any document lies beneath it.
- Limits: 512 characters, 16 segments, 128 characters per segment. They are constants, because a path
  that one host accepts must be storable by every store.

Hosts running with invariant globalization cannot normalize non-ASCII text; such paths are refused with
`NormalizationUnavailable` rather than stored unnormalized.

## Bind an editor to a stored document

A `BpmnDocumentBinding` ties one editor to one stored document. The host creates it, passes it to the
editor instead of `Document`, and disposes it:

```razor
<BpmnEditor DocumentBinding="@_binding" OnDocumentChanged="@OnChanged" />

@code {
    [Inject] private IBpmnDocumentBindingFactory Bindings { get; init; } = default!;
    private BpmnDocumentBinding _binding = default!;

    protected override Task OnInitializedAsync()
    {
        _binding = Bindings.Create();
        return _binding.LoadAsync(new BpmnDocumentKey(null, BpmnDocumentPath.Parse("claims/intake.bpmn")));
    }

    private async Task SaveAsync()
    {
        var result = await _binding.SaveAsync();
        // Conflict or NotFound: someone else changed or deleted it. Nothing was overwritten.
    }

    public ValueTask DisposeAsync() => _binding.DisposeAsync();
}
```

- `IsDirty` follows every edit, undo and redo: undoing back to the saved state is clean again.
- `SaveAsync` checks the stored revision first and writes only if it is still the one loaded. A conflict
  is a result, never an exception and never an overwrite. `SaveAsAsync(key)` writes a new document and
  never replaces an existing one.
- `ReloadAsync` takes the stored version and starts a fresh undo history.
- Accepting a file import with a binding replaces the bound document's contents: the key is kept and the
  editor becomes dirty.
- One binding serves one editor at a time. An editor given a binding that another editor holds takes it
  over (so swapping one editor for another in a single render works), and the other stops reporting to
  it. Setting both `Document` and `DocumentBinding` is an error.
- Saves run one at a time: a second Save waits for the first. Edits made while a save is in flight stay
  unsaved, and the editor stays dirty, rather than being rolled back to what was written.
- `OnDocumentChanged` still fires on every edit.

The binding's public members run on the renderer's context, like any component API.

## Changes made by someone else

With `AddBpmnDocumentChanges()`, an open editor hears about saves and deletes made through the
registered store by any other party: another user, another tab, an agent. The editor shows the
`ExternalChangeOffer` part — a notice over the diagram, not a dialog — with:

| Change | Actions |
|---|---|
| Updated | **Reload** (asks first when there are unsaved edits), **Save a copy** as `name (copy).bpmn`, **Dismiss** |
| Deleted | **Save again** where it was, **Dismiss** |

Dismiss hides the notice but keeps the conflict: the next save still refuses.

`StorageOptions.ExternalChangePolicy` (in `BpmnOptions.Storage`) chooses the behaviour:

| Policy | Behaviour |
|---|---|
| `Notify` (default) | Show the notice; change nothing until the user chooses |
| `AutoReloadWhenClean` | Reload silently when there are no unsaved edits and no label is being edited; otherwise notify |
| `Ignore` | Say nothing; the next save reports the conflict |

There is no polling. Notifications are pushed through `IBpmnDocumentChangeBus` (server side) and
`IBpmnDocumentChangeSubscriber` (what the binding listens to). Core's in-process bus serves Blazor
Server on one instance and the playground; SignalR and Server-Sent Events transports, and buses that
span instances, are separate packages. Delivery is best effort: the binding makes one revision check
after subscribing and after any gap in delivery, and every save checks again, so a missed notification
can delay the warning but never cause an overwrite.

Writes that bypass the registered store — a hand-edited database row, a blob uploaded in a portal — are
not announced. They are found by the binding's checks, at the latest when saving. Hosts that know of
other moments worth checking, such as the browser tab regaining focus, call
`binding.CheckForChangesAsync()`:

```razor
@* With a small JS module that invokes OnFocus when document.visibilityState becomes "visible". *@
[JSInvokable] public Task OnFocus() => _binding.CheckForChangesAsync();
```

## Custom layouts

In a custom composition, render the part inside the canvas, so the notice floats over the diagram
without covering the toolbar:

```razor
<BpmnEditorCanvas>
    <BpmnPart Part="BpmnEditorPart.ExternalChangeOffer" />
</BpmnEditorCanvas>
```

A kit's own implementation derives from `BpmnExternalChangeOfferBase`, which supplies the state and the
Reload, Save a copy, Save again and Dismiss actions.

## Design

The reasoning behind these choices, and how WebMCP and MCP tools build on the same store, is in
[webmcp-design.md](webmcp-design.md).
