SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF SCHEMA_ID(N'project') IS NULL
    EXEC(N'CREATE SCHEMA [project] AUTHORIZATION dbo;');
GO

CREATE TABLE [project].Projects
(
    ProjectId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_Projects PRIMARY KEY,
    PublicId uniqueidentifier NOT NULL CONSTRAINT DF_Projects_PublicId DEFAULT NEWSEQUENTIALID(),
    Code varchar(100) NOT NULL,
    NameAr nvarchar(200) NOT NULL,
    NameEn nvarchar(200) NULL,
    DefaultLanguage varchar(10) NOT NULL,
    TimeZoneId varchar(100) NOT NULL,
    IsActive bit NOT NULL CONSTRAINT DF_Projects_IsActive DEFAULT (1),
    CreatedAtUtc datetime2(3) NOT NULL CONSTRAINT DF_Projects_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
    CreatedBy uniqueidentifier NULL,
    UpdatedAtUtc datetime2(3) NOT NULL CONSTRAINT DF_Projects_UpdatedAtUtc DEFAULT SYSUTCDATETIME(),
    UpdatedBy uniqueidentifier NULL,
    CONSTRAINT UQ_Projects_PublicId UNIQUE (PublicId),
    CONSTRAINT UQ_Projects_Code UNIQUE (Code)
);

CREATE TABLE [project].Channels
(
    ChannelId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_Channels PRIMARY KEY,
    PublicId uniqueidentifier NOT NULL CONSTRAINT DF_Channels_PublicId DEFAULT NEWSEQUENTIALID(),
    ProjectId bigint NOT NULL,
    Code varchar(100) NOT NULL,
    ChannelType varchar(50) NOT NULL,
    ProviderCode varchar(50) NOT NULL,
    IsActive bit NOT NULL CONSTRAINT DF_Channels_IsActive DEFAULT (1),
    ConfigurationJson nvarchar(max) NULL,
    CreatedAtUtc datetime2(3) NOT NULL CONSTRAINT DF_Channels_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
    CreatedBy uniqueidentifier NULL,
    UpdatedAtUtc datetime2(3) NOT NULL CONSTRAINT DF_Channels_UpdatedAtUtc DEFAULT SYSUTCDATETIME(),
    UpdatedBy uniqueidentifier NULL,
    CONSTRAINT UQ_Channels_PublicId UNIQUE (PublicId),
    CONSTRAINT UQ_Channels_Project_Code UNIQUE (ProjectId, Code),
    CONSTRAINT FK_Channels_Projects FOREIGN KEY (ProjectId) REFERENCES [project].Projects(ProjectId),
    CONSTRAINT CK_Channels_ChannelType CHECK (ChannelType = 'WHATSAPP'),
    CONSTRAINT CK_Channels_ProviderCode CHECK (ProviderCode = 'WAHA'),
    CONSTRAINT CK_Channels_ConfigurationJson CHECK (ConfigurationJson IS NULL OR ISJSON(ConfigurationJson) = 1)
);

CREATE TABLE [project].BusinessHours
(
    BusinessHourId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_BusinessHours PRIMARY KEY,
    ProjectId bigint NOT NULL,
    DayOfWeek tinyint NOT NULL,
    IsWorkingDay bit NOT NULL,
    StartTime time NULL,
    EndTime time NULL,
    CONSTRAINT UQ_BusinessHours_Project_Day UNIQUE (ProjectId, DayOfWeek),
    CONSTRAINT FK_BusinessHours_Projects FOREIGN KEY (ProjectId) REFERENCES [project].Projects(ProjectId),
    CONSTRAINT CK_BusinessHours_Day CHECK (DayOfWeek BETWEEN 0 AND 6),
    CONSTRAINT CK_BusinessHours_Times CHECK
    (
        (IsWorkingDay = 1 AND StartTime IS NOT NULL AND EndTime IS NOT NULL AND EndTime > StartTime)
        OR (IsWorkingDay = 0 AND StartTime IS NULL AND EndTime IS NULL)
    )
);

INSERT [identity].Permissions (Code, Name)
SELECT permission.Code, permission.Name
FROM
(
    VALUES
        ('projects.read', N'Read projects'),
        ('projects.manage', N'Manage projects'),
        ('channels.read', N'Read channels'),
        ('channels.manage', N'Manage channels')
) permission(Code, Name)
WHERE NOT EXISTS
(
    SELECT 1 FROM [identity].Permissions existing WHERE existing.Code = permission.Code
);

