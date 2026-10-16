# BPMN agent tools (preview)

`Hasseware.AspNetCore.Components.Bpmn.Tools` is the tool layer that agent surfaces share: the browser's
WebMCP module and an MCP server both expose these tools, and neither implements one twice. It depends
on the core package only.

> **Preview.** The API may change until the MCP package ships. It is public so the two surfaces, and
> anyone building a third, can use it.

## Tools

| Tool | Does | Hints |
|---|---|---|
| `bpmn_get_diagram` | Diagrams, elements (id, type, name, container, pool, bounds) and connectors, paged | read-only, untrusted content |
| `bpmn_describe_element` | One element's properties, references and findings | read-only, untrusted content |
| `bpmn_get_selection` | What the user has selected; empty for a host without a selection (`IBpmnSelectionHost`) | read-only, untrusted content |
| `bpmn_validate` | The editor's validation, with rule ids | read-only, untrusted content |
| `bpmn_export` | BPMN 2.0 XML or the JSON interchange format | read-only, untrusted content |
| `bpmn_list_creatable` | What the palette offers in a container, and why anything is unavailable | read-only |
| `bpmn_create_element` | A Flow Node beside an anchor or inside a container, placed automatically; optionally connected from the anchor | edits |
| `bpmn_connect` | A connector, refused when BPMN forbids it | edits |
| `bpmn_set_property` | One standard property | edits |
| `bpmn_delete` | Elements and what cannot outlive them, all listed | destructive |
| `bpmn_select` | Selects elements so the user sees what the agent means (`IBpmnInteractiveHost`) | edits no content |
| `bpmn_undo`, `bpmn_redo` | Steps through the agent's own edits only (`IBpmnInteractiveHost`) | edits |
| `bpmn_get_document_info` | Stored name and revision, `isDirty`, `externalChange` (`IBpmnPersistenceHost`) | read-only, untrusted content |
| `bpmn_save` | Saves to the stored document (`IBpmnPersistenceHost`); `PERSIST-CONFLICT` when it moved | destructive |

`BpmnTools.DiffAsync` compares the document with a baseline the adapter supplies. It is not in the
catalog, because its arguments name things only the adapter can resolve.

## Using it

Implement `IBpmnToolHost` over the document — or use `BpmnDocumentToolHost` for a document held in
memory, or `BpmnStoredDocumentToolHost` for a read-only document loaded from a store (its revision is the stored one) — then register `BpmnToolCatalog.All` with your protocol and send each call to
`BpmnToolCatalog.InvokeAsync`:

```csharp
var host = new BpmnDocumentToolHost(document, BpmnEditOrigin.FromAgent("my-agent"));
var result = await BpmnToolCatalog.InvokeAsync(host, "bpmn_validate", arguments);
var json = BpmnToolJson.Serialize(result);
```

## Rules the tools keep

- **Every edit goes through the editor's own commands**, so its rules, refusals and undo history apply.
  Each edit is attributed to the host's agent (`BpmnEditOrigin`), so an agent's undo can be limited to
  its own work.
- **Connections are checked against the BPMN connection rules** (`BpmnConnectionRules`, the same table
  the canvas uses during a drag). A forbidden connection is refused with `ILLEGAL-CONNECTION` and the
  rule ids. Creating and connecting in one call is one edit, so a refusal creates nothing.
- **No coordinates.** New elements are placed beside an anchor or inside a container, clear of their
  neighbours (`BpmnPlacement`).
- **Diagram text is untrusted.** Names, documentation, labels, findings and exports are returned as
  `{ "untrustedText": "…" }`, never in a tool's own `message`.
- **No locations.** No tool takes a file path, URL or connection string; a document is whatever the
  host holds.
- **Limits** (`BpmnToolLimits`) bound every answer: elements per page, export size, text length and how
  many elements one call may delete.
- **Nothing asks the user.** An adapter that can ask does so before calling a destructive tool, and
  reports `DENIED` when the user says no.

## Results

Every tool returns `ok`, `applied`, `code`, `message`, `createdIds`, `diagnostics` and `revision`, plus
`data` for tools that return something. A refusal is a result, never an exception. Codes are the
editor's own (`EDIT-…`, `DI-011`, `PL-001`, `SF-…`) or one of `BpmnToolCodes`: `INVALID-ARGUMENT`,
`NOT-FOUND`, `READONLY`, `TOO-LARGE`, `ILLEGAL-CONNECTION`, `REFUSED`, `NO-DOCUMENT`, and, reported by
adapters, `DENIED`, `STALE-DOCUMENT`, `PERSIST-CONFLICT` and `INTERNAL-ERROR`. `revision` is opaque: compare it only for
equality.
