# Hasseware.AspNetCore.Components.Bpmn.Mcp (preview)

A [Model Context Protocol](https://modelcontextprotocol.io) server over stored BPMN documents. It is a plain library
(no Blazor, no JavaScript), so it runs in a console tool, a container or a web host. It reads by default; the tools that
change documents are an explicit extra.

```csharp
builder.Services
    .AddBpmnFileDocumentStore("./diagrams")        // any IBpmnDocumentStore
    .AddBpmnMcp(options => options.Tenant = null)
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithBpmnTools();
```

| Tool | Returns |
|---|---|
| `bpmn_list_documents` | The documents and folders in a folder, a page at a time |
| `bpmn_open` | A summary of the diagram, the stored revision, and what the reader changed or dropped (`IMPORT-*`) |
| `bpmn_validate` | The editor's validation, with rule ids |
| `bpmn_describe_element` | One element's properties, references and findings |
| `bpmn_export` | BPMN 2.0 XML or the JSON interchange format |
| `bpmn_diff` | Added, removed and changed elements between two documents, by id |

With `.WithBpmnWriteTools()` and `AllowWrites = true`: `bpmn_create_element`, `bpmn_connect`, `bpmn_set_property`,
`bpmn_delete`, `bpmn_apply_batch` (all or nothing, with `@alias` references between operations) and `bpmn_create_document`.
Each saves at the revision it read, or at `expectedRevision` when the agent names one, so concurrent writers get
`PERSIST-CONFLICT` instead of overwriting each other; a store that fails while saving is `STORE-UNAVAILABLE`, never a retry.
`IBpmnDocumentAuthorizer` decides per document. For HTTP hosting see `Hasseware.AspNetCore.Components.Bpmn.Mcp.AspNetCore`.

Every tool takes a document by its **logical path** (`claims/2026/intake.bpmn`), never a file path, URL or connection
string; the path is parsed before the store is touched, the size is checked before the content is read, and the tenant
comes from the host (`BpmnMcpOptions.Tenant` or an `IBpmnMcpTenantResolver`), never from an argument.

Results are the same JSON as the browser surface (`Hasseware.AspNetCore.Components.Bpmn.WebMcp`): `ok`, `code`,
`message`, `diagnostics`, `revision` and `data`, with every name and piece of text from the diagram inside
`{ "untrustedText": … }`. A failure is a result with `isError` set, never a leaked exception message. `revision` is the
store's opaque revision.

Codes besides the editor's own: `INVALID-PATH`, `INVALID-ARGUMENT`, `NOT-FOUND`, `TOO-LARGE`, `INVALID-DOCUMENT` and
`STORE-UNAVAILABLE`.

Built against `ModelContextProtocol` 2.2.0. Calls are logged with the tool, the document id and the revision, never
with content.
