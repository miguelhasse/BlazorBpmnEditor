# Offering the editor to browser agents (WebMCP, preview)

`Hasseware.AspNetCore.Components.Bpmn.WebMcp` lets an AI agent running in the browser look at the diagram a
user has open, through [WebMCP](https://webmachinelearning.github.io/webmcp/). Out of the box it is **read-only**:
an agent can list, describe, validate and export. A host that wants agents to edit says so with
`AllowMutations`, and even then deleting and saving ask the user first. The design is in
[webmcp-design.md](webmcp-design.md); this page is what ships.

## Use it

```csharp
builder.Services.AddBpmnComponents().AddBpmnWebMcp();   // or AddBpmnEditor() / AddFluentBpmnEditor()
```

Put `<BpmnWebMcp />` anywhere inside the editor's scope. Both ready-made editors take it in their `Dialogs` slot,
which is inside the scope and draws nothing of its own:

```razor
<BpmnEditor Document="@document">
    <Dialogs><BpmnWebMcp /></Dialogs>
</BpmnEditor>
```

Registration happens after the first render in the browser, never while prerendering, and the tools are withdrawn
when the component is disposed. Where the browser has no WebMCP, nothing is registered, nothing is logged above
debug level, `Status` is `Unsupported` and `OnUnavailable` is raised.

| Member | Meaning |
|---|---|
| `ToolPrefix` | Replaces `bpmn_` on every tool name (the option sets the default) |
| `AllowMutations` | Overrides the option for this editor: whether the editing tools are offered. Read once, at registration |
| `InstanceName` | Tells editors on one page apart: `bpmn_get_diagram_left`. An unnamed second one gets `_2`, and a warning is logged |
| `OnRegistered`, `OnUnavailable` | Raised once the tools are registered, or will not be |
| `Status`, `RegisteredTools` | Where registration stands, and under which names |

Options (`AddBpmnWebMcp(options => …)`): `ToolPrefix`, `DisabledTools` (by catalog name, such as `bpmn_export`),
`MaxElementsPerCall` (200) and `MaxXmlBytes` (1,000,000), both of which must be at least 1, and:

| Option | Default | Meaning |
|---|---|---|
| `AllowMutations` | `false` | Offer the tools that change the diagram. Off, none of them is even registered, so an agent cannot call what it cannot see |
| `RequireConfirmationForConsequential` | `true` | Ask the user before `bpmn_delete` |
| `RequireConfirmationForPersistence` | `true` | Ask the user before `bpmn_save`. Separate from the above, because saving leaves the browser session |

## What an agent can call

| Tool | Returns |
|---|---|
| `bpmn_get_diagram` | Diagrams, elements (id, type, name, container, pool, bounds) and connectors, a page at a time (`cursor`, `limit`) |
| `bpmn_describe_element` | One element's properties and current values, references, findings and what the editor would let the user do |
| `bpmn_get_selection` | What the user has selected |
| `bpmn_validate` | The editor's validation with rule ids |
| `bpmn_export` | BPMN 2.0 XML or the JSON interchange format, refused above `MaxXmlBytes` |
| `bpmn_list_creatable` | What the palette offers in a container, and why anything is unavailable |

With `AllowMutations`, and for an editor bound to a stored document (`DocumentBinding`) for the last two:

| Tool | Does |
|---|---|
| `bpmn_create_element` | A Flow Node beside an anchor or inside a container, placed automatically; optionally connected from the anchor |
| `bpmn_connect` | A connector, refused when BPMN forbids it |
| `bpmn_set_property` | One standard property |
| `bpmn_delete` | Elements and what cannot outlive them, all listed. **Asks the user first** |
| `bpmn_select` | Selects elements so the user sees what the agent means; changes no content |
| `bpmn_undo`, `bpmn_redo` | Steps through the agent's **own** edits; refused when the next step is the user's |
| `bpmn_get_document_info` | Stored name, stored revision, `isDirty`, and `externalChange` (`none`, `updated`, `deleted`) |
| `bpmn_save` | Saves to the stored document. **Asks the user first**; `PERSIST-CONFLICT` when the stored copy moved |

The tools are those of the shared [`Bpmn.Tools`](../src/Bpmn.Tools/README.md) catalog, so a server-side agent
surface lists the same names. No tool takes a file path, a URL or a connection string.

## Results

`execute` returns a **plain object**, never a JSON string: the browser serializes the fulfilled value itself, and a
string would arrive double-encoded. A tool **never rejects**, because a rejection reaches the agent as an opaque
`DOMException`; a failure is an ordinary result:

```json
{ "ok": true, "applied": false, "diagnostics": [], "revision": "r3", "data": { … } }
{ "ok": false, "code": "NOT-FOUND", "message": "There is no element with that id.", "revision": "r3" }
```

Codes are the editor's own (`EDIT-…`, `DI-011`) or one of `INVALID-ARGUMENT`, `NOT-FOUND`, `TOO-LARGE`, `NO-DOCUMENT`,
`READONLY`, `REFUSED`, `ILLEGAL-CONNECTION`, `DENIED`, `BUSY`, `STALE-DOCUMENT`, `PERSIST-CONFLICT`, `INTERNAL-ERROR` and
`CANCELLED`. A result from an editor bound to a stored document also carries `isDirty`: whether there are edits not
yet saved. `revision` is an opaque string that changes whenever the document the editor holds
does, including when the user edits in between two calls; compare it for equality only. It is unrelated to any
stored revision.

## Security

- **Diagram text is data.** Every name, label and documentation string reaches the agent inside
  `{ "untrustedText": "…" }`, never in the tool's own `message`, and tools that return it are annotated
  `untrustedContentHint`. A name such as "ignore previous instructions" is therefore something the agent was
  told about the diagram, not something the tool said.
- **Read-only unless the host says otherwise.** Without `AllowMutations` the editing tools are not registered.
  Annotations (`readOnlyHint`, `consequentialHint`) are hints for the browser, not enforcement: the bridge checks
  every call that changes something, whatever the agent was told.
- **The user is asked before anything hard to undo.** `bpmn_delete` and `bpmn_save` raise the editor's confirmation
  prompt (`BpmnPart.AgentConfirmation`, in both kits) and act only on an explicit yes. The question is the module's
  own localized words; element names appear only in a separately drawn list of quoted details. One question at a
  time: a second request while one is open is answered `BUSY` (the user was not asked, so it is not a refusal, and the agent may try
  again after the first is answered), and an agent that gives up withdraws its question. The delete question counts what will
  actually go, a Sub-Process's contents included, and shortens long names; a call that the tool would refuse anyway (no ids,
  too many) is refused without asking. If the stored document moves while the user is deciding, an approved change is answered
  `STALE-DOCUMENT` instead of being applied. A host that has no prompt in its editor gets `DENIED`, not an unconfirmed deletion.
