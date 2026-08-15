using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Chatbot.Api.Logging;

namespace Chatbot.Api.Controllers;

[ApiController]
[Route("api/v1/system/client-errors")]
public sealed class SystemErrorsController(IApplicationErrorLogger logger) : ControllerBase
{
    public sealed record ClientErrorRequest(
        [property: Required, StringLength(4000)] string Message,
        [property: StringLength(16000)] string? Stack,
        [property: StringLength(1000)] string? Page,
        [property: StringLength(200)] string? Action);

    [HttpPost]
    public async Task<IActionResult> LogClientError([FromBody] ClientErrorRequest request, CancellationToken cancellationToken)
    {
        var exception = new ClientReportedException(request.Message, request.Stack);
        var contextJson = JsonSerializer.Serialize(new
        {
            request.Page,
            request.Action
        });
        var reference = await logger.LogAsync(
            exception,
            HttpContext,
            "FRONTEND",
            contextJson: contextJson,
            cancellationToken: cancellationToken);

        return Accepted(new { errorReference = reference });
    }

    private sealed class ClientReportedException(string message, string? clientStack) : Exception(message)
    {
        public override string? StackTrace => clientStack;
    }
}
