# Hasseware.AspNetCore.Components.Bpmn.Storage.FileSystem (preview)

A BPMN document store over a folder: `IBpmnDocumentStore` with documents as `.bpmn`, `.xml` and `.json` files.

```csharp
builder.Services.AddBpmnFileDocumentStore("./diagrams");
```

- **A logical path never becomes a file name by accident.** `claims/2026/intake.bpmn` is parsed by the same
  `BpmnDocumentPath` every store uses, mapped under the root, and checked again: the full path must stay inside the
  root, and no folder or file on the way may be a symbolic link or junction.
- **Tenants** live in `<root>/~tenants/<tenant>/`. `~` cannot appear in a document path, so a tenant folder can
  never collide with a document folder, and listing the default space never shows them.
- **Writes are atomic.** The new content goes to a temporary file in the same folder and replaces the old one in one
  step, so a crash leaves the old file. A save names the revision it expects; the comparison and the replace happen
  under a lock that works across processes, so two writers cannot both win.
- **A revision** is opaque: a hash of the file's length and last-write time, which a save always moves forward.
  Reading it needs no file read, so `StatAsync` is cheap.
- **Case.** On a case-insensitive file system `Order.bpmn` and `order.bpmn` are one file. The store refuses to
  overwrite one with the other, and reports the differently cased name as missing, rather than answering with the
  other document.
- Folders appear when they hold a document. Files with other extensions, and the store's own working files, are
  never listed.

The store passes the contract suite of `Hasseware.AspNetCore.Components.Bpmn.Storage.Testing`. It does not watch the
folder: changes made by other programs are noticed only by reading again.
