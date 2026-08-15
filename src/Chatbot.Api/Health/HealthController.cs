using Microsoft.AspNetCore.Mvc;

namespace Chatbot.Api.Health;

[ApiController]
public sealed class HealthController : ControllerBase
{
    [HttpGet("/health/live")]
    public IActionResult Live() => Ok(new { status = "Healthy" });
}