INSERT [identity].RolePermissions (RoleId, PermissionId)
SELECT role.RoleId, permission.PermissionId
FROM [identity].Roles role
CROSS JOIN [identity].Permissions permission
WHERE role.Code = 'ADMIN'
  AND permission.Code IN ('projects.read', 'projects.manage', 'channels.read', 'channels.manage')
  AND NOT EXISTS
  (
      SELECT 1
      FROM [identity].RolePermissions existing
      WHERE existing.RoleId = role.RoleId
        AND existing.PermissionId = permission.PermissionId
  );
GO

CREATE OR ALTER PROCEDURE [project].Project_Create
    @PublicId uniqueidentifier, @Code varchar(100), @NameAr nvarchar(200),
    @NameEn nvarchar(200) = NULL, @DefaultLanguage varchar(10), @TimeZoneId varchar(100),
    @IsActive bit, @ActorUserId uniqueidentifier = NULL,
    @CorrelationId uniqueidentifier, @ErrorReference varchar(40)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRY
        BEGIN TRANSACTION;
        INSERT [project].Projects
            (PublicId, Code, NameAr, NameEn, DefaultLanguage, TimeZoneId, IsActive, CreatedBy, UpdatedBy)
        VALUES
            (@PublicId, @Code, @NameAr, @NameEn, @DefaultLanguage, @TimeZoneId, @IsActive, @ActorUserId, @ActorUserId);
        EXEC audit.AuditLog_Create @CorrelationId, N'PROJECT.CREATE', @ActorUserId,
            N'PROJECT', @PublicId, @Source = 'BACKEND';
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        DECLARE @Message nvarchar(4000) = ERROR_MESSAGE();
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        EXEC audit.SystemError_Log @ErrorReference, @CorrelationId, 'DATABASE', @Message,
            @Component = N'Project', @Operation = N'PROJECT.CREATE';
        THROW;
    END CATCH;
END;
GO

CREATE OR ALTER PROCEDURE [project].Project_GetByPublicId
    @PublicId uniqueidentifier, @CorrelationId uniqueidentifier, @ErrorReference varchar(40)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRY
        SELECT ProjectId, PublicId, Code, NameAr, NameEn, DefaultLanguage, TimeZoneId, IsActive
        FROM [project].Projects WHERE PublicId = @PublicId;
    END TRY
    BEGIN CATCH
        DECLARE @Message nvarchar(4000) = ERROR_MESSAGE();
        EXEC audit.SystemError_Log @ErrorReference, @CorrelationId, 'DATABASE', @Message,
            @Component = N'Project', @Operation = N'PROJECT.GET';
        THROW;
    END CATCH;
END;
GO

CREATE OR ALTER PROCEDURE [project].Project_Update
    @PublicId uniqueidentifier, @Code varchar(100), @NameAr nvarchar(200),
    @NameEn nvarchar(200) = NULL, @DefaultLanguage varchar(10), @TimeZoneId varchar(100),
    @ActorUserId uniqueidentifier, @CorrelationId uniqueidentifier, @ErrorReference varchar(40)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRY
        BEGIN TRANSACTION;
        UPDATE [project].Projects
        SET Code = @Code, NameAr = @NameAr, NameEn = @NameEn,
            DefaultLanguage = @DefaultLanguage, TimeZoneId = @TimeZoneId,
            UpdatedAtUtc = SYSUTCDATETIME(), UpdatedBy = @ActorUserId
        WHERE PublicId = @PublicId;
        IF @@ROWCOUNT = 0 THROW 53001, 'Project was not found.', 1;
        EXEC audit.AuditLog_Create @CorrelationId, N'PROJECT.UPDATE', @ActorUserId,
            N'PROJECT', @PublicId, @Source = 'BACKEND';
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        DECLARE @Message nvarchar(4000) = ERROR_MESSAGE();
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        EXEC audit.SystemError_Log @ErrorReference, @CorrelationId, 'DATABASE', @Message,
            @Component = N'Project', @Operation = N'PROJECT.UPDATE';
        THROW;
    END CATCH;
END;
GO

