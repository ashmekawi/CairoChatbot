SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF SCHEMA_ID(N'system') IS NULL EXEC(N'CREATE SCHEMA system AUTHORIZATION dbo;');
GO
IF SCHEMA_ID(N'audit') IS NULL EXEC(N'CREATE SCHEMA audit AUTHORIZATION dbo;');
GO

IF OBJECT_ID(N'system.DatabaseVersions', N'U') IS NULL
BEGIN
    CREATE TABLE system.DatabaseVersions
    (
        Version             int             NOT NULL CONSTRAINT PK_DatabaseVersions PRIMARY KEY,
        VersionName         nvarchar(200)   NOT NULL,
        Checksum            char(64)        NOT NULL,
        AppliedAtUtc        datetime2(3)    NOT NULL CONSTRAINT DF_DatabaseVersions_AppliedAtUtc DEFAULT SYSUTCDATETIME(),
        AppliedBy           nvarchar(200)   NOT NULL,
        ExecutionMs         bigint          NOT NULL,
        CONSTRAINT UQ_DatabaseVersions_Checksum UNIQUE (Checksum)
    );
END;
GO

IF OBJECT_ID(N'system.Settings', N'U') IS NULL
BEGIN
    CREATE TABLE system.Settings
    (
        SettingKey          nvarchar(200)   NOT NULL CONSTRAINT PK_Settings PRIMARY KEY,
        SettingValue        nvarchar(max)   NULL,
        ValueType           varchar(30)     NOT NULL,
        Description         nvarchar(500)   NULL,
        IsActive            bit             NOT NULL CONSTRAINT DF_Settings_IsActive DEFAULT (1),
        UpdatedAtUtc        datetime2(3)    NOT NULL CONSTRAINT DF_Settings_UpdatedAtUtc DEFAULT SYSUTCDATETIME(),
        UpdatedBy           nvarchar(200)   NULL,
        CONSTRAINT CK_Settings_ValueType CHECK (ValueType IN ('STRING','INT','DECIMAL','BOOL','JSON','DATETIME'))
    );
END;
GO

IF OBJECT_ID(N'audit.SystemErrorLogs', N'U') IS NULL
BEGIN
    CREATE TABLE audit.SystemErrorLogs
    (
        ErrorLogId          bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_SystemErrorLogs PRIMARY KEY,
        ErrorReference      varchar(40)       NOT NULL,
        OccurredAtUtc       datetime2(3)      NOT NULL CONSTRAINT DF_SystemErrorLogs_OccurredAtUtc DEFAULT SYSUTCDATETIME(),
        CorrelationId       uniqueidentifier  NOT NULL,
        Source              varchar(30)       NOT NULL,
        Component           nvarchar(200)     NULL,
        Operation           nvarchar(200)     NULL,
        ProcedureName       nvarchar(256)     NULL,
        ErrorNumber         int               NULL,
        ErrorSeverity       int               NULL,
        ErrorState          int               NULL,
        ErrorLine           int               NULL,
        ExceptionType       nvarchar(500)     NULL,
        ErrorMessage        nvarchar(4000)    NOT NULL,
        StackTrace          nvarchar(max)     NULL,
        ActorUserId         uniqueidentifier  NULL,
        ConversationId      uniqueidentifier  NULL,
        MessageId           uniqueidentifier  NULL,
        JobId               uniqueidentifier  NULL,
        RequestPath         nvarchar(1000)    NULL,
        HttpMethod          varchar(20)       NULL,
        ContextJson         nvarchar(max)     NULL,
        HostName            nvarchar(200)     NULL,
        ApplicationName     nvarchar(200)     NULL,
        EnvironmentName     nvarchar(100)     NULL,
        CONSTRAINT UQ_SystemErrorLogs_ErrorReference UNIQUE (ErrorReference),
        CONSTRAINT CK_SystemErrorLogs_Source CHECK (Source IN ('DATABASE','BACKEND','FRONTEND','WORKER','INTEGRATION','DEPLOYMENT')),
        CONSTRAINT CK_SystemErrorLogs_ContextJson CHECK (ContextJson IS NULL OR ISJSON(ContextJson)=1)
    );
    CREATE INDEX IX_SystemErrorLogs_CorrelationId ON audit.SystemErrorLogs(CorrelationId);
    CREATE INDEX IX_SystemErrorLogs_OccurredAtUtc ON audit.SystemErrorLogs(OccurredAtUtc DESC);
    CREATE INDEX IX_SystemErrorLogs_Source_OccurredAtUtc ON audit.SystemErrorLogs(Source, OccurredAtUtc DESC);
