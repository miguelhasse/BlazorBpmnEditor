# Serving diagrams to agents over MCP (preview)

Two things for agents and pipelines that have no browser: a **server** that lets an AI agent read the BPMN documents
in a store, and a **command** that validates `.bpmn` files in a build. Both are plain .NET: no Blazor, no JavaScript.
The browser surface is described in [webmcp.md](webmcp.md); the design of both is in
[webmcp-design.md](webmcp-design.md). Out of the box the server only reads; changing documents is something the
operator turns on, and every change is saved at the revision it was made against.

| Package | What it is |
|---|---|
| `Hasseware.AspNetCore.Components.Bpmn.Mcp` | The MCP tools, over any `IBpmnDocumentStore` |
| `Hasseware.AspNetCore.Components.Bpmn.Mcp.Tool` | The `bpmn-mcp` command: `serve` and `validate` |
| `Hasseware.AspNetCore.Components.Bpmn.Mcp.AspNetCore` | Hosts the server over streamable HTTP, authenticated and stateless |
| `Hasseware.AspNetCore.Components.Bpmn.Storage.FileSystem` | A store over a folder of files |
| `Hasseware.AspNetCore.Components.Bpmn.Storage.Testing` | The contract suite every store must pass (test-only) |

## The `bpmn-mcp` command

```
dotnet tool install --global Hasseware.AspNetCore.Components.Bpmn.Mcp.Tool
```

**Serve a folder to an agent**

```
bpmn-mcp serve --root ./diagrams [--tenant <name>] [--allow-writes]
```

An MCP client starts it as a child process and speaks over stdin and stdout, for example in a client's MCP
configuration:

```json
{ "mcpServers": { "bpmn": { "command": "bpmn-mcp", "args": ["serve", "--root", "./diagrams"] } } }
```