CREATE OR ALTER PROCEDURE [project].Project_SetActive
    @PublicId uniqueidentifier, @IsActive bit, @ActorUserId uniqueidentifier,
    @CorrelationId uniqueidentifier, @ErrorReference varchar(40)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRY
        BEGIN TRANSACTION;
        UPDATE [project].Projects
        SET IsActive = @IsActive, UpdatedAtUtc = SYSUTCDATETIME(), UpdatedBy = @ActorUserId
        WHERE PublicId = @PublicId;
        IF @@ROWCOUNT = 0 THROW 53001, 'Project was not found.', 1;
        DECLARE @Action nvarchar(200) = CASE WHEN @IsActive = 1 THEN N'PROJECT.ACTIVATE' ELSE N'PROJECT.DEACTIVATE' END;
        EXEC audit.AuditLog_Create @CorrelationId, @Action, @ActorUserId,
            N'PROJECT', @PublicId, @Source = 'BACKEND';
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        DECLARE @Message nvarchar(4000) = ERROR_MESSAGE();
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        EXEC audit.SystemError_Log @ErrorReference, @CorrelationId, 'DATABASE', @Message,
            @Component = N'Project', @Operation = N'PROJECT.SET_ACTIVE';
        THROW;
    END CATCH;
END;
GO

CREATE OR ALTER PROCEDURE [project].Channel_Create
    @PublicId uniqueidentifier, @ProjectPublicId uniqueidentifier, @Code varchar(100),
    @ChannelType varchar(50), @ProviderCode varchar(50), @IsActive bit,
    @ConfigurationJson nvarchar(max) = NULL, @ActorUserId uniqueidentifier = NULL,
    @CorrelationId uniqueidentifier, @ErrorReference varchar(40)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRY
        BEGIN TRANSACTION;
        DECLARE @ProjectId bigint = (SELECT ProjectId FROM [project].Projects WHERE PublicId = @ProjectPublicId);
        IF @ProjectId IS NULL THROW 53001, 'Project was not found.', 1;
        INSERT [project].Channels
            (PublicId, ProjectId, Code, ChannelType, ProviderCode, IsActive, ConfigurationJson, CreatedBy, UpdatedBy)
        VALUES
            (@PublicId, @ProjectId, @Code, @ChannelType, @ProviderCode, @IsActive, @ConfigurationJson, @ActorUserId, @ActorUserId);
        EXEC audit.AuditLog_Create @CorrelationId, N'CHANNEL.CREATE', @ActorUserId,
            N'CHANNEL', @PublicId, @Source = 'BACKEND';
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        DECLARE @Message nvarchar(4000) = ERROR_MESSAGE();
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        EXEC audit.SystemError_Log @ErrorReference, @CorrelationId, 'DATABASE', @Message,
            @Component = N'Project', @Operation = N'CHANNEL.CREATE';
        THROW;
    END CATCH;
END;
GO

CREATE OR ALTER PROCEDURE [project].Channel_GetByPublicId
    @PublicId uniqueidentifier, @CorrelationId uniqueidentifier, @ErrorReference varchar(40)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRY
        SELECT c.ChannelId, c.PublicId, p.PublicId AS ProjectPublicId, c.Code,
               c.ChannelType, c.ProviderCode, c.IsActive, c.ConfigurationJson
        FROM [project].Channels c
        INNER JOIN [project].Projects p ON p.ProjectId = c.ProjectId
        WHERE c.PublicId = @PublicId;
    END TRY
    BEGIN CATCH
        DECLARE @Message nvarchar(4000) = ERROR_MESSAGE();
        EXEC audit.SystemError_Log @ErrorReference, @CorrelationId, 'DATABASE', @Message,
            @Component = N'Project', @Operation = N'CHANNEL.GET';
        THROW;
    END CATCH;
END;
GO

CREATE OR ALTER PROCEDURE [project].Channel_Update
    @PublicId uniqueidentifier, @Code varchar(100), @ChannelType varchar(50),
    @ProviderCode varchar(50), @ConfigurationJson nvarchar(max) = NULL,
    @ActorUserId uniqueidentifier, @CorrelationId uniqueidentifier, @ErrorReference varchar(40)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRY
        BEGIN TRANSACTION;
        UPDATE [project].Channels
        SET Code = @Code, ChannelType = @ChannelType, ProviderCode = @ProviderCode,
            ConfigurationJson = @ConfigurationJson, UpdatedAtUtc = SYSUTCDATETIME(), UpdatedBy = @ActorUserId
        WHERE PublicId = @PublicId;
        IF @@ROWCOUNT = 0 THROW 53002, 'Channel was not found.', 1;
        EXEC audit.AuditLog_Create @CorrelationId, N'CHANNEL.UPDATE', @ActorUserId,
            N'CHANNEL', @PublicId, @Source = 'BACKEND';
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        DECLARE @Message nvarchar(4000) = ERROR_MESSAGE();
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        EXEC audit.SystemError_Log @ErrorReference, @CorrelationId, 'DATABASE', @Message,
            @Component = N'Project', @Operation = N'CHANNEL.UPDATE';
        THROW;
    END CATCH;
