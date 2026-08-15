SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF SCHEMA_ID(N'identity') IS NULL
    EXEC(N'CREATE SCHEMA identity AUTHORIZATION dbo;');
GO

CREATE TABLE identity.Users
(
    UserId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_Users PRIMARY KEY,
    PublicId uniqueidentifier NOT NULL CONSTRAINT DF_Users_PublicId DEFAULT NEWSEQUENTIALID(),
    Username nvarchar(100) NOT NULL,
    DisplayName nvarchar(200) NOT NULL,
    PasswordHash nvarchar(1000) NOT NULL,
    IsActive bit NOT NULL CONSTRAINT DF_Users_IsActive DEFAULT (1),
    FailedLoginCount int NOT NULL CONSTRAINT DF_Users_FailedLoginCount DEFAULT (0),
    LockedUntilUtc datetime2(3) NULL,
    CreatedAtUtc datetime2(3) NOT NULL CONSTRAINT DF_Users_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
    CreatedBy uniqueidentifier NULL,
    UpdatedAtUtc datetime2(3) NOT NULL CONSTRAINT DF_Users_UpdatedAtUtc DEFAULT SYSUTCDATETIME(),
    UpdatedBy uniqueidentifier NULL,
    CONSTRAINT UQ_Users_PublicId UNIQUE (PublicId),
    CONSTRAINT UQ_Users_Username UNIQUE (Username),
    CONSTRAINT CK_Users_FailedLoginCount CHECK (FailedLoginCount >= 0)
);

CREATE TABLE identity.Roles
(
    RoleId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Roles PRIMARY KEY,
    PublicId uniqueidentifier NOT NULL CONSTRAINT DF_Roles_PublicId DEFAULT NEWSEQUENTIALID(),
    Code varchar(100) NOT NULL,
    Name nvarchar(200) NOT NULL,
    IsActive bit NOT NULL CONSTRAINT DF_Roles_IsActive DEFAULT (1),
    CONSTRAINT UQ_Roles_PublicId UNIQUE (PublicId),
    CONSTRAINT UQ_Roles_Code UNIQUE (Code)
);

CREATE TABLE identity.Permissions
(
    PermissionId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Permissions PRIMARY KEY,
    Code varchar(150) NOT NULL,
    Name nvarchar(200) NOT NULL,
    IsActive bit NOT NULL CONSTRAINT DF_Permissions_IsActive DEFAULT (1),
    CONSTRAINT UQ_Permissions_Code UNIQUE (Code)
);

CREATE TABLE identity.UserRoles
(
    UserId bigint NOT NULL,
    RoleId int NOT NULL,
    CONSTRAINT PK_UserRoles PRIMARY KEY (UserId, RoleId),
    CONSTRAINT FK_UserRoles_Users FOREIGN KEY (UserId) REFERENCES identity.Users(UserId),
    CONSTRAINT FK_UserRoles_Roles FOREIGN KEY (RoleId) REFERENCES identity.Roles(RoleId)
);

CREATE TABLE identity.RolePermissions
(
    RoleId int NOT NULL,
    PermissionId int NOT NULL,
    CONSTRAINT PK_RolePermissions PRIMARY KEY (RoleId, PermissionId),
    CONSTRAINT FK_RolePermissions_Roles FOREIGN KEY (RoleId) REFERENCES identity.Roles(RoleId),
    CONSTRAINT FK_RolePermissions_Permissions FOREIGN KEY (PermissionId) REFERENCES identity.Permissions(PermissionId)
);

INSERT identity.Permissions (Code, Name)
VALUES
    ('users.read', N'Read users'),
    ('users.create', N'Create users'),
    ('users.activate', N'Activate or deactivate users'),
    ('users.password.reset', N'Reset user passwords'),
    ('users.roles.manage', N'Manage user roles');

IF EXISTS (SELECT 1 FROM identity.Roles WHERE Code = 'ADMIN')
BEGIN
    INSERT identity.RolePermissions (RoleId, PermissionId)
    SELECT role.RoleId, permission.PermissionId
    FROM identity.Roles role
    CROSS JOIN identity.Permissions permission
    WHERE role.Code = 'ADMIN'
      AND permission.Code IN
      (
          'users.read',
          'users.create',
          'users.activate',
          'users.password.reset',
          'users.roles.manage'
      );
END;

