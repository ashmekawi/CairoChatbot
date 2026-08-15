using System.Diagnostics;

namespace Chatbot.Api.Middleware;

public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-ID";

    public async Task Invoke(HttpContext context)
    {
        var correlationId = Guid.TryParse(context.Request.Headers[HeaderName].FirstOrDefault(), out var parsed)
            ? parsed
            : Guid.NewGuid();

        context.Items[HeaderName] = correlationId;
        context.Response.Headers[HeaderName] = correlationId.ToString();
        Activity.Current?.SetTag("correlation.id", correlationId);
        await next(context);
    }
}
