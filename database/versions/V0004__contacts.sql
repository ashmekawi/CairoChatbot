SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF SCHEMA_ID(N'contact') IS NULL
    EXEC(N'CREATE SCHEMA [contact] AUTHORIZATION dbo;');
GO

CREATE TABLE [contact].Contacts
(
    ContactId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_Contacts PRIMARY KEY,
    PublicId uniqueidentifier NOT NULL CONSTRAINT DF_Contacts_PublicId DEFAULT NEWSEQUENTIALID(),
    DisplayName nvarchar(200) NULL,
    PreferredLanguage varchar(10) NULL,
    IsActive bit NOT NULL CONSTRAINT DF_Contacts_IsActive DEFAULT (1),
    CreatedAtUtc datetime2(3) NOT NULL CONSTRAINT DF_Contacts_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
    CreatedBy uniqueidentifier NULL,
    UpdatedAtUtc datetime2(3) NOT NULL CONSTRAINT DF_Contacts_UpdatedAtUtc DEFAULT SYSUTCDATETIME(),
    UpdatedBy uniqueidentifier NULL,
    CONSTRAINT UQ_Contacts_PublicId UNIQUE (PublicId)
);

CREATE TABLE [contact].ChannelIdentities
(
    ChannelIdentityId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_ChannelIdentities PRIMARY KEY,
    PublicId uniqueidentifier NOT NULL CONSTRAINT DF_ChannelIdentities_PublicId DEFAULT NEWSEQUENTIALID(),
    ContactId bigint NOT NULL,
    ChannelId bigint NOT NULL,
    ExternalId nvarchar(200) NOT NULL,
    NormalizedAddress varchar(100) NOT NULL,
    DisplayAddress nvarchar(200) NULL,
    IsVerified bit NOT NULL CONSTRAINT DF_ChannelIdentities_IsVerified DEFAULT (0),
    VerifiedAtUtc datetime2(3) NULL,
    IsActive bit NOT NULL CONSTRAINT DF_ChannelIdentities_IsActive DEFAULT (1),
    LastSeenAtUtc datetime2(3) NULL,
    CreatedAtUtc datetime2(3) NOT NULL CONSTRAINT DF_ChannelIdentities_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
    CreatedBy uniqueidentifier NULL,
    UpdatedAtUtc datetime2(3) NOT NULL CONSTRAINT DF_ChannelIdentities_UpdatedAtUtc DEFAULT SYSUTCDATETIME(),
    UpdatedBy uniqueidentifier NULL,
    CONSTRAINT UQ_ChannelIdentities_PublicId UNIQUE (PublicId),
    CONSTRAINT UQ_ChannelIdentities_Channel_External UNIQUE (ChannelId, ExternalId),
    CONSTRAINT FK_ChannelIdentities_Contacts FOREIGN KEY (ContactId) REFERENCES [contact].Contacts(ContactId),
    CONSTRAINT FK_ChannelIdentities_Channels FOREIGN KEY (ChannelId) REFERENCES [project].Channels(ChannelId),
    CONSTRAINT CK_ChannelIdentities_NormalizedAddress CHECK (LEN(NormalizedAddress) > 0),
    CONSTRAINT CK_ChannelIdentities_Verification CHECK
    (
        (IsVerified = 0 AND VerifiedAtUtc IS NULL)
        OR (IsVerified = 1 AND VerifiedAtUtc IS NOT NULL)
    )
);

INSERT [identity].Permissions (Code, Name)
SELECT permission.Code, permission.Name
FROM (VALUES ('contacts.read', N'Read contacts'), ('contacts.manage', N'Manage contacts')) permission(Code, Name)
WHERE NOT EXISTS
(
    SELECT 1 FROM [identity].Permissions existing WHERE existing.Code = permission.Code
);

INSERT [identity].RolePermissions (RoleId, PermissionId)
SELECT role.RoleId, permission.PermissionId
FROM [identity].Roles role
CROSS JOIN [identity].Permissions permission
WHERE role.Code = 'ADMIN'
  AND permission.Code IN ('contacts.read', 'contacts.manage')
  AND NOT EXISTS
  (
      SELECT 1 FROM [identity].RolePermissions existing
      WHERE existing.RoleId = role.RoleId AND existing.PermissionId = permission.PermissionId
  );
GO

