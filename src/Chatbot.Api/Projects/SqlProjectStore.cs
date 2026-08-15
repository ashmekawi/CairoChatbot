using System.Data;
using Chatbot.Api.Logging;
using Chatbot.Core.Errors;
using Microsoft.Data.SqlClient;

namespace Chatbot.Api.Projects;

public sealed class SqlProjectStore(IConfiguration configuration) : IProjectStore, IChannelStore, IBusinessHoursStore
{
    private string ConnectionString => configuration.GetConnectionString("ChatbotDatabase")
        ?? throw new InvalidOperationException("ChatbotDatabase connection string is missing.");

    Task IProjectStore.CreateAsync(ProjectRecord project, Guid? actorId, Guid correlationId, CancellationToken cancellationToken)
    {
        return ExecuteAsync("[project].Project_Create", correlationId, cancellationToken,
            ProjectParameters(project, includePublicId: true)
                .Append(NullableGuid("@ActorUserId", actorId))
                .ToArray());
    }

    async Task<ProjectRecord?> IProjectStore.GetAsync(Guid publicId, Guid correlationId, CancellationToken cancellationToken)
    {
        await using var reader = await ReadAsync(
            "[project].Project_GetByPublicId",
            correlationId,
            cancellationToken,
            new SqlParameter("@PublicId", SqlDbType.UniqueIdentifier) { Value = publicId });
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }
        return ReadProject(reader);
    }

    Task IProjectStore.UpdateAsync(ProjectRecord project, Guid actorId, Guid correlationId, CancellationToken cancellationToken)
    {
        return ExecuteAsync("[project].Project_Update", correlationId, cancellationToken,
            ProjectParameters(project, includePublicId: true)
                .Where(parameter => parameter.ParameterName != "@IsActive")
                .Append(GuidParameter("@ActorUserId", actorId))
                .ToArray());
    }

    Task IProjectStore.SetActiveAsync(Guid publicId, bool isActive, Guid actorId, Guid correlationId, CancellationToken cancellationToken)
    {
        return SetActiveAsync("[project].Project_SetActive", publicId, isActive, actorId, correlationId, cancellationToken);
    }

    Task IChannelStore.CreateAsync(ChannelRecord channel, Guid? actorId, Guid correlationId, CancellationToken cancellationToken)
    {
        return ExecuteAsync("[project].Channel_Create", correlationId, cancellationToken,
            ChannelParameters(channel, includeProject: true, includeActive: true)
                .Append(NullableGuid("@ActorUserId", actorId))
                .ToArray());
    }

    async Task<ChannelRecord?> IChannelStore.GetAsync(Guid publicId, Guid correlationId, CancellationToken cancellationToken)
    {
        await using var reader = await ReadAsync(
            "[project].Channel_GetByPublicId",
            correlationId,
            cancellationToken,
            new SqlParameter("@PublicId", SqlDbType.UniqueIdentifier) { Value = publicId });
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }
        return new ChannelRecord(
            reader.GetInt64(reader.GetOrdinal("ChannelId")),
            reader.GetGuid(reader.GetOrdinal("PublicId")),
            reader.GetGuid(reader.GetOrdinal("ProjectPublicId")),
            reader.GetString(reader.GetOrdinal("Code")),
            reader.GetString(reader.GetOrdinal("ChannelType")),
            reader.GetString(reader.GetOrdinal("ProviderCode")),
            reader.GetBoolean(reader.GetOrdinal("IsActive")),
            reader.IsDBNull(reader.GetOrdinal("ConfigurationJson"))
                ? null
                : reader.GetString(reader.GetOrdinal("ConfigurationJson")));
    }

    Task IChannelStore.UpdateAsync(ChannelRecord channel, Guid actorId, Guid correlationId, CancellationToken cancellationToken)
    {
        return ExecuteAsync("[project].Channel_Update", correlationId, cancellationToken,
            ChannelParameters(channel, includeProject: false, includeActive: false)
                .Append(GuidParameter("@ActorUserId", actorId))
                .ToArray());
    }

    Task IChannelStore.SetActiveAsync(Guid publicId, bool isActive, Guid actorId, Guid correlationId, CancellationToken cancellationToken)
    {
        return SetActiveAsync("[project].Channel_SetActive", publicId, isActive, actorId, correlationId, cancellationToken);
    }

    async Task<IReadOnlyList<BusinessHourRecord>> IBusinessHoursStore.GetAsync(
        Guid projectPublicId,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        var results = new List<BusinessHourRecord>();
        await using var reader = await ReadAsync(
            "[project].BusinessHours_GetByProject",
            correlationId,
            cancellationToken,
            GuidParameter("@ProjectPublicId", projectPublicId));
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new BusinessHourRecord(
                reader.GetByte(reader.GetOrdinal("DayOfWeek")),
                reader.GetBoolean(reader.GetOrdinal("IsWorkingDay")),
                reader.IsDBNull(reader.GetOrdinal("StartTime")) ? null : reader.GetTimeSpan(reader.GetOrdinal("StartTime")),
                reader.IsDBNull(reader.GetOrdinal("EndTime")) ? null : reader.GetTimeSpan(reader.GetOrdinal("EndTime"))));
        }
        return results;
    }

    Task IBusinessHoursStore.UpsertAsync(
        Guid projectPublicId,
        BusinessHourRecord hours,
        Guid actorId,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync("[project].BusinessHours_Upsert", correlationId, cancellationToken,
            GuidParameter("@ProjectPublicId", projectPublicId),
            new SqlParameter("@DayOfWeek", SqlDbType.TinyInt) { Value = hours.DayOfWeek },
            new SqlParameter("@IsWorkingDay", SqlDbType.Bit) { Value = hours.IsWorkingDay },
            NullableTime("@StartTime", hours.StartTime),
            NullableTime("@EndTime", hours.EndTime),
            GuidParameter("@ActorUserId", actorId));
    }

    private async Task SetActiveAsync(
        string procedure,
        Guid publicId,
        bool isActive,
        Guid actorId,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        await ExecuteAsync(procedure, correlationId, cancellationToken,
            GuidParameter("@PublicId", publicId),
            new SqlParameter("@IsActive", SqlDbType.Bit) { Value = isActive },
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
        var command = new SqlCommand(procedure, connection)
        {
            CommandType = CommandType.StoredProcedure
        };
        command.Parameters.AddRange(parameters);
        command.Parameters.Add(GuidParameter("@CorrelationId", correlationId));
        command.Parameters.Add(new SqlParameter("@ErrorReference", SqlDbType.VarChar, 40)
        {
            Value = ErrorReferences.Create()
        });
        return command;
    }

    private static IEnumerable<SqlParameter> ProjectParameters(ProjectRecord project, bool includePublicId)
    {
        if (includePublicId)
        {
            yield return GuidParameter("@PublicId", project.PublicId);
        }
        yield return new SqlParameter("@Code", SqlDbType.VarChar, 100) { Value = project.Code };
        yield return new SqlParameter("@NameAr", SqlDbType.NVarChar, 200) { Value = project.NameAr };
        yield return new SqlParameter("@NameEn", SqlDbType.NVarChar, 200) { Value = project.NameEn ?? (object)DBNull.Value };
        yield return new SqlParameter("@DefaultLanguage", SqlDbType.VarChar, 10) { Value = project.DefaultLanguage };
        yield return new SqlParameter("@TimeZoneId", SqlDbType.VarChar, 100) { Value = project.TimeZoneId };
        yield return new SqlParameter("@IsActive", SqlDbType.Bit) { Value = project.IsActive };
    }

    private static IEnumerable<SqlParameter> ChannelParameters(ChannelRecord channel, bool includeProject, bool includeActive)
    {
        yield return GuidParameter("@PublicId", channel.PublicId);
        if (includeProject)
        {
            yield return GuidParameter("@ProjectPublicId", channel.ProjectPublicId);
        }
        yield return new SqlParameter("@Code", SqlDbType.VarChar, 100) { Value = channel.Code };
        yield return new SqlParameter("@ChannelType", SqlDbType.VarChar, 50) { Value = channel.ChannelType };
        yield return new SqlParameter("@ProviderCode", SqlDbType.VarChar, 50) { Value = channel.ProviderCode };
        if (includeActive)
        {
            yield return new SqlParameter("@IsActive", SqlDbType.Bit) { Value = channel.IsActive };
        }
        yield return new SqlParameter("@ConfigurationJson", SqlDbType.NVarChar, -1)
        {
            Value = channel.ConfigurationJson ?? (object)DBNull.Value
        };
    }

    private static ProjectRecord ReadProject(SqlDataReader reader)
    {
        return new ProjectRecord(
            reader.GetInt64(reader.GetOrdinal("ProjectId")),
            reader.GetGuid(reader.GetOrdinal("PublicId")),
            reader.GetString(reader.GetOrdinal("Code")),
            reader.GetString(reader.GetOrdinal("NameAr")),
            reader.IsDBNull(reader.GetOrdinal("NameEn")) ? null : reader.GetString(reader.GetOrdinal("NameEn")),
            reader.GetString(reader.GetOrdinal("DefaultLanguage")),
            reader.GetString(reader.GetOrdinal("TimeZoneId")),
            reader.GetBoolean(reader.GetOrdinal("IsActive")));
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

    private static SqlParameter NullableTime(string name, TimeSpan? value)
    {
        return new SqlParameter(name, SqlDbType.Time) { Value = value ?? (object)DBNull.Value };
    }
}