Stdout carries the protocol and nothing else; every log line goes to stderr. Without `--allow-writes` nothing can
change; with it the agent may also create and edit documents (see [Changing documents](#changing-documents)). An unknown
option is refused rather than ignored.

**Validate in a build**

```
bpmn-mcp validate diagrams                       # every .bpmn under the folder
bpmn-mcp validate "src/**/*.bpmn" --warnings-as-errors --json
```

One line per finding, `file: severity CODE message [element]`, or JSON with `--json`. The findings are exactly what an
agent's `bpmn_validate` reports; a file the reader degrades or drops content from shows up as `IMPORT-*`.

| Exit code | Meaning |
|---|---|
| 0 | Clean |
| 1 | Errors, or warnings with `--warnings-as-errors`, or a file that is not BPMN |
| 2 | An argument or a file could not be used (no match, unreadable, unknown option) |

A broken invocation therefore never passes as a clean diagram. A pipeline step is just
`dotnet tool restore && dotnet bpmn-mcp validate diagrams --warnings-as-errors`.

## The tools

| Tool | Returns |
|---|---|
| `bpmn_list_documents` | The documents and folders in a folder (`folder`, `recursive`, `namePrefix`), a page at a time (`cursor`, `limit`) |
| `bpmn_open` | The diagram summary (diagrams, elements, connectors), the stored `revision`, and the reader's `IMPORT-*` findings |
| `bpmn_validate` | The editor's validation, with rule ids |
| `bpmn_describe_element` | One element's properties, references and findings |
| `bpmn_export` | BPMN 2.0 XML or the JSON interchange format |
| `bpmn_diff` | What was added, removed and changed between two documents, by element id (no move detection) |

The reading tools are annotated read-only, idempotent, non-destructive and closed-world. They are the same handlers as the
browser surface's, so a result has the same shape in both: `ok`, `code`, `message`, `diagnostics`, `revision`, `data`.
The text block of an MCP result is exactly that JSON and so is its structured content; a failure sets `isError`.

A document is named by its **logical path**, `claims/2026/intake.bpmn`. No tool takes a file path, a URL, a connection
string or a tenant. The path is parsed before the store is touched (`INVALID-PATH` for `..`, a leading `/`, a backslash,
a drive letter, a reserved device name, an extension other than `.bpmn`, `.xml` or `.json`), and the size is checked
before the content is read (`TOO-LARGE`).

Codes besides the editor's own: `INVALID-PATH`, `INVALID-ARGUMENT`, `NOT-FOUND`, `TOO-LARGE`, `INVALID-DOCUMENT`
(present but not readable as BPMN), `STORE-UNAVAILABLE` (the store failed; nothing is known about the document, or about
whether a write happened), `READONLY`, `PERSIST-CONFLICT` and `FORBIDDEN`.
The messages never quote the document or an exception.

## Changing documents

Writing is opt-in twice: the host registers `WithBpmnWriteTools()` and sets `AllowWrites` (`bpmn-mcp serve --allow-writes`
does both); otherwise the tools are absent, and if registered anyway every call answers `READONLY` and saves nothing.

| Tool | Does |
|---|---|
| `bpmn_create_element` | A Flow Node placed beside an anchor or inside a container, optionally connected from the anchor |
| `bpmn_connect` | A connector; refused, with the rule ids, when BPMN forbids it |
| `bpmn_set_property` | One standard property (idempotent) |
| `bpmn_delete` | Elements and what cannot outlive them, listed (destructive) |
| `bpmn_apply_batch` | Up to 50 operations as one edit: all saved, or none |
| `bpmn_create_document` | A blank document at a logical path where there is none; folders are created as needed |

They are the browser's handlers, so the arguments, refusals (`EDIT-…`, `ILLEGAL-CONNECTION`, `NOT-FOUND`) and diagnostics are
the same. Every call reads the document, edits it in memory and writes it back **at the revision it read** with the store's
conditional save; nothing is kept between calls. Each result carries the document's new stored `revision`.

- **Conflicts.** Pass `expectedRevision` (the `revision` from `bpmn_open`, or from your last edit) to say "only if nothing
  changed since I looked"; otherwise the edit still saves only at the revision this call read, so a lost race is never
  reported as saved. A document that has moved is `PERSIST-CONFLICT` with the current revision, and nothing is written.
  Of several writers holding one revision exactly one wins.
- **A failing store.** If the store fails while saving, the answer is `STORE-UNAVAILABLE` and says the edit may or may not
  have been written: open the document to find out. A conditional write is never retried for you.
- **Size.** An edit that would make the document larger than `MaxXmlBytes` is `TOO-LARGE` and is not saved.
- **Batches.** Each operation is `{ "op": "createElement|connect|setProperty|delete", "arguments": { … }, "as": "name" }`, with
  that tool's arguments and no `documentId`. `as` names what the operation creates, and a later operation uses `"@name"`
  wherever an element id is expected:

```json
{ "documentId": "claims/intake.bpmn",
  "operations": [
    { "op": "createElement", "arguments": { "type": "userTask", "name": "Review", "anchorId": "Start_1", "connectFromAnchor": true }, "as": "review" },
    { "op": "createElement", "arguments": { "type": "endEvent", "anchorId": "@review", "connectFromAnchor": true } } ] }
```

  The first refused operation rejects the batch (`failedAt` is its index, with its code and diagnostics) and nothing is
  saved; success saves once and moves the revision once.
- **Who may.** `IBpmnDocumentAuthorizer` decides per document and action (read, write, create); the default allows
  everything inside the tenant. A refusal is answered exactly like a missing document, except that a refused *create* is
  `FORBIDDEN`. Nothing is disclosed about what a caller may not read: a folder is listed only when something beneath it is readable
  (a bounded look: at most ten folders per page, each checked over its first hundred documents, so a very large folder the caller
  cannot see into is hidden rather than scanned), and a *create* that collides with a document the caller cannot read is the same
  `FORBIDDEN`, with no revision, not `PERSIST-CONFLICT`. The audit log records the caller, the tool, the document id and the revision before and after, never content.

## Over HTTP

`Hasseware.AspNetCore.Components.Bpmn.Mcp.AspNetCore` serves the same tools over streamable HTTP in an ASP.NET Core app:

```csharp
builder.Services.AddBpmnFileDocumentStore("/data/diagrams");
builder.Services.AddBpmnMcpHttp(http => http.TenantClaimType = "tid", mcp => mcp.AllowWrites = true);   // AllowWrites also offers the writing tools
builder.Services.AddAuthentication(/* e.g. JwtBearer, or the SDK's AddMcp OAuth metadata handler */);
builder.Services.AddAuthorization();
// …
app.UseAuthentication();
app.UseAuthorization();
app.MapBpmnMcp("/mcp");
```

- **Stateless.** `SessionMode` is set to `Stateless` explicitly. Every request stands alone, so any number of instances
  can sit behind a load balancer and the store's revision check arbitrates concurrent writers.
- **Authenticated, and the tenant is the caller's.** The endpoint requires an authenticated caller. The tenant is the
  `TenantClaimType` claim; a caller without it, or whose claim names no tenant a store could hold (an email address, a URL, more than
  128 characters), gets 403, never the shared space. A host must choose
  `TenantClaimType` or `SingleTenant` (not both), or `MapBpmnMcp` throws. `AllowAnonymous` needs `SingleTenant` and is meant for a
  loopback developer endpoint. `AuthorizationPolicy` adds a scope or role requirement, and `MapBpmnMcp` throws if it is combined with `AllowAnonymous`, where it would
  silently apply to no one.
- **At the door.** `Host` must be a loopback name unless you list yours in `AllowedHosts` (DNS rebinding); a request with an
  `Origin` must be the endpoint's own or in `AllowedOrigins`; a body over `MaxRequestBodyBytes` is 413; `RateLimitPolicy`
  applies one of your rate-limiter policies.
- **Transport security.** Run on loopback, or terminate TLS in front, and set `AllowedHosts` to the public name. Credentials
  for the store belong in host configuration; prefer a least-privilege identity, and a read-only one for a read-only deployment.

## In your own host

```csharp
builder.Services
    .AddBpmnFileDocumentStore("./diagrams")          // or any IBpmnDocumentStore
    .AddBpmnMcp(options => options.Tenant = "acme")  // fixed by the host, never by the agent
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithBpmnTools();
```

`BpmnMcpOptions`: `Tenant`, `MaxElementsPerCall` (200) and `MaxXmlBytes` (1,000,000), both at least 1, `AgentName`, and
`AllowWrites` (false). A host that serves several users supplies an
`IBpmnMcpTenantResolver` that answers from the authenticated caller. Profile validators registered in DI, such as the
scripting ones, run with `bpmn_validate`. Calls are logged (tool, document id, revision) and never with content.

## The file store

`AddBpmnFileDocumentStore(root)` keeps each document as a file under the root.

- **Paths stay inside the root.** The logical path is parsed by the same `BpmnDocumentPath` every store uses, mapped
  under the root and checked again; no folder or file on the way may be a symbolic link or junction, which the store
  does not follow (a read finds nothing, a write is refused).
- **Tenants** live in `<root>/~tenants/<tenant>/`. A document path cannot contain `~`, so a tenant folder cannot collide
  with a document folder, and the default space never lists them.
- **Writes are atomic and checked.** The new content goes to a temporary file in the same folder and replaces the old
  in one step, under a lock that works across processes; the revision comparison and the replace are one action, so of
  two writers holding the same revision exactly one wins. A revision is a hash of length and last-write time, and a
  save always moves the time forward.
- **Case.** On Windows and macOS `Order.bpmn` and `order.bpmn` are one file; the store refuses to overwrite one with
  the other and reports the other spelling as missing. Folders appear when they hold a document; other files are
  never listed.
- The store does not watch the folder, so changes made by other programs are seen only by reading again.

To write a store of your own, derive from `BpmnDocumentStoreContractTests` in the `Storage.Testing` package and
implement `CreateStore`; the suite checks create-only and conditional saves, stale revisions, one winner among
concurrent writers, hierarchical listing with stable paging, tenants kept apart and names that differ only by case.

## Security

- **Untrusted by construction.** Document names, file names and element text reach the agent only inside
  `{ "untrustedText": … }`, never in a tool's own message, and the file reader prohibits DTDs and external entities,
  so a hostile file cannot read other files or expand into memory (both are tested).
- **Read-only until asked.** Nothing changes a document unless the host registers the writing tools *and* sets
  `AllowWrites`. Deleting a document is not offered at all: removal stays with a person or an operator.
- **The agent chooses nothing about storage.** It never names a location, a connection or a tenant.
- **Local use.** The stdio server runs with the permissions of whoever starts it and serves only the folder it was given.
  Do not point `--root` at a folder that holds anything you would not show the agent.

SDK: built against `ModelContextProtocol` 2.2.0, whose annotation names (`ReadOnly`, `Destructive`, `Idempotent`,
`OpenWorld`, `UseStructuredContent`), `CallToolResult.StructuredContent`, `StreamServerTransport` and
`StdioClientTransport` were checked against the package.
