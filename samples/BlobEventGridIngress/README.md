# Sample: forward Azure Event Grid blob events to the change bus

A reference ingress, not a package. The Azure Blob store tells the change bus about every write that goes through it. Writes that bypass
it (an upload in the portal, another application, a pipeline) are announced by Event Grid instead, and this sample turns those events into
changes with `BpmnDocumentChangeIngress`.

- `BlobEventGridParser` reads a delivery (`Microsoft.Storage.BlobCreated`, `…BlobDeleted`, and the subscription validation handshake) into the
  document key (through `AzureBlobBpmnDocumentStore.TryKeyOf`, so tenants and the prefix map as the store maps them) and the revision: the blob's
  ETag, which is also the store's revision.
- `BlobEventGridEndpoint` is the webhook: it refuses any delivery without the shared key in `aeg-sas-key` (compared in constant time), caps the body,
  answers the handshake, and publishes.
- Events arrive at least once and unordered. A repeated revision is dropped by the ingress; anything else is "changed, revision X", and the binding compares it
  with its own, which is right in any order.

Setup: create an Event Grid subscription on the storage account (event types `BlobCreated`, `BlobDeleted`; subject filter `/blobServices/default/containers/<container>/`),
webhook endpoint `https://<host>/events/blob`, with a delivery header `aeg-sas-key: <secret>` (a header from the subscription's delivery properties). Configure
`Ingress:Container`, `Ingress:Prefix` and `Ingress:SharedSecret`. The endpoint must be reachable by Event Grid and nothing else should reach it.

The blob change feed carries the same information but arrives minutes later and must be enabled on the account; it suits audit, not an editor warning. Azurite does not
emulate Event Grid, so the tests use delivery bodies as Event Grid documents them.