CREATE TABLE identity.RefreshTokens
(
    RefreshTokenId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_RefreshTokens PRIMARY KEY,
    UserId bigint NOT NULL,
    TokenHash char(64) NOT NULL,
    CreatedAtUtc datetime2(3) NOT NULL CONSTRAINT DF_RefreshTokens_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
    ExpiresAtUtc datetime2(3) NOT NULL,
    RevokedAtUtc datetime2(3) NULL,
    ReplacedByTokenId bigint NULL,
    CONSTRAINT UQ_RefreshTokens_TokenHash UNIQUE (TokenHash),
    CONSTRAINT FK_RefreshTokens_Users FOREIGN KEY (UserId) REFERENCES identity.Users(UserId),
    CONSTRAINT FK_RefreshTokens_ReplacedBy FOREIGN KEY (ReplacedByTokenId) REFERENCES identity.RefreshTokens(RefreshTokenId),
    CONSTRAINT CK_RefreshTokens_Expiry CHECK (ExpiresAtUtc > CreatedAtUtc)
);

CREATE INDEX IX_RefreshTokens_UserId ON identity.RefreshTokens(UserId);
GO

CREATE OR ALTER PROCEDURE identity.User_Create
    @PublicId uniqueidentifier,
    @Username nvarchar(100),
    @DisplayName nvarchar(200),
    @PasswordHash nvarchar(1000),
    @IsActive bit,
    @ActorUserId uniqueidentifier = NULL,
    @CorrelationId uniqueidentifier,
    @ErrorReference varchar(40)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRY
        BEGIN TRANSACTION;
        INSERT identity.Users
            (PublicId, Username, DisplayName, PasswordHash, IsActive, CreatedBy, UpdatedBy)
        VALUES
            (@PublicId, @Username, @DisplayName, @PasswordHash, @IsActive, @ActorUserId, @ActorUserId);

        EXEC audit.AuditLog_Create
            @CorrelationId = @CorrelationId,
            @Action = N'USER.CREATE',
            @ActorUserId = @ActorUserId,
            @EntityType = N'USER',
            @EntityId = @PublicId,
            @Source = 'BACKEND';
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        DECLARE @Number int = ERROR_NUMBER(), @Severity int = ERROR_SEVERITY(),
                @State int = ERROR_STATE(), @Line int = ERROR_LINE(),
                @Message nvarchar(4000) = ERROR_MESSAGE(), @Procedure nvarchar(256) = ERROR_PROCEDURE();
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        EXEC audit.SystemError_Log @ErrorReference, @CorrelationId, 'DATABASE', @Message,
            @Component = N'Identity', @Operation = N'USER.CREATE', @ProcedureName = @Procedure,
            @ErrorNumber = @Number, @ErrorSeverity = @Severity, @ErrorState = @State, @ErrorLine = @Line;
        THROW;
    END CATCH;
END;
GO

CREATE OR ALTER PROCEDURE identity.User_GetByUsername
    @Username nvarchar(100), @CorrelationId uniqueidentifier, @ErrorReference varchar(40)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRY
        SELECT UserId, PublicId, Username, DisplayName, PasswordHash, IsActive,
               FailedLoginCount, LockedUntilUtc, CreatedAtUtc, CreatedBy, UpdatedAtUtc, UpdatedBy
        FROM identity.Users
        WHERE Username = @Username;
    END TRY
    BEGIN CATCH
        DECLARE @Message nvarchar(4000) = ERROR_MESSAGE();
        DECLARE @Number int = ERROR_NUMBER();
        EXEC audit.SystemError_Log @ErrorReference, @CorrelationId, 'DATABASE', @Message,
            @Component = N'Identity', @Operation = N'USER.GET_BY_USERNAME', @ProcedureName = N'identity.User_GetByUsername',
            @ErrorNumber = @Number;
        THROW;
    END CATCH;
END;
GO

CREATE OR ALTER PROCEDURE identity.User_GetByPublicId
    @PublicId uniqueidentifier, @CorrelationId uniqueidentifier, @ErrorReference varchar(40)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRY
        SELECT UserId, PublicId, Username, DisplayName, IsActive,
               FailedLoginCount, LockedUntilUtc, CreatedAtUtc, CreatedBy, UpdatedAtUtc, UpdatedBy
        FROM identity.Users
        WHERE PublicId = @PublicId;
    END TRY
    BEGIN CATCH
        DECLARE @Message nvarchar(4000) = ERROR_MESSAGE();
        EXEC audit.SystemError_Log @ErrorReference, @CorrelationId, 'DATABASE', @Message,
            @Component = N'Identity', @Operation = N'USER.GET_BY_PUBLIC_ID', @ProcedureName = N'identity.User_GetByPublicId';
        THROW;
    END CATCH;
END;
GO

