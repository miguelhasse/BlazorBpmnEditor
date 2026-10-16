# Known gaps

Things a review found that are real but were left alone: each is either rare, bounded, or needs a design decision. None loses data silently. Fixed findings are not listed here.

## Live updates

- **A failed catch-up stat is not retried.** After a subscription reconnects, the binding stats the document to find out what it missed. If that one stat fails, the editor stays on what it had until the next notification or save; the save still checks the revision.
- **Reconnect backoff** is simple (capped exponential, no jitter between many clients).
- **SignalR hub**: subscribing twice on one connection is not refused, and a failure in the pump that forwards changes ends that subscription without telling the client.
- **SSE client** does not look at the content type, and treats a 404 or 429 like any other failure (it retries) rather than stopping or honouring `Retry-After`.
- **SSE anonymous callers** share one subscriber identity, and an empty tenant claim is read as "no tenant".
- **WebMCP bridge**: a close that races a pending call can leave that call waiting until its own timeout.

## Storage

- **File system**: listing is not paged against very large folders; names are compared as given (no Unicode normalisation, so two spellings of one accented name are two documents); there is no size limit beyond the disk; a crash during a save can leave a temporary file beside the document.
- **EF Core**: on SQL Server the key columns can exceed the index size limit for very long paths. The path collation must be ordinal (see [storage-backends](storage-backends.md)).
- **Azure Blob**: lease and snapshot behaviour follows the service defaults; a save does not take a lease, it relies on the ETag.
- **Batch** operations cost one store call per document.

## Agent surfaces

- **`Origin` check** on the MCP HTTP endpoint compares the request's host; behind a TLS-terminating proxy that rewrites the host, configure the allowed origins explicitly.
- **The authorizer is called once per listed entry**, and the folder-visibility probe is bounded (10 folders, 50 entries, 2 pages each), so in a very large store a folder that holds something readable only deep inside may be hidden.
