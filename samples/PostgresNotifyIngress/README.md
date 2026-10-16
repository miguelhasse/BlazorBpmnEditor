# Sample: forward PostgreSQL NOTIFY to the change bus

A reference ingress, not a package. The EF Core store tells the change bus about every write that goes through it. Rows written by anything that bypasses
it (a script, another application, an ETL job) are announced by the database itself, with `LISTEN`/`NOTIFY`, which is real push: no polling.

- `trigger.sql` creates a trigger on the `BpmnDocuments` table that calls `pg_notify('bpmn_documents', …)` with the tenant, path, version and operation. No content is sent.
- `PostgresNotifyIngressService` holds a connection that has issued `LISTEN`, reads each notification with `PostgresNotificationParser` (which rejects any path the store would not
  accept) and publishes it through `BpmnDocumentChangeIngress`. The version is the EF Core store's revision, so the binding compares it with its own.
- If the connection drops, the service reconnects after five seconds; notifications sent meanwhile are lost, and editors learn of what they missed at their next save or when their
  own live-update feed reconnects.

Other databases differ: SQL Server's Change Tracking and CDC are pull-based, and MySQL and SQLite have no practical push, so there is no generic "EF Core ingress"; this sample is
the PostgreSQL one. Configure `ConnectionStrings:Documents`.
