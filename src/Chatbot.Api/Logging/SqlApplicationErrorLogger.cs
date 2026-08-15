using System.Data;
using Microsoft.Data.SqlClient;
using Chatbot.Api.Middleware;

namespace Chatbot.Api.Logging;

public sealed class SqlApplicationErrorLogger(IConfiguration configuration, IHostEnvironment environment) : IApplicationErrorLogger
{
    public async Task<string> LogAsync(
        Exception exception,
        HttpContext context,
        string source = "BACKEND",
        string? errorReference = null,
        string? contextJson = null,
        CancellationToken cancellationToken = default)
    {
        errorReference ??= ErrorReferences.Create();
        var correlationId = context.Items.TryGetValue(CorrelationIdMiddleware.HeaderName, out var value)
            && value is Guid id
                ? id
                : Guid.NewGuid();
        var connectionString = configuration.GetConnectionString("ChatbotDatabase")
            ?? throw new InvalidOperationException("ChatbotDatabase connection string is missing.");

        // Deliberately use a new connection with no ambient business transaction.
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("audit.SystemError_Log", connection) { CommandType = CommandType.StoredProcedure };
        command.Parameters.Add(new SqlParameter("@ErrorReference", SqlDbType.VarChar, 40) { Value = errorReference });
        command.Parameters.Add(new SqlParameter("@CorrelationId", SqlDbType.UniqueIdentifier) { Value = correlationId });
        command.Parameters.Add(new SqlParameter("@Source", SqlDbType.VarChar, 30) { Value = source });
        var safeMessage = exception.Message.Length <= 4000 ? exception.Message : exception.Message[..4000];
        command.Parameters.Add(new SqlParameter("@ErrorMessage", SqlDbType.NVarChar, 4000) { Value = safeMessage });
        command.Parameters.Add(new SqlParameter("@ExceptionType", SqlDbType.NVarChar, 500) { Value = exception.GetType().FullName ?? exception.GetType().Name });
        command.Parameters.Add(new SqlParameter("@StackTrace", SqlDbType.NVarChar, -1) { Value = exception.StackTrace ?? (object)DBNull.Value });
        command.Parameters.Add(new SqlParameter("@Component", SqlDbType.NVarChar, 200) { Value = "Chatbot.Api" });
        command.Parameters.Add(new SqlParameter("@Operation", SqlDbType.NVarChar, 200) { Value = context.GetEndpoint()?.DisplayName ?? (object)DBNull.Value });
        command.Parameters.Add(new SqlParameter("@RequestPath", SqlDbType.NVarChar, 1000) { Value = context.Request.Path.Value ?? (object)DBNull.Value });
        command.Parameters.Add(new SqlParameter("@HttpMethod", SqlDbType.VarChar, 20) { Value = context.Request.Method });
        command.Parameters.Add(new SqlParameter("@HostName", SqlDbType.NVarChar, 200) { Value = Environment.MachineName });
        command.Parameters.Add(new SqlParameter("@ApplicationName", SqlDbType.NVarChar, 200) { Value = environment.ApplicationName });
        command.Parameters.Add(new SqlParameter("@EnvironmentName", SqlDbType.NVarChar, 100) { Value = environment.EnvironmentName });
        command.Parameters.Add(new SqlParameter("@ContextJson", SqlDbType.NVarChar, -1) { Value = contextJson ?? (object)DBNull.Value });
        await command.ExecuteNonQueryAsync(cancellationToken);
        return errorReference;
    }
}