CREATE OR ALTER PROCEDURE identity.User_SetActive
    @PublicId uniqueidentifier, @IsActive bit, @ActorUserId uniqueidentifier,
    @CorrelationId uniqueidentifier, @ErrorReference varchar(40)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRY
        BEGIN TRANSACTION;
        UPDATE identity.Users
        SET IsActive = @IsActive, UpdatedAtUtc = SYSUTCDATETIME(), UpdatedBy = @ActorUserId
        WHERE PublicId = @PublicId;
        IF @@ROWCOUNT = 0 THROW 52001, 'User was not found.', 1;
        DECLARE @AuditAction nvarchar(200) = CASE
            WHEN @IsActive = 1 THEN N'USER.ACTIVATE'
            ELSE N'USER.DEACTIVATE'
        END;
        EXEC audit.AuditLog_Create @CorrelationId, @Action = @AuditAction,
            @ActorUserId = @ActorUserId, @EntityType = N'USER', @EntityId = @PublicId, @Source = 'BACKEND';
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        DECLARE @Message nvarchar(4000) = ERROR_MESSAGE();
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        EXEC audit.SystemError_Log @ErrorReference, @CorrelationId, 'DATABASE', @Message,
            @Component = N'Identity', @Operation = N'USER.SET_ACTIVE';
        THROW;
    END CATCH;
END;
GO

CREATE OR ALTER PROCEDURE identity.User_SetPassword
    @PublicId uniqueidentifier, @PasswordHash nvarchar(1000), @ActorUserId uniqueidentifier,
    @CorrelationId uniqueidentifier, @ErrorReference varchar(40)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRY
        BEGIN TRANSACTION;
        UPDATE identity.Users
        SET PasswordHash = @PasswordHash, FailedLoginCount = 0, LockedUntilUtc = NULL,
            UpdatedAtUtc = SYSUTCDATETIME(), UpdatedBy = @ActorUserId
        WHERE PublicId = @PublicId;
        IF @@ROWCOUNT = 0 THROW 52001, 'User was not found.', 1;
        EXEC audit.AuditLog_Create @CorrelationId, N'USER.PASSWORD_RESET', @ActorUserId,
            N'USER', @PublicId, @Source = 'BACKEND';
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        DECLARE @Message nvarchar(4000) = ERROR_MESSAGE();
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        EXEC audit.SystemError_Log @ErrorReference, @CorrelationId, 'DATABASE', @Message,
            @Component = N'Identity', @Operation = N'USER.SET_PASSWORD';
        THROW;
    END CATCH;
END;
GO

CREATE OR ALTER PROCEDURE identity.User_RecordLoginSuccess
    @UserId bigint, @CorrelationId uniqueidentifier, @ErrorReference varchar(40)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRY
        BEGIN TRANSACTION;
        UPDATE identity.Users SET FailedLoginCount = 0, LockedUntilUtc = NULL, UpdatedAtUtc = SYSUTCDATETIME()
        WHERE UserId = @UserId;
        EXEC audit.AuditLog_Create @CorrelationId, N'AUTH.LOGIN_SUCCESS', @EntityType = N'USER',
            @EntityId = @UserId, @Source = 'BACKEND';
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        DECLARE @Message nvarchar(4000) = ERROR_MESSAGE();
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        EXEC audit.SystemError_Log @ErrorReference, @CorrelationId, 'DATABASE', @Message,
            @Component = N'Identity', @Operation = N'AUTH.LOGIN_SUCCESS';
        THROW;
    END CATCH;
END;
GO

CREATE OR ALTER PROCEDURE identity.User_RecordLoginFailure
    @UserId bigint, @MaximumFailedAttempts int, @LockoutMinutes int,
    @CorrelationId uniqueidentifier, @ErrorReference varchar(40)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRY
        BEGIN TRANSACTION;
        UPDATE identity.Users
        SET FailedLoginCount = FailedLoginCount + 1,
            LockedUntilUtc = CASE WHEN FailedLoginCount + 1 >= @MaximumFailedAttempts
                THEN DATEADD(MINUTE, @LockoutMinutes, SYSUTCDATETIME()) ELSE LockedUntilUtc END,
            UpdatedAtUtc = SYSUTCDATETIME()
        WHERE UserId = @UserId;
        EXEC audit.AuditLog_Create @CorrelationId, N'AUTH.LOGIN_FAILED', @EntityType = N'USER',
            @EntityId = @UserId, @Source = 'BACKEND';
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        DECLARE @Message nvarchar(4000) = ERROR_MESSAGE();
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        EXEC audit.SystemError_Log @ErrorReference, @CorrelationId, 'DATABASE', @Message,
            @Component = N'Identity', @Operation = N'AUTH.LOGIN_FAILED';
        THROW;
    END CATCH;
