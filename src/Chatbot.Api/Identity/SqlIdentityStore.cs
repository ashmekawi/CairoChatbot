using System.Data;
using Chatbot.Api.Logging;
using Chatbot.Core.Errors;
using Microsoft.Data.SqlClient;

namespace Chatbot.Api.Identity;

public sealed class SqlIdentityStore(IConfiguration configuration) : IIdentityStore
{
    private string ConnectionString => configuration.GetConnectionString("ChatbotDatabase")
        ?? throw new InvalidOperationException("ChatbotDatabase connection string is missing.");

    public Task<IdentityUser?> GetByUsernameAsync(string username, Guid correlationId, CancellationToken cancellationToken)
    {
        return ReadUserAsync("identity.User_GetByUsername", "@Username", username, true, correlationId, cancellationToken);
    }

    public Task<IdentityUser?> GetByPublicIdAsync(Guid publicId, Guid correlationId, CancellationToken cancellationToken)
    {
        return ReadUserAsync("identity.User_GetByPublicId", "@PublicId", publicId, false, correlationId, cancellationToken);
    }

    public Task CreateUserAsync(IdentityUser user, Guid? actorUserId, Guid correlationId, CancellationToken cancellationToken)
    {
        return ExecuteAsync("identity.User_Create", correlationId, cancellationToken,
            new SqlParameter("@PublicId", SqlDbType.UniqueIdentifier) { Value = user.PublicId },
            new SqlParameter("@Username", SqlDbType.NVarChar, 100) { Value = user.Username },
            new SqlParameter("@DisplayName", SqlDbType.NVarChar, 200) { Value = user.DisplayName },
            new SqlParameter("@PasswordHash", SqlDbType.NVarChar, 1000) { Value = user.PasswordHash! },
            new SqlParameter("@IsActive", SqlDbType.Bit) { Value = user.IsActive },
            NullableGuid("@ActorUserId", actorUserId));
    }

    public Task SetActiveAsync(Guid publicId, bool isActive, Guid actorUserId, Guid correlationId, CancellationToken cancellationToken)
    {
        return ExecuteAsync("identity.User_SetActive", correlationId, cancellationToken,
            new SqlParameter("@PublicId", SqlDbType.UniqueIdentifier) { Value = publicId },
            new SqlParameter("@IsActive", SqlDbType.Bit) { Value = isActive },
            new SqlParameter("@ActorUserId", SqlDbType.UniqueIdentifier) { Value = actorUserId });
    }

    public Task SetPasswordAsync(Guid publicId, string passwordHash, Guid actorUserId, Guid correlationId, CancellationToken cancellationToken)
    {
        return ExecuteAsync("identity.User_SetPassword", correlationId, cancellationToken,
            new SqlParameter("@PublicId", SqlDbType.UniqueIdentifier) { Value = publicId },
            new SqlParameter("@PasswordHash", SqlDbType.NVarChar, 1000) { Value = passwordHash },
            new SqlParameter("@ActorUserId", SqlDbType.UniqueIdentifier) { Value = actorUserId });
    }

    public Task RecordLoginSuccessAsync(long userId, Guid correlationId, CancellationToken cancellationToken)
    {
        return ExecuteAsync("identity.User_RecordLoginSuccess", correlationId, cancellationToken,
            new SqlParameter("@UserId", SqlDbType.BigInt) { Value = userId });
    }

    public Task RecordLoginFailureAsync(long userId, int maximumAttempts, int lockoutMinutes, Guid correlationId, CancellationToken cancellationToken)
    {
        return ExecuteAsync("identity.User_RecordLoginFailure", correlationId, cancellationToken,
            new SqlParameter("@UserId", SqlDbType.BigInt) { Value = userId },
            new SqlParameter("@MaximumFailedAttempts", SqlDbType.Int) { Value = maximumAttempts },
            new SqlParameter("@LockoutMinutes", SqlDbType.Int) { Value = lockoutMinutes });
    }

