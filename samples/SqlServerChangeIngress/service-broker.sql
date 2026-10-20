-- Service Broker mode: a trigger on the documents table of Hasseware.AspNetCore.Components.Bpmn.Storage.EfCore sends one message per
-- statement, holding the tenant, path, version and operation of every row it changed. No content is sent.
--
-- Service Broker must be enabled on the database. That needs exclusive access, so it is not done here; run it once, in a quiet moment:
--   ALTER DATABASE CURRENT SET ENABLE_BROKER WITH ROLLBACK IMMEDIATE;
-- Check with: SELECT is_broker_enabled FROM sys.databases WHERE database_id = DB_ID();

CREATE MESSAGE TYPE [//Hasseware/Bpmn/DocumentChanged] VALIDATION = NONE;
CREATE CONTRACT [//Hasseware/Bpmn/DocumentChanges] ([//Hasseware/Bpmn/DocumentChanged] SENT BY INITIATOR);

-- One queue holds both ends of every conversation: the ingress receives the change on the target side, ends it, and then receives the
-- initiator side's EndDialog and ends that too, so no conversation is left open.
CREATE QUEUE dbo.BpmnDocumentChangeQueue;
CREATE SERVICE [//Hasseware/Bpmn/DocumentChanges/Initiator] ON QUEUE dbo.BpmnDocumentChangeQueue;
CREATE SERVICE [//Hasseware/Bpmn/DocumentChanges/Target] ON QUEUE dbo.BpmnDocumentChangeQueue ([//Hasseware/Bpmn/DocumentChanges]);
GO

CREATE OR ALTER TRIGGER dbo.BpmnDocuments_Changed ON dbo.BpmnDocuments
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    -- Rows in inserted are inserts or updates; rows only in deleted are deletes (including the old key of an update that renamed a row).
    DECLARE @payload nvarchar(max) =
    (
        SELECT changed.Tenant AS tenant, changed.Path AS path, changed.Version AS version, changed.Operation AS op
        FROM
        (
            SELECT i.Tenant, i.Path, i.Version, CASE WHEN d.Path IS NULL THEN N'I' ELSE N'U' END AS Operation
            FROM inserted AS i
            LEFT JOIN deleted AS d ON d.Tenant = i.Tenant AND d.Path = i.Path
            UNION ALL
            SELECT d.Tenant, d.Path, NULL, N'D'
            FROM deleted AS d
            WHERE NOT EXISTS (SELECT 1 FROM inserted AS i WHERE i.Tenant = d.Tenant AND i.Path = d.Path)
        ) AS changed
        FOR JSON PATH, INCLUDE_NULL_VALUES
    );

    IF @payload IS NULL RETURN;

    DECLARE @dialog uniqueidentifier;
    BEGIN DIALOG CONVERSATION @dialog
        FROM SERVICE [//Hasseware/Bpmn/DocumentChanges/Initiator]
        TO SERVICE N'//Hasseware/Bpmn/DocumentChanges/Target'
        ON CONTRACT [//Hasseware/Bpmn/DocumentChanges]
        WITH ENCRYPTION = OFF;
    SEND ON CONVERSATION @dialog MESSAGE TYPE [//Hasseware/Bpmn/DocumentChanged] (@payload);
END;
GO
