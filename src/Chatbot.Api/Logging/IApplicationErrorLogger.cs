namespace Chatbot.Api.Logging;

public interface IApplicationErrorLogger
{
    Task<string> LogAsync(
        Exception exception,
        HttpContext context,
        string source = "BACKEND",
        string? errorReference = null,
        string? contextJson = null,
        CancellationToken cancellationToken = default);
}