    public async Task AuditLoginFailureAsync(string username, Guid correlationId, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("audit.AuditLog_Create", connection)
        {
            CommandType = CommandType.StoredProcedure
        };
        command.Parameters.Add(new SqlParameter("@CorrelationId", SqlDbType.UniqueIdentifier) { Value = correlationId });
        command.Parameters.Add(new SqlParameter("@Action", SqlDbType.NVarChar, 200) { Value = "AUTH.LOGIN_FAILED" });
        command.Parameters.Add(new SqlParameter("@EntityType", SqlDbType.NVarChar, 200) { Value = "USERNAME" });
        command.Parameters.Add(new SqlParameter("@EntityId", SqlDbType.NVarChar, 200) { Value = username });
        command.Parameters.Add(new SqlParameter("@Source", SqlDbType.VarChar, 30) { Value = "BACKEND" });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> GetPermissionsAsync(long userId, Guid correlationId, CancellationToken cancellationToken)
    {
        var permissions = new List<string>();
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = CreateCommand(connection, "identity.Permission_GetByUser", correlationId,
            new SqlParameter("@UserId", SqlDbType.BigInt) { Value = userId });
        await using var reader = await ExecuteReaderAsync(command, cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            permissions.Add(reader.GetString(reader.GetOrdinal("Code")));
        }
        return permissions;
    }

    public Task AssignRoleAsync(Guid publicId, string roleCode, Guid actorUserId, Guid correlationId, CancellationToken cancellationToken)
    {
        return ChangeRoleAsync("identity.UserRole_Assign", publicId, roleCode, actorUserId, correlationId, cancellationToken);
    }

    public Task RemoveRoleAsync(Guid publicId, string roleCode, Guid actorUserId, Guid correlationId, CancellationToken cancellationToken)
    {
        return ChangeRoleAsync("identity.UserRole_Remove", publicId, roleCode, actorUserId, correlationId, cancellationToken);
    }

    public Task CreateRefreshTokenAsync(long userId, string tokenHash, DateTime expiresAtUtc, Guid correlationId, CancellationToken cancellationToken)
    {
        return ExecuteAsync("identity.RefreshToken_Create", correlationId, cancellationToken,
            new SqlParameter("@UserId", SqlDbType.BigInt) { Value = userId },
            new SqlParameter("@TokenHash", SqlDbType.Char, 64) { Value = tokenHash },
            new SqlParameter("@ExpiresAtUtc", SqlDbType.DateTime2) { Value = expiresAtUtc });
    }

    public async Task<ValidRefreshToken?> GetValidRefreshTokenAsync(string tokenHash, Guid correlationId, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = CreateCommand(connection, "identity.RefreshToken_GetValid", correlationId,
            new SqlParameter("@TokenHash", SqlDbType.Char, 64) { Value = tokenHash });
        await using var reader = await ExecuteReaderAsync(command, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }
        return new ValidRefreshToken(
            reader.GetInt64(reader.GetOrdinal("RefreshTokenId")),
            reader.GetInt64(reader.GetOrdinal("UserId")),
            reader.GetGuid(reader.GetOrdinal("PublicId")),
            reader.GetString(reader.GetOrdinal("Username")),
            reader.GetString(reader.GetOrdinal("DisplayName")),
            reader.GetBoolean(reader.GetOrdinal("IsActive")));
    }

    public Task RotateRefreshTokenAsync(string currentHash, string newHash, DateTime expiresAtUtc, Guid correlationId, CancellationToken cancellationToken)
    {
        return ExecuteAsync("identity.RefreshToken_Rotate", correlationId, cancellationToken,
            new SqlParameter("@CurrentTokenHash", SqlDbType.Char, 64) { Value = currentHash },
            new SqlParameter("@NewTokenHash", SqlDbType.Char, 64) { Value = newHash },
            new SqlParameter("@ExpiresAtUtc", SqlDbType.DateTime2) { Value = expiresAtUtc });
    }

    public Task RevokeRefreshTokenAsync(string tokenHash, Guid? actorUserId, Guid correlationId, CancellationToken cancellationToken)
    {
        return ExecuteAsync("identity.RefreshToken_Revoke", correlationId, cancellationToken,
            new SqlParameter("@TokenHash", SqlDbType.Char, 64) { Value = tokenHash },
            NullableGuid("@ActorUserId", actorUserId));
    }

    private async Task<IdentityUser?> ReadUserAsync(
        string procedure,
        string parameterName,
        object parameterValue,
        bool includePassword,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        var parameter = parameterValue is Guid
            ? new SqlParameter(parameterName, SqlDbType.UniqueIdentifier) { Value = parameterValue }
            : new SqlParameter(parameterName, SqlDbType.NVarChar, 100) { Value = parameterValue };
        await using var command = CreateCommand(connection, procedure, correlationId, parameter);
        await using var reader = await ExecuteReaderAsync(command, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }
        return new IdentityUser(
            reader.GetInt64(reader.GetOrdinal("UserId")),
            reader.GetGuid(reader.GetOrdinal("PublicId")),
            reader.GetString(reader.GetOrdinal("Username")),
            reader.GetString(reader.GetOrdinal("DisplayName")),
            includePassword ? reader.GetString(reader.GetOrdinal("PasswordHash")) : null,
            reader.GetBoolean(reader.GetOrdinal("IsActive")),
            reader.GetInt32(reader.GetOrdinal("FailedLoginCount")),
            reader.IsDBNull(reader.GetOrdinal("LockedUntilUtc")) ? null : reader.GetDateTime(reader.GetOrdinal("LockedUntilUtc")));
    }

    private Task ChangeRoleAsync(string procedure, Guid publicId, string roleCode, Guid actorUserId, Guid correlationId, CancellationToken cancellationToken)
    {
        return ExecuteAsync(procedure, correlationId, cancellationToken,
            new SqlParameter("@UserPublicId", SqlDbType.UniqueIdentifier) { Value = publicId },
            new SqlParameter("@RoleCode", SqlDbType.VarChar, 100) { Value = roleCode },
            new SqlParameter("@ActorUserId", SqlDbType.UniqueIdentifier) { Value = actorUserId });
    }

    private async Task ExecuteAsync(string procedure, Guid correlationId, CancellationToken cancellationToken, params SqlParameter[] parameters)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = CreateCommand(connection, procedure, correlationId, parameters);
        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqlException exception)
        {
            throw new AppException("Database operation failed.", 500, "database_error", exception)
            {
                ErrorReference = (string)command.Parameters["@ErrorReference"].Value
            };
        }
    }

    private static async Task<SqlDataReader> ExecuteReaderAsync(SqlCommand command, CancellationToken cancellationToken)
    {
        try
        {
            return await command.ExecuteReaderAsync(cancellationToken);
        }
        catch (SqlException exception)
        {
            throw new AppException("Database operation failed.", 500, "database_error", exception)
            {
                ErrorReference = (string)command.Parameters["@ErrorReference"].Value
            };
        }
    }

    private static SqlCommand CreateCommand(SqlConnection connection, string procedure, Guid correlationId, params SqlParameter[] parameters)
    {
        var command = new SqlCommand(procedure, connection)
        {
            CommandType = CommandType.StoredProcedure
        };
        command.Parameters.AddRange(parameters);
        command.Parameters.Add(new SqlParameter("@CorrelationId", SqlDbType.UniqueIdentifier) { Value = correlationId });
        command.Parameters.Add(new SqlParameter("@ErrorReference", SqlDbType.VarChar, 40) { Value = ErrorReferences.Create() });
        return command;
    }

    private static SqlParameter NullableGuid(string name, Guid? value)
    {
        return new SqlParameter(name, SqlDbType.UniqueIdentifier)
        {
            Value = value ?? (object)DBNull.Value
        };
    }
}
