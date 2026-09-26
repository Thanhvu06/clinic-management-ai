/*
   ClinicCare AI Phase 1.2 hardening overlay.
   Review-only/idempotent: do not run automatically from the application.
   The EF migration is the source of truth; this script is for a DBA applying
   the same guarded shape to an existing SQL Server database.
*/
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF COL_LENGTH(N'dbo.AppointmentChangeRequests', N'SourceAiActionId') IS NULL
    ALTER TABLE dbo.AppointmentChangeRequests ADD SourceAiActionId uniqueidentifier NULL;

IF COL_LENGTH(N'dbo.AiPendingToolActions', N'ExecutionAttemptCount') IS NULL
    ALTER TABLE dbo.AiPendingToolActions ADD ExecutionAttemptCount int NOT NULL CONSTRAINT DF_AiPendingToolActions_ExecutionAttemptCount DEFAULT (0);
IF COL_LENGTH(N'dbo.AiPendingToolActions', N'ExecutionLeaseExpiresAtUtc') IS NULL
    ALTER TABLE dbo.AiPendingToolActions ADD ExecutionLeaseExpiresAtUtc datetime2 NULL;
IF COL_LENGTH(N'dbo.AiPendingToolActions', N'ExecutionLeaseId') IS NULL
    ALTER TABLE dbo.AiPendingToolActions ADD ExecutionLeaseId uniqueidentifier NULL;
IF COL_LENGTH(N'dbo.AiPendingToolActions', N'LastErrorCode') IS NULL
    ALTER TABLE dbo.AiPendingToolActions ADD LastErrorCode nvarchar(120) NULL;
IF COL_LENGTH(N'dbo.AiPendingToolActions', N'State') IS NULL
    ALTER TABLE dbo.AiPendingToolActions ADD State nvarchar(32) NULL;

UPDATE dbo.AiPendingToolActions
SET State = CASE
    WHEN ExecutedAtUtc IS NOT NULL THEN N'Completed'
    WHEN CancelledAtUtc IS NOT NULL THEN N'Cancelled'
    WHEN ExpiresAtUtc <= SYSUTCDATETIME() THEN N'Expired'
    ELSE N'PendingConfirmation'
END
WHERE State IS NULL;

IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.AiPendingToolActions') AND name = N'State' AND is_nullable = 1)
    ALTER TABLE dbo.AiPendingToolActions ALTER COLUMN State nvarchar(32) NOT NULL;

IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.AiPendingToolActions') AND name = N'IX_AiPendingToolActions_UserId_ResourceType_ResourceId')
    DROP INDEX IX_AiPendingToolActions_UserId_ResourceType_ResourceId ON dbo.AiPendingToolActions;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.AiPendingToolActions') AND name = N'IX_AiPendingToolActions_UserId_ResourceType_ResourceId')
    CREATE UNIQUE INDEX IX_AiPendingToolActions_UserId_ResourceType_ResourceId
        ON dbo.AiPendingToolActions(UserId, ResourceType, ResourceId)
        WHERE State IN (N'PendingConfirmation', N'Executing', N'FailedRetryable');

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.AppointmentChangeRequests') AND name = N'IX_AppointmentChangeRequests_SourceAiActionId')
    CREATE UNIQUE INDEX IX_AppointmentChangeRequests_SourceAiActionId
        ON dbo.AppointmentChangeRequests(SourceAiActionId)
        WHERE SourceAiActionId IS NOT NULL;

COMMIT TRANSACTION;