CREATE OR ALTER PROCEDURE [contact].Contact_Create
    @PublicId uniqueidentifier, @DisplayName nvarchar(200) = NULL,
    @PreferredLanguage varchar(10) = NULL, @IsActive bit,
    @ActorUserId uniqueidentifier = NULL, @CorrelationId uniqueidentifier, @ErrorReference varchar(40)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRY
        BEGIN TRANSACTION;
        INSERT [contact].Contacts
            (PublicId, DisplayName, PreferredLanguage, IsActive, CreatedBy, UpdatedBy)
        VALUES
            (@PublicId, @DisplayName, @PreferredLanguage, @IsActive, @ActorUserId, @ActorUserId);
        EXEC audit.AuditLog_Create @CorrelationId, N'CONTACT.CREATE', @ActorUserId,
            N'CONTACT', @PublicId, @Source = 'BACKEND';
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        DECLARE @Message nvarchar(4000) = ERROR_MESSAGE();
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        EXEC audit.SystemError_Log @ErrorReference, @CorrelationId, 'DATABASE', @Message,
            @Component = N'Contact', @Operation = N'CONTACT.CREATE';
        THROW;
    END CATCH;
END;
GO

CREATE OR ALTER PROCEDURE [contact].Contact_GetByPublicId
    @PublicId uniqueidentifier, @CorrelationId uniqueidentifier, @ErrorReference varchar(40)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRY
        SELECT ContactId, PublicId, DisplayName, PreferredLanguage, IsActive
        FROM [contact].Contacts WHERE PublicId = @PublicId;
    END TRY
    BEGIN CATCH
        DECLARE @Message nvarchar(4000) = ERROR_MESSAGE();
        EXEC audit.SystemError_Log @ErrorReference, @CorrelationId, 'DATABASE', @Message,
            @Component = N'Contact', @Operation = N'CONTACT.GET';
        THROW;
    END CATCH;
END;
GO

CREATE OR ALTER PROCEDURE [contact].Contact_Update
    @PublicId uniqueidentifier, @DisplayName nvarchar(200) = NULL,
    @PreferredLanguage varchar(10) = NULL, @ActorUserId uniqueidentifier,
    @CorrelationId uniqueidentifier, @ErrorReference varchar(40)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRY
        BEGIN TRANSACTION;
        UPDATE [contact].Contacts
        SET DisplayName = @DisplayName, PreferredLanguage = @PreferredLanguage,
            UpdatedAtUtc = SYSUTCDATETIME(), UpdatedBy = @ActorUserId
        WHERE PublicId = @PublicId;
        IF @@ROWCOUNT = 0 THROW 54001, 'Contact was not found.', 1;
        EXEC audit.AuditLog_Create @CorrelationId, N'CONTACT.UPDATE', @ActorUserId,
            N'CONTACT', @PublicId, @Source = 'BACKEND';
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        DECLARE @Message nvarchar(4000) = ERROR_MESSAGE();
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        EXEC audit.SystemError_Log @ErrorReference, @CorrelationId, 'DATABASE', @Message,
            @Component = N'Contact', @Operation = N'CONTACT.UPDATE';
        THROW;
    END CATCH;
END;
GO

CREATE OR ALTER PROCEDURE [contact].Contact_SetActive
    @PublicId uniqueidentifier, @IsActive bit, @ActorUserId uniqueidentifier,
    @CorrelationId uniqueidentifier, @ErrorReference varchar(40)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRY
        BEGIN TRANSACTION;
        UPDATE [contact].Contacts
        SET IsActive = @IsActive, UpdatedAtUtc = SYSUTCDATETIME(), UpdatedBy = @ActorUserId
        WHERE PublicId = @PublicId;
        IF @@ROWCOUNT = 0 THROW 54001, 'Contact was not found.', 1;
        DECLARE @Action nvarchar(200) = CASE WHEN @IsActive = 1 THEN N'CONTACT.ACTIVATE' ELSE N'CONTACT.DEACTIVATE' END;
        EXEC audit.AuditLog_Create @CorrelationId, @Action, @ActorUserId,
            N'CONTACT', @PublicId, @Source = 'BACKEND';
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        DECLARE @Message nvarchar(4000) = ERROR_MESSAGE();
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        EXEC audit.SystemError_Log @ErrorReference, @CorrelationId, 'DATABASE', @Message,
            @Component = N'Contact', @Operation = N'CONTACT.SET_ACTIVE';
        THROW;
    END CATCH;
END;
GO

