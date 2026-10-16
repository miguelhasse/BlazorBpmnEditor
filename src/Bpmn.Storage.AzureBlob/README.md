# Hasseware.AspNetCore.Components.Bpmn.Storage.AzureBlob (preview)

A BPMN document store over an Azure Blob Storage container.

```csharp
builder.Services.AddSingleton(_ => new BlobServiceClient(new Uri("https://myaccount.blob.core.windows.net"), new DefaultAzureCredential()));
builder.Services.AddBpmnAzureBlobDocumentStore(provider => provider.GetRequiredService<BlobServiceClient>().GetBlobContainerClient("diagrams"), prefix: "prod");
```

- **The ETag is the revision.** A read returns the blob's ETag; a create is `If-None-Match: *` and an update `If-Match`, so the service makes the
  compare-and-write atomic and two writers cannot both win. Nothing reads first and writes after.
- **Layout.** `<prefix>/<logical path>` for the default space and `<prefix>/~tenants/<tenant>/<logical path>` for a tenant (a document path cannot contain `~`).
- **Credentials and the container live in host configuration** and are never returned by a tool or logged. Prefer a managed identity scoped to one container
  (`Storage Blob Data Contributor` for a writing host, `Reader` for a read-only one).
- **Listing** reads the flat list of blobs under a folder and builds folders in memory, so it considers at most `MaxListEntries` (100,000); beyond that, list
  a subfolder or give a name prefix.
- **Soft delete and blob versioning** are the container's recovery story: deleting a document is not undo.
- **Changes made outside the store** (an upload in the portal, another application) are not seen by editors until they read again. Event Grid's
  `BlobCreated` carries the blob's ETag, which is this store's revision; see the ingress sample for forwarding it to the change bus.
- To test without Azure, implement `IBpmnBlobContainer` (six small operations) and give it to `AzureBlobBpmnDocumentStore`.