END;
GO

IF OBJECT_ID(N'audit.AuditLogs', N'U') IS NULL
BEGIN
    CREATE TABLE audit.AuditLogs
    (
        AuditLogId          bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_AuditLogs PRIMARY KEY,
        OccurredAtUtc       datetime2(3)      NOT NULL CONSTRAINT DF_AuditLogs_OccurredAtUtc DEFAULT SYSUTCDATETIME(),
        CorrelationId       uniqueidentifier  NOT NULL,
        ActorUserId         uniqueidentifier  NULL,
        Action              nvarchar(200)     NOT NULL,
        EntityType          nvarchar(200)     NULL,
        EntityId            nvarchar(200)     NULL,
        BeforeJson          nvarchar(max)     NULL,
        AfterJson           nvarchar(max)     NULL,
        Source              varchar(30)       NOT NULL,
        IpAddress           varchar(64)       NULL,
        CONSTRAINT CK_AuditLogs_BeforeJson CHECK (BeforeJson IS NULL OR ISJSON(BeforeJson)=1),
        CONSTRAINT CK_AuditLogs_AfterJson CHECK (AfterJson IS NULL OR ISJSON(AfterJson)=1)
    );
    CREATE INDEX IX_AuditLogs_CorrelationId ON audit.AuditLogs(CorrelationId);
    CREATE INDEX IX_AuditLogs_ActorUserId_OccurredAtUtc ON audit.AuditLogs(ActorUserId, OccurredAtUtc DESC);
    CREATE INDEX IX_AuditLogs_Entity ON audit.AuditLogs(EntityType, EntityId);
END;
GO

IF OBJECT_ID(N'audit.OperationalLogs', N'U') IS NULL
BEGIN
    CREATE TABLE audit.OperationalLogs
    (
        OperationalLogId    bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_OperationalLogs PRIMARY KEY,
        OccurredAtUtc       datetime2(3)      NOT NULL CONSTRAINT DF_OperationalLogs_OccurredAtUtc DEFAULT SYSUTCDATETIME(),
        CorrelationId       uniqueidentifier  NOT NULL,
        Component           nvarchar(200)     NOT NULL,
        Operation           nvarchar(200)     NOT NULL,
        Status              varchar(30)       NOT NULL,
        ReferenceType       nvarchar(100)     NULL,
        ReferenceId         nvarchar(200)     NULL,
        DurationMs          bigint            NULL,
        DetailsJson         nvarchar(max)     NULL,
        CONSTRAINT CK_OperationalLogs_DetailsJson CHECK (DetailsJson IS NULL OR ISJSON(DetailsJson)=1)
    );
    CREATE INDEX IX_OperationalLogs_CorrelationId ON audit.OperationalLogs(CorrelationId);
    CREATE INDEX IX_OperationalLogs_OccurredAtUtc ON audit.OperationalLogs(OccurredAtUtc DESC);
END;
GO