END;
GO

CREATE OR ALTER PROCEDURE [project].Channel_SetActive
    @PublicId uniqueidentifier, @IsActive bit, @ActorUserId uniqueidentifier,
    @CorrelationId uniqueidentifier, @ErrorReference varchar(40)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRY
        BEGIN TRANSACTION;
        UPDATE [project].Channels
        SET IsActive = @IsActive, UpdatedAtUtc = SYSUTCDATETIME(), UpdatedBy = @ActorUserId
        WHERE PublicId = @PublicId;
        IF @@ROWCOUNT = 0 THROW 53002, 'Channel was not found.', 1;
        DECLARE @Action nvarchar(200) = CASE WHEN @IsActive = 1 THEN N'CHANNEL.ACTIVATE' ELSE N'CHANNEL.DEACTIVATE' END;
        EXEC audit.AuditLog_Create @CorrelationId, @Action, @ActorUserId,
            N'CHANNEL', @PublicId, @Source = 'BACKEND';
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        DECLARE @Message nvarchar(4000) = ERROR_MESSAGE();
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        EXEC audit.SystemError_Log @ErrorReference, @CorrelationId, 'DATABASE', @Message,
            @Component = N'Project', @Operation = N'CHANNEL.SET_ACTIVE';
        THROW;
    END CATCH;
END;
GO

CREATE OR ALTER PROCEDURE [project].BusinessHours_GetByProject
    @ProjectPublicId uniqueidentifier, @CorrelationId uniqueidentifier, @ErrorReference varchar(40)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRY
        SELECT h.DayOfWeek, h.IsWorkingDay, h.StartTime, h.EndTime
        FROM [project].BusinessHours h
        INNER JOIN [project].Projects p ON p.ProjectId = h.ProjectId
        WHERE p.PublicId = @ProjectPublicId
        ORDER BY h.DayOfWeek;
    END TRY
    BEGIN CATCH
        DECLARE @Message nvarchar(4000) = ERROR_MESSAGE();
        EXEC audit.SystemError_Log @ErrorReference, @CorrelationId, 'DATABASE', @Message,
            @Component = N'Project', @Operation = N'BUSINESS_HOURS.GET';
        THROW;
    END CATCH;
END;
GO

CREATE OR ALTER PROCEDURE [project].BusinessHours_Upsert
    @ProjectPublicId uniqueidentifier, @DayOfWeek tinyint, @IsWorkingDay bit,
    @StartTime time = NULL, @EndTime time = NULL, @ActorUserId uniqueidentifier,
    @CorrelationId uniqueidentifier, @ErrorReference varchar(40)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRY
        BEGIN TRANSACTION;
        DECLARE @ProjectId bigint = (SELECT ProjectId FROM [project].Projects WHERE PublicId = @ProjectPublicId);
        IF @ProjectId IS NULL THROW 53001, 'Project was not found.', 1;
        UPDATE [project].BusinessHours
        SET IsWorkingDay = @IsWorkingDay, StartTime = @StartTime, EndTime = @EndTime
        WHERE ProjectId = @ProjectId AND DayOfWeek = @DayOfWeek;
        IF @@ROWCOUNT = 0
            INSERT [project].BusinessHours (ProjectId, DayOfWeek, IsWorkingDay, StartTime, EndTime)
            VALUES (@ProjectId, @DayOfWeek, @IsWorkingDay, @StartTime, @EndTime);
        EXEC audit.AuditLog_Create @CorrelationId, N'PROJECT.BUSINESS_HOURS_UPDATE', @ActorUserId,
            N'PROJECT', @ProjectPublicId, @Source = 'BACKEND';
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        DECLARE @Message nvarchar(4000) = ERROR_MESSAGE();
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        EXEC audit.SystemError_Log @ErrorReference, @CorrelationId, 'DATABASE', @Message,
            @Component = N'Project', @Operation = N'BUSINESS_HOURS.UPSERT';
        THROW;
    END CATCH;
END;
GO
