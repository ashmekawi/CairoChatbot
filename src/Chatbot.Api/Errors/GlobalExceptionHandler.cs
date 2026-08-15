using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Chatbot.Api.Logging;
using Chatbot.Api.Middleware;
using Chatbot.Core.Errors;

namespace Chatbot.Api.Errors;

public sealed class GlobalExceptionHandler(IApplicationErrorLogger errorLogger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        var correlationId = context.Items.TryGetValue(CorrelationIdMiddleware.HeaderName, out var value)
            && value is Guid id
                ? id
                : Guid.NewGuid();

        if (exception is AppException app)
        {
            var appErrorReference = app.ErrorReference ?? ErrorReferences.Create();
            context.Response.StatusCode = app.StatusCode;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Status = app.StatusCode,
                Title = "تعذر تنفيذ الطلب.",
                Type = app.ErrorType,
                Extensions =
                {
                    ["correlationId"] = correlationId,
                    ["errorReference"] = appErrorReference
                }
            }, cancellationToken);
            return true;
        }

        var errorReference = ErrorReferences.Create();
        try
        {
            await errorLogger.LogAsync(
                exception,
                context,
                "BACKEND",
                errorReference,
                cancellationToken: cancellationToken);
        }
        catch
        {
            // A logging failure must not replace or expose the original exception.
        }

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = 500,
            Title = "تعذر تنفيذ العملية.",
            Type = "unexpected_error",
            Extensions =
            {
                ["correlationId"] = correlationId,
                ["errorReference"] = errorReference
            }
        }, cancellationToken);
        return true;
    }
}