END;
GO

CREATE OR ALTER PROCEDURE identity.Role_GetAll
    @CorrelationId uniqueidentifier, @ErrorReference varchar(40)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRY
        SELECT PublicId, Code, Name, IsActive FROM identity.Roles ORDER BY Code;
    END TRY
    BEGIN CATCH
        DECLARE @Message nvarchar(4000) = ERROR_MESSAGE();
        EXEC audit.SystemError_Log @ErrorReference, @CorrelationId, 'DATABASE', @Message,
            @Component = N'Identity', @Operation = N'ROLE.GET_ALL';
        THROW;
    END CATCH;
END;
GO

CREATE OR ALTER PROCEDURE identity.Permission_GetByUser
    @UserId bigint, @CorrelationId uniqueidentifier, @ErrorReference varchar(40)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRY
        SELECT DISTINCT p.Code, p.Name
        FROM identity.UserRoles ur
        INNER JOIN identity.Roles r ON r.RoleId = ur.RoleId AND r.IsActive = 1
        INNER JOIN identity.RolePermissions rp ON rp.RoleId = r.RoleId
        INNER JOIN identity.Permissions p ON p.PermissionId = rp.PermissionId AND p.IsActive = 1
        WHERE ur.UserId = @UserId
        ORDER BY p.Code;
    END TRY
    BEGIN CATCH
        DECLARE @Message nvarchar(4000) = ERROR_MESSAGE();
        EXEC audit.SystemError_Log @ErrorReference, @CorrelationId, 'DATABASE', @Message,
            @Component = N'Identity', @Operation = N'PERMISSION.GET_BY_USER';
        THROW;
    END CATCH;
END;
GO

CREATE OR ALTER PROCEDURE identity.UserRole_Assign
    @UserPublicId uniqueidentifier, @RoleCode varchar(100), @ActorUserId uniqueidentifier,
    @CorrelationId uniqueidentifier, @ErrorReference varchar(40)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRY
        BEGIN TRANSACTION;
        DECLARE @UserId bigint = (SELECT UserId FROM identity.Users WHERE PublicId = @UserPublicId);
        DECLARE @RoleId int = (SELECT RoleId FROM identity.Roles WHERE Code = @RoleCode AND IsActive = 1);
        IF @UserId IS NULL OR @RoleId IS NULL THROW 52002, 'User or role was not found.', 1;
        IF NOT EXISTS (SELECT 1 FROM identity.UserRoles WHERE UserId = @UserId AND RoleId = @RoleId)
            INSERT identity.UserRoles (UserId, RoleId) VALUES (@UserId, @RoleId);
        EXEC audit.AuditLog_Create @CorrelationId, N'USER.ROLE_ASSIGN', @ActorUserId,
            N'USER', @UserPublicId, @Source = 'BACKEND';
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        DECLARE @Message nvarchar(4000) = ERROR_MESSAGE();
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        EXEC audit.SystemError_Log @ErrorReference, @CorrelationId, 'DATABASE', @Message,
            @Component = N'Identity', @Operation = N'USER.ROLE_ASSIGN';
        THROW;
    END CATCH;
END;
GO

CREATE OR ALTER PROCEDURE identity.UserRole_Remove
    @UserPublicId uniqueidentifier, @RoleCode varchar(100), @ActorUserId uniqueidentifier,
    @CorrelationId uniqueidentifier, @ErrorReference varchar(40)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRY
        BEGIN TRANSACTION;
        DELETE ur FROM identity.UserRoles ur
        INNER JOIN identity.Users u ON u.UserId = ur.UserId
        INNER JOIN identity.Roles r ON r.RoleId = ur.RoleId
        WHERE u.PublicId = @UserPublicId AND r.Code = @RoleCode;
        EXEC audit.AuditLog_Create @CorrelationId, N'USER.ROLE_REMOVE', @ActorUserId,
            N'USER', @UserPublicId, @Source = 'BACKEND';
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        DECLARE @Message nvarchar(4000) = ERROR_MESSAGE();
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        EXEC audit.SystemError_Log @ErrorReference, @CorrelationId, 'DATABASE', @Message,
            @Component = N'Identity', @Operation = N'USER.ROLE_REMOVE';
        THROW;
    END CATCH;
END;
GO

