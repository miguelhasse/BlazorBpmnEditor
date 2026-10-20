-- Change Tracking mode: SQL Server records which rows of the documents table changed; the ingress asks for them on an interval.
-- There is no trigger and nothing is added to the write path beyond Change Tracking's own bookkeeping.

IF NOT EXISTS (SELECT 1 FROM sys.change_tracking_databases WHERE database_id = DB_ID())
    ALTER DATABASE CURRENT SET CHANGE_TRACKING = ON (CHANGE_RETENTION = 2 DAYS, AUTO_CLEANUP = ON);
GO

IF NOT EXISTS (SELECT 1 FROM sys.change_tracking_tables WHERE object_id = OBJECT_ID(N'dbo.BpmnDocuments'))
    ALTER TABLE dbo.BpmnDocuments ENABLE CHANGE_TRACKING WITH (TRACK_COLUMNS_UPDATED = OFF);
GO