CREATE OR ALTER PROCEDURE [contact].ChannelIdentity_Create
    @PublicId uniqueidentifier, @ContactPublicId uniqueidentifier, @ChannelPublicId uniqueidentifier,
    @ExternalId nvarchar(200), @NormalizedAddress varchar(100), @DisplayAddress nvarchar(200) = NULL,
    @IsVerified bit, @IsActive bit, @ActorUserId uniqueidentifier = NULL,
    @CorrelationId uniqueidentifier, @ErrorReference varchar(40)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRY
        BEGIN TRANSACTION;
        DECLARE @ContactId bigint = (SELECT ContactId FROM [contact].Contacts WHERE PublicId = @ContactPublicId);
        DECLARE @ChannelId bigint = (SELECT ChannelId FROM [project].Channels WHERE PublicId = @ChannelPublicId);
        IF @ContactId IS NULL THROW 54001, 'Contact was not found.', 1;
        IF @ChannelId IS NULL THROW 54002, 'Channel was not found.', 1;
        INSERT [contact].ChannelIdentities
            (PublicId, ContactId, ChannelId, ExternalId, NormalizedAddress, DisplayAddress,
             IsVerified, VerifiedAtUtc, IsActive, CreatedBy, UpdatedBy)
        VALUES
            (@PublicId, @ContactId, @ChannelId, @ExternalId, @NormalizedAddress, @DisplayAddress,
             @IsVerified, CASE WHEN @IsVerified = 1 THEN SYSUTCDATETIME() ELSE NULL END,
             @IsActive, @ActorUserId, @ActorUserId);
        EXEC audit.AuditLog_Create @CorrelationId, N'CONTACT.IDENTITY.CREATE', @ActorUserId,
            N'CONTACT_IDENTITY', @PublicId, @Source = 'BACKEND';
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        DECLARE @Message nvarchar(4000) = ERROR_MESSAGE();
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        EXEC audit.SystemError_Log @ErrorReference, @CorrelationId, 'DATABASE', @Message,
            @Component = N'Contact', @Operation = N'CONTACT.IDENTITY.CREATE';
        THROW;
    END CATCH;
END;
GO

CREATE OR ALTER PROCEDURE [contact].ChannelIdentity_GetByPublicId
    @PublicId uniqueidentifier, @CorrelationId uniqueidentifier, @ErrorReference varchar(40)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRY
        SELECT ci.ChannelIdentityId, ci.PublicId, c.PublicId AS ContactPublicId,
               ch.PublicId AS ChannelPublicId, ci.ExternalId, ci.NormalizedAddress,
               ci.DisplayAddress, ci.IsVerified, ci.VerifiedAtUtc, ci.IsActive, ci.LastSeenAtUtc
        FROM [contact].ChannelIdentities ci
        INNER JOIN [contact].Contacts c ON c.ContactId = ci.ContactId
        INNER JOIN [project].Channels ch ON ch.ChannelId = ci.ChannelId
        WHERE ci.PublicId = @PublicId;
    END TRY
    BEGIN CATCH
        DECLARE @Message nvarchar(4000) = ERROR_MESSAGE();
        EXEC audit.SystemError_Log @ErrorReference, @CorrelationId, 'DATABASE', @Message,
            @Component = N'Contact', @Operation = N'CONTACT.IDENTITY.GET';
        THROW;
    END CATCH;
END;
GO

CREATE OR ALTER PROCEDURE [contact].ChannelIdentity_Update
    @PublicId uniqueidentifier, @ExternalId nvarchar(200), @NormalizedAddress varchar(100),
    @DisplayAddress nvarchar(200) = NULL, @ActorUserId uniqueidentifier,
    @CorrelationId uniqueidentifier, @ErrorReference varchar(40)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRY
        BEGIN TRANSACTION;
        UPDATE [contact].ChannelIdentities
        SET ExternalId = @ExternalId, NormalizedAddress = @NormalizedAddress,
            DisplayAddress = @DisplayAddress, UpdatedAtUtc = SYSUTCDATETIME(), UpdatedBy = @ActorUserId
        WHERE PublicId = @PublicId;
        IF @@ROWCOUNT = 0 THROW 54003, 'Channel identity was not found.', 1;
        EXEC audit.AuditLog_Create @CorrelationId, N'CONTACT.IDENTITY.UPDATE', @ActorUserId,
            N'CONTACT_IDENTITY', @PublicId, @Source = 'BACKEND';
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        DECLARE @Message nvarchar(4000) = ERROR_MESSAGE();
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        EXEC audit.SystemError_Log @ErrorReference, @CorrelationId, 'DATABASE', @Message,
            @Component = N'Contact', @Operation = N'CONTACT.IDENTITY.UPDATE';
        THROW;
    END CATCH;