CREATE OR ALTER PROCEDURE identity.RefreshToken_Create
    @UserId bigint, @TokenHash char(64), @ExpiresAtUtc datetime2(3),
    @CorrelationId uniqueidentifier, @ErrorReference varchar(40)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRY
        INSERT identity.RefreshTokens (UserId, TokenHash, ExpiresAtUtc)
        OUTPUT inserted.RefreshTokenId
        VALUES (@UserId, @TokenHash, @ExpiresAtUtc);
    END TRY
    BEGIN CATCH
        DECLARE @Message nvarchar(4000) = ERROR_MESSAGE();
        EXEC audit.SystemError_Log @ErrorReference, @CorrelationId, 'DATABASE', @Message,
            @Component = N'Identity', @Operation = N'REFRESH_TOKEN.CREATE';
        THROW;
    END CATCH;
END;
GO

CREATE OR ALTER PROCEDURE identity.RefreshToken_GetValid
    @TokenHash char(64), @CorrelationId uniqueidentifier, @ErrorReference varchar(40)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRY
        SELECT rt.RefreshTokenId, rt.UserId, rt.ExpiresAtUtc, u.PublicId, u.Username, u.DisplayName, u.IsActive
        FROM identity.RefreshTokens rt
        INNER JOIN identity.Users u ON u.UserId = rt.UserId
        WHERE rt.TokenHash = @TokenHash AND rt.RevokedAtUtc IS NULL AND rt.ExpiresAtUtc > SYSUTCDATETIME();
    END TRY
    BEGIN CATCH
        DECLARE @Message nvarchar(4000) = ERROR_MESSAGE();
        EXEC audit.SystemError_Log @ErrorReference, @CorrelationId, 'DATABASE', @Message,
            @Component = N'Identity', @Operation = N'REFRESH_TOKEN.GET_VALID';
        THROW;
    END CATCH;
END;
GO

CREATE OR ALTER PROCEDURE identity.RefreshToken_Revoke
    @TokenHash char(64), @ActorUserId uniqueidentifier = NULL,
    @CorrelationId uniqueidentifier, @ErrorReference varchar(40)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRY
        BEGIN TRANSACTION;
        UPDATE identity.RefreshTokens SET RevokedAtUtc = COALESCE(RevokedAtUtc, SYSUTCDATETIME())
        WHERE TokenHash = @TokenHash;
        EXEC audit.AuditLog_Create @CorrelationId, N'AUTH.LOGOUT', @ActorUserId,
            N'REFRESH_TOKEN', @TokenHash, @Source = 'BACKEND';
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        DECLARE @Message nvarchar(4000) = ERROR_MESSAGE();
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        EXEC audit.SystemError_Log @ErrorReference, @CorrelationId, 'DATABASE', @Message,
            @Component = N'Identity', @Operation = N'AUTH.LOGOUT';
        THROW;
    END CATCH;
END;
GO

CREATE OR ALTER PROCEDURE identity.RefreshToken_Rotate
    @CurrentTokenHash char(64), @NewTokenHash char(64), @ExpiresAtUtc datetime2(3),
    @CorrelationId uniqueidentifier, @ErrorReference varchar(40)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRY
        BEGIN TRANSACTION;
        DECLARE @CurrentId bigint, @UserId bigint, @NewId bigint;
        SELECT @CurrentId = RefreshTokenId, @UserId = UserId
        FROM identity.RefreshTokens WITH (UPDLOCK, HOLDLOCK)
        WHERE TokenHash = @CurrentTokenHash AND RevokedAtUtc IS NULL AND ExpiresAtUtc > SYSUTCDATETIME();
        IF @CurrentId IS NULL THROW 52003, 'Refresh token is invalid.', 1;
        INSERT identity.RefreshTokens (UserId, TokenHash, ExpiresAtUtc)
        VALUES (@UserId, @NewTokenHash, @ExpiresAtUtc);
        SET @NewId = SCOPE_IDENTITY();
        UPDATE identity.RefreshTokens
        SET RevokedAtUtc = SYSUTCDATETIME(), ReplacedByTokenId = @NewId
        WHERE RefreshTokenId = @CurrentId;
        EXEC audit.AuditLog_Create @CorrelationId, N'AUTH.REFRESH', @EntityType = N'USER',
            @EntityId = @UserId, @Source = 'BACKEND';
        COMMIT TRANSACTION;
        SELECT @UserId AS UserId;
    END TRY
    BEGIN CATCH
        DECLARE @Message nvarchar(4000) = ERROR_MESSAGE();
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        EXEC audit.SystemError_Log @ErrorReference, @CorrelationId, 'DATABASE', @Message,
            @Component = N'Identity', @Operation = N'AUTH.REFRESH';
        THROW;
    END CATCH;
END;
GO
