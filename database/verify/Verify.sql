SET NOCOUNT ON;

IF SCHEMA_ID(N'system') IS NULL THROW 51001, 'system schema is missing.', 1;
IF SCHEMA_ID(N'audit') IS NULL THROW 51002, 'audit schema is missing.', 1;
IF OBJECT_ID(N'system.DatabaseVersions', N'U') IS NULL THROW 51003, 'DatabaseVersions is missing.', 1;
IF OBJECT_ID(N'system.Settings', N'U') IS NULL THROW 51004, 'Settings is missing.', 1;
IF OBJECT_ID(N'audit.SystemErrorLogs', N'U') IS NULL THROW 51005, 'SystemErrorLogs is missing.', 1;
IF OBJECT_ID(N'audit.AuditLogs', N'U') IS NULL THROW 51006, 'AuditLogs is missing.', 1;
IF OBJECT_ID(N'audit.OperationalLogs', N'U') IS NULL THROW 51007, 'OperationalLogs is missing.', 1;
IF OBJECT_ID(N'audit.SystemError_Log', N'P') IS NULL THROW 51008, 'SystemError_Log is missing.', 1;
IF OBJECT_ID(N'audit.AuditLog_Create', N'P') IS NULL THROW 51009, 'AuditLog_Create is missing.', 1;
IF OBJECT_ID(N'audit.OperationalLog_Create', N'P') IS NULL THROW 51010, 'OperationalLog_Create is missing.', 1;

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'audit.SystemErrorLogs')
      AND is_unique = 1
      AND name = N'UQ_SystemErrorLogs_ErrorReference'
)
    THROW 51011, 'ErrorReference unique constraint is missing.', 1;

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'audit.SystemErrorLogs')
      AND name = N'IX_SystemErrorLogs_CorrelationId'
)
    THROW 51012, 'CorrelationId lookup index is missing.', 1;

DECLARE @CorrelationId uniqueidentifier = NEWID();
DECLARE @ErrorReference varchar(40) = CONCAT(
    'VERIFY-', LEFT(REPLACE(CONVERT(varchar(36), NEWID()), '-', ''), 24)
);
DECLARE @SettingKey nvarchar(200) = CONCAT('VERIFY-', CONVERT(varchar(36), @CorrelationId));

BEGIN TRY
    BEGIN TRANSACTION;
    INSERT system.Settings
        (SettingKey, SettingValue, ValueType, Description, UpdatedBy)
    VALUES
        (@SettingKey, N'test', 'INVALID', N'test only', N'Verify.sql');
    COMMIT TRANSACTION;
    THROW 51013, 'Expected SQL failure did not occur.', 1;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;

    EXEC audit.SystemError_Log
        @ErrorReference = @ErrorReference,
        @CorrelationId = @CorrelationId,
        @Source = 'DATABASE',
        @Component = N'DatabaseVerify',
        @Operation = N'RollbackPersistence',
        @ProcedureName = N'Verify.sql',
        @ErrorNumber = ERROR_NUMBER(),
        @ErrorSeverity = ERROR_SEVERITY(),
        @ErrorState = ERROR_STATE(),
        @ErrorLine = ERROR_LINE(),
        @ErrorMessage = N'Verification test record';
END CATCH;

IF EXISTS (SELECT 1 FROM system.Settings WHERE SettingKey = @SettingKey)
    THROW 51014, 'Business transaction was not rolled back.', 1;

IF NOT EXISTS
(
    SELECT 1 FROM audit.SystemErrorLogs
    WHERE ErrorReference = @ErrorReference
      AND CorrelationId = @CorrelationId
)
    THROW 51015, 'Error log did not survive business rollback.', 1;

DELETE FROM audit.SystemErrorLogs WHERE ErrorReference = @ErrorReference;
SELECT N'V0001 verification passed.' AS Result;