END;
GO

CREATE OR ALTER PROCEDURE [contact].ChannelIdentity_SetActive
    @PublicId uniqueidentifier, @IsActive bit, @ActorUserId uniqueidentifier,
    @CorrelationId uniqueidentifier, @ErrorReference varchar(40)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRY
        BEGIN TRANSACTION;
        UPDATE [contact].ChannelIdentities
        SET IsActive = @IsActive, UpdatedAtUtc = SYSUTCDATETIME(), UpdatedBy = @ActorUserId
        WHERE PublicId = @PublicId;
        IF @@ROWCOUNT = 0 THROW 54003, 'Channel identity was not found.', 1;
        DECLARE @Action nvarchar(200) = CASE WHEN @IsActive = 1 THEN N'CONTACT.IDENTITY.ACTIVATE' ELSE N'CONTACT.IDENTITY.DEACTIVATE' END;
        EXEC audit.AuditLog_Create @CorrelationId, @Action, @ActorUserId,
            N'CONTACT_IDENTITY', @PublicId, @Source = 'BACKEND';
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        DECLARE @Message nvarchar(4000) = ERROR_MESSAGE();
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        EXEC audit.SystemError_Log @ErrorReference, @CorrelationId, 'DATABASE', @Message,
            @Component = N'Contact', @Operation = N'CONTACT.IDENTITY.SET_ACTIVE';
        THROW;
    END CATCH;
END;
GO

CREATE OR ALTER PROCEDURE [contact].ChannelIdentity_SetVerified
    @PublicId uniqueidentifier, @IsVerified bit, @ActorUserId uniqueidentifier,
    @CorrelationId uniqueidentifier, @ErrorReference varchar(40)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRY
        BEGIN TRANSACTION;
        UPDATE [contact].ChannelIdentities
        SET IsVerified = @IsVerified,
            VerifiedAtUtc = CASE WHEN @IsVerified = 1 THEN SYSUTCDATETIME() ELSE NULL END,
            UpdatedAtUtc = SYSUTCDATETIME(), UpdatedBy = @ActorUserId
        WHERE PublicId = @PublicId;
        IF @@ROWCOUNT = 0 THROW 54003, 'Channel identity was not found.', 1;
        DECLARE @Action nvarchar(200) = CASE WHEN @IsVerified = 1 THEN N'CONTACT.IDENTITY.VERIFY' ELSE N'CONTACT.IDENTITY.UNVERIFY' END;
        EXEC audit.AuditLog_Create @CorrelationId, @Action, @ActorUserId,
            N'CONTACT_IDENTITY', @PublicId, @Source = 'BACKEND';
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        DECLARE @Message nvarchar(4000) = ERROR_MESSAGE();
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        EXEC audit.SystemError_Log @ErrorReference, @CorrelationId, 'DATABASE', @Message,
            @Component = N'Contact', @Operation = N'CONTACT.IDENTITY.SET_VERIFIED';
        THROW;
    END CATCH;
END;
GO

CREATE OR ALTER PROCEDURE [contact].ChannelIdentity_GetByExternalId
    @ChannelPublicId uniqueidentifier, @ExternalId nvarchar(200),
    @CorrelationId uniqueidentifier, @ErrorReference varchar(40)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRY
        SELECT ci.ChannelIdentityId, ci.PublicId, c.PublicId AS ContactPublicId,
               ch.PublicId AS ChannelPublicId, ci.ExternalId, ci.NormalizedAddress,
               ci.DisplayAddress, ci.IsVerified, ci.VerifiedAtUtc, ci.IsActive, ci.LastSeenAtUtc
        FROM [contact].ChannelIdentities ci
        INNER JOIN [contact].Contacts c ON c.ContactId = ci.ContactId
        INNER JOIN [project].Channels ch ON ch.ChannelId = ci.ChannelId
        WHERE ch.PublicId = @ChannelPublicId AND ci.ExternalId = @ExternalId;
    END TRY
    BEGIN CATCH
        DECLARE @Message nvarchar(4000) = ERROR_MESSAGE();
        EXEC audit.SystemError_Log @ErrorReference, @CorrelationId, 'DATABASE', @Message,
            @Component = N'Contact', @Operation = N'CONTACT.IDENTITY.GET_BY_EXTERNAL_ID';
        THROW;
    END CATCH;
END;
GO
