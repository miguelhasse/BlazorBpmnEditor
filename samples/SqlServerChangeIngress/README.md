# Sample: forward SQL Server changes to the change bus

A reference ingress, not a package. The EF Core store tells the change bus about every write that goes through it. Rows written by anything that bypasses
it (a script, another application, an ETL job) are announced by SQL Server itself, in one of two ways chosen by `Ingress:Mode`:

| Mode | Script | How it works | Trade-offs |
|---|---|---|---|
| `ServiceBroker` (default) | `service-broker.sql` | A trigger on `BpmnDocuments` sends one Service Broker message per statement; `ServiceBrokerIngressService` waits on the queue with `WAITFOR (RECEIVE …)` | Push: changes arrive as soon as they commit. Durable: messages wait in the queue while the ingress is down. Adds a trigger to every write, and needs Service Broker enabled on the database |
| `ChangeTracking` | `change-tracking.sql` | SQL Server records which rows changed; `ChangeTrackingIngressService` asks `CHANGETABLE(CHANGES …)` every `Ingress:PollInterval` (2 s) | Pull: changes arrive within one interval. No trigger. Starts from "now" when the ingress starts, and starts again from "now" if it falls behind the retention period |

Either way, each change carries the tenant, path, version and operation of a row, never its content. `SqlServerChangeParser` rejects any path or tenant the store
would not accept, and `BpmnDocumentChangeIngress` publishes the rest; the version is the EF Core store's revision, so the binding compares it with its own.
Changes arrive at least once and in no promised order; the ingress drops a repeat, and the binding only asks "is the stored revision still mine".

## Set up

1. Create the documents table with the EF Core store (`ApplyBpmnDocuments(pathCollation: "Latin1_General_100_BIN2")` and a migration).
2. For `ServiceBroker`, enable Service Broker once (it needs exclusive access to the database), then run `service-broker.sql`:

   ```sql
   ALTER DATABASE CURRENT SET ENABLE_BROKER WITH ROLLBACK IMMEDIATE;
   ```

   For `ChangeTracking`, run `change-tracking.sql`, which enables Change Tracking on the database (two days' retention) and on the table.
3. Configure `ConnectionStrings:Documents` and, optionally, the `Ingress` section:

   ```json
   {
     "Ingress": {
       "Mode": "ServiceBroker",
       "Table": "dbo.BpmnDocuments",
       "Queue": "dbo.BpmnDocumentChangeQueue",
       "MessageType": "//Hasseware/Bpmn/DocumentChanged",
       "ReceiveTimeout": "00:00:30",
       "PollInterval": "00:00:02"
     }
   }
   ```

   A renamed table or queue must be renamed in the script too. `Table` and `Queue` are `schema.name` pairs of plain identifiers; anything else is refused.

The account the ingress uses needs `RECEIVE` on the queue (Service Broker), or `SELECT` and `VIEW CHANGE TRACKING` on the table (Change Tracking).

## Notes

- **Several instances.** Service Broker's queue has competing readers, so each message reaches one instance: the bus must span instances. With Change Tracking every
  instance reads every change and forwards it to its own bus.
- **Failures.** Both services reconnect after `Ingress:RetryDelay` (5 s). A message whose changes cannot be forwarded is logged and still removed from the queue,
  because a message rolled back five times in a row disables the queue. If the queue is disabled anyway, the trigger's messages wait in
  `sys.transmission_queue` and writes are not blocked; re-enable it with `ALTER QUEUE … WITH STATUS = ON`.
- **Large statements.** The trigger sends one message per statement, holding every row it changed. A statement that changes many thousands of documents sends a large
  message; a busy system could also reuse conversations rather than open one per statement.
- **What is missed.** Whatever the mode, editors still check the stored revision after subscribing, after any gap in their own feed and before every save, so a
  change the ingress did not see can delay the warning but never cause an overwrite.

The services are tested against a real server by `tests/Bpmn.Samples.Tests` when `BPMN_SQLSERVER_CONNECTION` is set (Service Broker must be enabled on that database
for the Service Broker test).
