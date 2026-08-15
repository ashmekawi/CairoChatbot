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
    DECLARE @ErrorNumber int = ERROR_NUMBER();
    DECLARE @ErrorSeverity int = ERROR_SEVERITY();
    DECLARE @ErrorState int = ERROR_STATE();
    DECLARE @ErrorLine int = ERROR_LINE();

    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION;

    EXEC audit.SystemError_Log
        @ErrorReference = @ErrorReference,
        @CorrelationId = @CorrelationId,
        @Source = 'DATABASE',
        @Component = N'DatabaseVerify',
        @Operation = N'RollbackPersistence',
        @ProcedureName = N'Verify.sql',
        @ErrorNumber = @ErrorNumber,
        @ErrorSeverity = @ErrorSeverity,
        @ErrorState = @ErrorState,
        @ErrorLine = @ErrorLine,
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

IF SCHEMA_ID(N'identity') IS NULL
    THROW 51101, 'identity schema is missing.', 1;

DECLARE @RequiredIdentityObjects TABLE
(
    ObjectName sysname NOT NULL,
    ObjectType char(2) NOT NULL
);

INSERT @RequiredIdentityObjects (ObjectName, ObjectType)
VALUES
    (N'[identity].Users', N'U'),
    (N'[identity].Roles', N'U'),
    (N'[identity].Permissions', N'U'),
    (N'[identity].UserRoles', N'U'),
    (N'[identity].RolePermissions', N'U'),
    (N'[identity].RefreshTokens', N'U'),
    (N'[identity].User_Create', N'P'),
    (N'[identity].User_GetByUsername', N'P'),
    (N'[identity].User_GetByPublicId', N'P'),
    (N'[identity].User_SetActive', N'P'),
    (N'[identity].User_SetPassword', N'P'),
    (N'[identity].User_RecordLoginSuccess', N'P'),
    (N'[identity].User_RecordLoginFailure', N'P'),
    (N'[identity].Role_GetAll', N'P'),
    (N'[identity].Permission_GetByUser', N'P'),
    (N'[identity].UserRole_Assign', N'P'),
    (N'[identity].UserRole_Remove', N'P'),
    (N'[identity].RefreshToken_Create', N'P'),
    (N'[identity].RefreshToken_GetValid', N'P'),
    (N'[identity].RefreshToken_Revoke', N'P'),
    (N'[identity].RefreshToken_Rotate', N'P');

DECLARE @MissingIdentityObject sysname =
(
    SELECT TOP (1) ObjectName
    FROM @RequiredIdentityObjects
    WHERE OBJECT_ID(ObjectName, ObjectType) IS NULL
    ORDER BY ObjectName
);

IF @MissingIdentityObject IS NOT NULL
BEGIN
    DECLARE @MissingMessage nvarchar(2048) = CONCAT(@MissingIdentityObject, ' is missing.');
    THROW 51102, @MissingMessage, 1;
END;

SELECT N'V0002 verification passed.' AS Result;

IF SCHEMA_ID(N'project') IS NULL
    THROW 51201, 'project schema is missing.', 1;

DECLARE @RequiredProjectObjects TABLE
(
    ObjectName sysname NOT NULL,
    ObjectType char(2) NOT NULL
);

INSERT @RequiredProjectObjects (ObjectName, ObjectType)
VALUES
    (N'[project].Projects', N'U'),
    (N'[project].Channels', N'U'),
    (N'[project].BusinessHours', N'U'),
    (N'[project].Project_Create', N'P'),
    (N'[project].Project_GetByPublicId', N'P'),
    (N'[project].Project_Update', N'P'),
    (N'[project].Project_SetActive', N'P'),
    (N'[project].Channel_Create', N'P'),
    (N'[project].Channel_GetByPublicId', N'P'),
    (N'[project].Channel_Update', N'P'),
    (N'[project].Channel_SetActive', N'P'),
    (N'[project].BusinessHours_GetByProject', N'P'),
    (N'[project].BusinessHours_Upsert', N'P');

DECLARE @MissingProjectObject sysname =
(
    SELECT TOP (1) ObjectName
    FROM @RequiredProjectObjects
    WHERE OBJECT_ID(ObjectName, ObjectType) IS NULL
    ORDER BY ObjectName
);

IF @MissingProjectObject IS NOT NULL
BEGIN
    DECLARE @MissingProjectMessage nvarchar(2048) = CONCAT(@MissingProjectObject, ' is missing.');
    THROW 51202, @MissingProjectMessage, 1;
END;

IF EXISTS
(
    SELECT required.Code
    FROM
    (
        VALUES ('projects.read'), ('projects.manage'), ('channels.read'), ('channels.manage')
    ) required(Code)
    WHERE NOT EXISTS
    (
        SELECT 1 FROM [identity].Permissions permission WHERE permission.Code = required.Code
    )
)
    THROW 51203, 'A V0003 permission is missing.', 1;

SELECT N'V0003 verification passed.' AS Result;

IF SCHEMA_ID(N'contact') IS NULL
    THROW 51301, 'contact schema is missing.', 1;

DECLARE @RequiredContactObjects TABLE
(
    ObjectName sysname NOT NULL,
    ObjectType char(2) NOT NULL
);

INSERT @RequiredContactObjects (ObjectName, ObjectType)
VALUES
    (N'[contact].Contacts', N'U'),
    (N'[contact].ChannelIdentities', N'U'),
    (N'[contact].Contact_Create', N'P'),
    (N'[contact].Contact_GetByPublicId', N'P'),
    (N'[contact].Contact_Update', N'P'),
    (N'[contact].Contact_SetActive', N'P'),
    (N'[contact].ChannelIdentity_Create', N'P'),
    (N'[contact].ChannelIdentity_GetByPublicId', N'P'),
    (N'[contact].ChannelIdentity_Update', N'P'),
    (N'[contact].ChannelIdentity_SetActive', N'P'),
    (N'[contact].ChannelIdentity_SetVerified', N'P'),
    (N'[contact].ChannelIdentity_GetByExternalId', N'P');

DECLARE @MissingContactObject sysname =
(
    SELECT TOP (1) ObjectName
    FROM @RequiredContactObjects
    WHERE OBJECT_ID(ObjectName, ObjectType) IS NULL
    ORDER BY ObjectName
);

IF @MissingContactObject IS NOT NULL
BEGIN
    DECLARE @MissingContactMessage nvarchar(2048) = CONCAT(@MissingContactObject, ' is missing.');
    THROW 51302, @MissingContactMessage, 1;
END;

IF EXISTS
(
    SELECT required.Code
    FROM (VALUES ('contacts.read'), ('contacts.manage')) required(Code)
    WHERE NOT EXISTS
    (
        SELECT 1 FROM [identity].Permissions permission WHERE permission.Code = required.Code
    )
)
    THROW 51303, 'A V0004 permission is missing.', 1;

SELECT N'V0004 verification passed.' AS Result;