- **Edits are the user's to take back.** They go through the editor's own history, marked as the agent's, so the
  user's Ctrl+Z undoes them. `bpmn_undo` and `bpmn_redo` step only through entries the agent made; when the next
  entry is the user's they refuse, so an agent cannot unwind their work.
- **A document that moved in storage is not edited.** When the stored copy changed behind the editor, every tool that
  changes anything, `bpmn_save` included, answers `STALE-DOCUMENT` until the user has chosen Reload, Save as copy or
  Dismiss in the editor. Reading still works and `bpmn_get_document_info` reports `externalChange`.
- **Same origin only.** `exposedTo` is never set.
- **The agent chooses nothing about storage.** There is no tool that names a document, a path or a connection.
- **Limits.** One call lists at most `MaxElementsPerCall` items and returns at most `MaxXmlBytes` of export.
  `bpmn_export` is the one tool that hands over the whole document; add it to `DisabledTools` where that is not wanted.

## Browser support

WebMCP is a draft, and the specification changes. This package was written against it as read on 2026-10-15. In
Chrome it is an origin trial announced to end 2026-11-17, so a page needs a trial token or a flag; that is the
host's concern. Support is detected by feature, never by user agent: the module uses `document.modelContext` and
falls back to `navigator.modelContext`, which earlier previews used.

No polyfill is bundled. A host that wants to test without a supporting browser can load
`@mcp-b/webmcp-polyfill` before Blazor starts.

If the API changes, only `src/Bpmn.WebMcp.Scripts/src/webmcp.ts` should need to: it is the one file that knows
the WebMCP shapes, and the descriptors and results come from .NET unchanged.

## Interactive render modes

The module is called from JavaScript through async interop only, so it works under Blazor Server and WebAssembly
alike. On Server every call crosses the SignalR circuit, which is fine at the speed of an agent. A lost circuit
loses the registration; the tools return when the page's component is created again.