CREATE OR ALTER PROCEDURE audit.SystemError_Log
    @ErrorReference     varchar(40),
    @CorrelationId      uniqueidentifier,
    @Source             varchar(30),
    @ErrorMessage       nvarchar(4000),
    @ExceptionType      nvarchar(500) = NULL,
    @StackTrace         nvarchar(max) = NULL,
    @Component          nvarchar(200) = NULL,
    @Operation          nvarchar(200) = NULL,
    @ProcedureName      nvarchar(256) = NULL,
    @ErrorNumber        int = NULL,
    @ErrorSeverity      int = NULL,
    @ErrorState         int = NULL,
    @ErrorLine          int = NULL,
    @ActorUserId        uniqueidentifier = NULL,
    @ConversationId     uniqueidentifier = NULL,
    @MessageId          uniqueidentifier = NULL,
    @JobId              uniqueidentifier = NULL,
    @RequestPath        nvarchar(1000) = NULL,
    @HttpMethod         varchar(20) = NULL,
    @ContextJson        nvarchar(max) = NULL,
    @HostName           nvarchar(200) = NULL,
    @ApplicationName    nvarchar(200) = NULL,
    @EnvironmentName    nvarchar(100) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        IF NULLIF(LTRIM(RTRIM(@ErrorReference)), '') IS NULL THROW 50001, 'ErrorReference is required.', 1;
        IF NULLIF(LTRIM(RTRIM(@ErrorMessage)), '') IS NULL THROW 50002, 'ErrorMessage is required.', 1;
        IF @Source NOT IN ('DATABASE','BACKEND','FRONTEND','WORKER','INTEGRATION','DEPLOYMENT') THROW 50003, 'Invalid Source.', 1;
        IF @ContextJson IS NOT NULL AND ISJSON(@ContextJson) <> 1 THROW 50004, 'ContextJson must be valid JSON.', 1;

        IF EXISTS (SELECT 1 FROM audit.SystemErrorLogs WHERE ErrorReference = @ErrorReference)
            RETURN;

        INSERT audit.SystemErrorLogs
        (
            ErrorReference, CorrelationId, Source, Component, Operation, ProcedureName,
            ErrorNumber, ErrorSeverity, ErrorState, ErrorLine, ExceptionType, ErrorMessage, StackTrace,
            ActorUserId, ConversationId, MessageId, JobId, RequestPath, HttpMethod,
            ContextJson, HostName, ApplicationName, EnvironmentName
        )
        VALUES
        (
            @ErrorReference, @CorrelationId, @Source, @Component, @Operation, @ProcedureName,
            @ErrorNumber, @ErrorSeverity, @ErrorState, @ErrorLine, @ExceptionType, @ErrorMessage, @StackTrace,
            @ActorUserId, @ConversationId, @MessageId, @JobId, @RequestPath, @HttpMethod,
            @ContextJson, @HostName, @ApplicationName, @EnvironmentName
        );
    END TRY
    BEGIN CATCH
        THROW;
    END CATCH;
END;
GO

CREATE OR ALTER PROCEDURE audit.AuditLog_Create
    @CorrelationId uniqueidentifier,
    @Action nvarchar(200),
    @ActorUserId uniqueidentifier = NULL,
    @EntityType nvarchar(200) = NULL,
    @EntityId nvarchar(200) = NULL,
    @BeforeJson nvarchar(max) = NULL,
    @AfterJson nvarchar(max) = NULL,
    @Source varchar(30) = 'BACKEND',
    @IpAddress varchar(64) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRY
        IF @BeforeJson IS NOT NULL AND ISJSON(@BeforeJson) <> 1 THROW 50005, 'BeforeJson must be valid JSON.', 1;
        IF @AfterJson IS NOT NULL AND ISJSON(@AfterJson) <> 1 THROW 50006, 'AfterJson must be valid JSON.', 1;

        INSERT audit.AuditLogs(CorrelationId, ActorUserId, Action, EntityType, EntityId, BeforeJson, AfterJson, Source, IpAddress)
        VALUES(@CorrelationId, @ActorUserId, @Action, @EntityType, @EntityId, @BeforeJson, @AfterJson, @Source, @IpAddress);
    END TRY
    BEGIN CATCH
        THROW;
    END CATCH;
END;
GO

CREATE OR ALTER PROCEDURE audit.OperationalLog_Create
    @CorrelationId uniqueidentifier,
    @Component nvarchar(200),
    @Operation nvarchar(200),
    @Status varchar(30),
    @ReferenceType nvarchar(100) = NULL,
    @ReferenceId nvarchar(200) = NULL,
    @DurationMs bigint = NULL,
    @DetailsJson nvarchar(max) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRY
        IF @DetailsJson IS NOT NULL AND ISJSON(@DetailsJson) <> 1 THROW 50007, 'DetailsJson must be valid JSON.', 1;

        INSERT audit.OperationalLogs(CorrelationId, Component, Operation, Status, ReferenceType, ReferenceId, DurationMs, DetailsJson)
        VALUES(@CorrelationId, @Component, @Operation, @Status, @ReferenceType, @ReferenceId, @DurationMs, @DetailsJson);
    END TRY
    BEGIN CATCH
        THROW;
    END CATCH;
END;
GO
