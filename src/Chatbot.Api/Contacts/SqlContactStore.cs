using System.Data;
using Chatbot.Api.Logging;
using Chatbot.Core.Errors;
using Microsoft.Data.SqlClient;

namespace Chatbot.Api.Contacts;

public sealed class SqlContactStore(IConfiguration configuration) : IContactStore, IChannelIdentityStore
{
    private string ConnectionString => configuration.GetConnectionString("ChatbotDatabase")
        ?? throw new InvalidOperationException("ChatbotDatabase connection string is missing.");

    Task IContactStore.CreateAsync(ContactRecord contact, Guid? actorId, Guid correlationId, CancellationToken cancellationToken)
    {
        return ExecuteAsync("[contact].Contact_Create", correlationId, cancellationToken,
            GuidParameter("@PublicId", contact.PublicId),
            NullableString("@DisplayName", contact.DisplayName, SqlDbType.NVarChar, 200),
            NullableString("@PreferredLanguage", contact.PreferredLanguage, SqlDbType.VarChar, 10),
            new SqlParameter("@IsActive", SqlDbType.Bit) { Value = contact.IsActive },
            NullableGuid("@ActorUserId", actorId));
    }

    async Task<ContactRecord?> IContactStore.GetAsync(Guid publicId, Guid correlationId, CancellationToken cancellationToken)
    {
        await using var reader = await ReadAsync("[contact].Contact_GetByPublicId", correlationId, cancellationToken,
            GuidParameter("@PublicId", publicId));
        return await reader.ReadAsync(cancellationToken) ? ReadContact(reader) : null;
    }

    Task IContactStore.UpdateAsync(ContactRecord contact, Guid actorId, Guid correlationId, CancellationToken cancellationToken)
    {
        return ExecuteAsync("[contact].Contact_Update", correlationId, cancellationToken,
            GuidParameter("@PublicId", contact.PublicId),
            NullableString("@DisplayName", contact.DisplayName, SqlDbType.NVarChar, 200),
            NullableString("@PreferredLanguage", contact.PreferredLanguage, SqlDbType.VarChar, 10),
            GuidParameter("@ActorUserId", actorId));
    }

    Task IContactStore.SetActiveAsync(Guid publicId, bool isActive, Guid actorId, Guid correlationId, CancellationToken cancellationToken)
    {
        return ChangeStateAsync("[contact].Contact_SetActive", "@IsActive", publicId, isActive, actorId, correlationId, cancellationToken);
    }

    Task IChannelIdentityStore.CreateAsync(ChannelIdentityRecord identity, Guid? actorId, Guid correlationId, CancellationToken cancellationToken)
    {
        return ExecuteAsync("[contact].ChannelIdentity_Create", correlationId, cancellationToken,
            GuidParameter("@PublicId", identity.PublicId),
            GuidParameter("@ContactPublicId", identity.ContactPublicId),
            GuidParameter("@ChannelPublicId", identity.ChannelPublicId),
            new SqlParameter("@ExternalId", SqlDbType.NVarChar, 200) { Value = identity.ExternalId },
            new SqlParameter("@NormalizedAddress", SqlDbType.VarChar, 100) { Value = identity.NormalizedAddress },
            NullableString("@DisplayAddress", identity.DisplayAddress, SqlDbType.NVarChar, 200),
            new SqlParameter("@IsVerified", SqlDbType.Bit) { Value = identity.IsVerified },
            new SqlParameter("@IsActive", SqlDbType.Bit) { Value = identity.IsActive },
            NullableGuid("@ActorUserId", actorId));
    }

    async Task<ChannelIdentityRecord?> IChannelIdentityStore.GetAsync(
        Guid publicId,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        await using var reader = await ReadAsync("[contact].ChannelIdentity_GetByPublicId", correlationId, cancellationToken,
            GuidParameter("@PublicId", publicId));
        return await reader.ReadAsync(cancellationToken) ? ReadIdentity(reader) : null;
    }

    async Task<ChannelIdentityRecord?> IChannelIdentityStore.GetByExternalIdAsync(
        Guid channelPublicId,
        string externalId,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        await using var reader = await ReadAsync("[contact].ChannelIdentity_GetByExternalId", correlationId, cancellationToken,
            GuidParameter("@ChannelPublicId", channelPublicId),
            new SqlParameter("@ExternalId", SqlDbType.NVarChar, 200) { Value = externalId });
        return await reader.ReadAsync(cancellationToken) ? ReadIdentity(reader) : null;
    }

    Task IChannelIdentityStore.UpdateAsync(
        ChannelIdentityRecord identity,
        Guid actorId,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync("[contact].ChannelIdentity_Update", correlationId, cancellationToken,
            GuidParameter("@PublicId", identity.PublicId),
            new SqlParameter("@ExternalId", SqlDbType.NVarChar, 200) { Value = identity.ExternalId },
            new SqlParameter("@NormalizedAddress", SqlDbType.VarChar, 100) { Value = identity.NormalizedAddress },
            NullableString("@DisplayAddress", identity.DisplayAddress, SqlDbType.NVarChar, 200),
            GuidParameter("@ActorUserId", actorId));
    }

