using System.Text.Json;
using Chatbot.Api.Controllers;
using Chatbot.Api.Errors;
using Chatbot.Api.Logging;
using Chatbot.Api.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

await FoundationTests.RunAsync();

internal static class FoundationTests
{
    public static async Task RunAsync()
    {
        await CorrelationMiddlewareAddsCorrelationIdAsync();
        await UnexpectedExceptionReturnsSafeProblemDetailsAsync();
        await FrontendErrorEndpointLogsErrorAsync();
        Console.WriteLine("V0001 backend foundation tests passed.");
    }

    private static async Task CorrelationMiddlewareAddsCorrelationIdAsync()
    {
        var context = new DefaultHttpContext();
        var nextWasCalled = false;
        var middleware = new CorrelationIdMiddleware(_ =>
        {
            nextWasCalled = true;
            return Task.CompletedTask;
        });

        await middleware.Invoke(context);

        Assert(nextWasCalled, "Correlation middleware did not invoke the next delegate.");
        Assert(context.Items[CorrelationIdMiddleware.HeaderName] is Guid, "CorrelationId was not added to the request.");
        Assert(!string.IsNullOrWhiteSpace(context.Response.Headers[CorrelationIdMiddleware.HeaderName]), "CorrelationId response header is missing.");
    }

    private static async Task UnexpectedExceptionReturnsSafeProblemDetailsAsync()
    {
        var logger = new RecordingErrorLogger();
        var handler = new GlobalExceptionHandler(logger);
        var context = CreateHttpContext();
        var exception = new InvalidOperationException("sensitive SQL detail");

        var handled = await handler.TryHandleAsync(context, exception, CancellationToken.None);
        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        var body = document.RootElement;

        Assert(handled, "Unexpected exception was not handled.");
        Assert(context.Response.StatusCode == StatusCodes.Status500InternalServerError, "Unexpected exception status was not 500.");
        Assert(logger.LastSource == "BACKEND", "Backend exception was not logged.");
        Assert(body.GetProperty("errorReference").GetString() == logger.LastErrorReference, "ProblemDetails ErrorReference is not searchable through the logger.");
        Assert(!body.ToString().Contains(exception.Message, StringComparison.Ordinal), "Sensitive exception details were returned to the client.");
    }

    private static async Task FrontendErrorEndpointLogsErrorAsync()
    {
        var logger = new RecordingErrorLogger();
        var controller = new SystemErrorsController(logger)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = CreateHttpContext()
            }
        };
        var request = new SystemErrorsController.ClientErrorRequest(
            "frontend runtime failure",
            "client stack",
            "/dashboard",
            "render");

        var result = await controller.LogClientError(request, CancellationToken.None);

        Assert(result is AcceptedResult, "Frontend error endpoint did not return 202 Accepted.");
        Assert(logger.LastSource == "FRONTEND", "Frontend error was not logged with FRONTEND source.");
        Assert(logger.LastContextJson?.Contains("/dashboard", StringComparison.Ordinal) == true, "Frontend context was not logged.");
    }

    private static DefaultHttpContext CreateHttpContext()
    {
        var context = new DefaultHttpContext();
        context.Items[CorrelationIdMiddleware.HeaderName] = Guid.NewGuid();
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class RecordingErrorLogger : IApplicationErrorLogger
    {
        public string? LastSource { get; private set; }
        public string? LastErrorReference { get; private set; }
        public string? LastContextJson { get; private set; }

        public Task<string> LogAsync(
            Exception exception,
            HttpContext context,
            string source = "BACKEND",
            string? errorReference = null,
            string? contextJson = null,
            CancellationToken cancellationToken = default)
        {
            LastSource = source;
            LastErrorReference = errorReference ?? ErrorReferences.Create();
            LastContextJson = contextJson;
            return Task.FromResult(LastErrorReference);
        }
    }
}