    Task IChannelIdentityStore.SetActiveAsync(Guid publicId, bool isActive, Guid actorId, Guid correlationId, CancellationToken cancellationToken)
    {
        return ChangeStateAsync("[contact].ChannelIdentity_SetActive", "@IsActive", publicId, isActive, actorId, correlationId, cancellationToken);
    }

    Task IChannelIdentityStore.SetVerifiedAsync(Guid publicId, bool isVerified, Guid actorId, Guid correlationId, CancellationToken cancellationToken)
    {
        return ChangeStateAsync("[contact].ChannelIdentity_SetVerified", "@IsVerified", publicId, isVerified, actorId, correlationId, cancellationToken);
    }

    private Task ChangeStateAsync(
        string procedure,
        string stateParameter,
        Guid publicId,
        bool state,
        Guid actorId,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(procedure, correlationId, cancellationToken,
            GuidParameter("@PublicId", publicId),
            new SqlParameter(stateParameter, SqlDbType.Bit) { Value = state },
            GuidParameter("@ActorUserId", actorId));
    }

    private async Task ExecuteAsync(
        string procedure,
        Guid correlationId,
        CancellationToken cancellationToken,
        params SqlParameter[] parameters)
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
            throw DatabaseError(command, exception);
        }
    }

    private async Task<SqlDataReader> ReadAsync(
        string procedure,
        Guid correlationId,
        CancellationToken cancellationToken,
        params SqlParameter[] parameters)
    {
        var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        var command = CreateCommand(connection, procedure, correlationId, parameters);
        try
        {
            return await command.ExecuteReaderAsync(CommandBehavior.CloseConnection, cancellationToken);
        }
        catch (SqlException exception)
        {
            var error = DatabaseError(command, exception);
            await command.DisposeAsync();
            await connection.DisposeAsync();
            throw error;
        }
    }

    private static SqlCommand CreateCommand(
        SqlConnection connection,
        string procedure,
        Guid correlationId,
        params SqlParameter[] parameters)
    {
        var command = new SqlCommand(procedure, connection) { CommandType = CommandType.StoredProcedure };
        command.Parameters.AddRange(parameters);
        command.Parameters.Add(GuidParameter("@CorrelationId", correlationId));
        command.Parameters.Add(new SqlParameter("@ErrorReference", SqlDbType.VarChar, 40)
        {
            Value = ErrorReferences.Create()
        });
        return command;
    }

    private static ContactRecord ReadContact(SqlDataReader reader)
    {
        return new ContactRecord(
            reader.GetInt64(reader.GetOrdinal("ContactId")),
            reader.GetGuid(reader.GetOrdinal("PublicId")),
            NullableReaderString(reader, "DisplayName"),
            NullableReaderString(reader, "PreferredLanguage"),
            reader.GetBoolean(reader.GetOrdinal("IsActive")));
    }

    private static ChannelIdentityRecord ReadIdentity(SqlDataReader reader)
    {
        return new ChannelIdentityRecord(
            reader.GetInt64(reader.GetOrdinal("ChannelIdentityId")),
            reader.GetGuid(reader.GetOrdinal("PublicId")),
            reader.GetGuid(reader.GetOrdinal("ContactPublicId")),
            reader.GetGuid(reader.GetOrdinal("ChannelPublicId")),
            reader.GetString(reader.GetOrdinal("ExternalId")),
            reader.GetString(reader.GetOrdinal("NormalizedAddress")),
            NullableReaderString(reader, "DisplayAddress"),
            reader.GetBoolean(reader.GetOrdinal("IsVerified")),
            NullableReaderDateTime(reader, "VerifiedAtUtc"),
            reader.GetBoolean(reader.GetOrdinal("IsActive")),
            NullableReaderDateTime(reader, "LastSeenAtUtc"));
    }

    private static AppException DatabaseError(SqlCommand command, SqlException exception)
    {
        return new AppException("Database operation failed.", 500, "database_error", exception)
        {
            ErrorReference = (string)command.Parameters["@ErrorReference"].Value
        };
    }

    private static SqlParameter GuidParameter(string name, Guid value)
    {
        return new SqlParameter(name, SqlDbType.UniqueIdentifier) { Value = value };
    }

    private static SqlParameter NullableGuid(string name, Guid? value)
    {
        return new SqlParameter(name, SqlDbType.UniqueIdentifier) { Value = value ?? (object)DBNull.Value };
    }

    private static SqlParameter NullableString(string name, string? value, SqlDbType type, int size)
    {
        return new SqlParameter(name, type, size) { Value = value ?? (object)DBNull.Value };
    }

    private static string? NullableReaderString(SqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static DateTime? NullableReaderDateTime(SqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetDateTime(ordinal);
    }
}
