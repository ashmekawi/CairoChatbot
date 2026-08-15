using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Chatbot.Api.Identity;
using Chatbot.Api.Middleware;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Chatbot.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController(AuthService authService) : ControllerBase
{
    public sealed record LoginRequest(
        [property: Required, StringLength(100)] string Username,
        [property: Required, StringLength(500)] string Password);

    public sealed record RefreshRequest([property: Required] string RefreshToken);

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<ActionResult<TokenPair>> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        return Ok(await authService.LoginAsync(
            request.Username,
            request.Password,
            CorrelationId(),
            cancellationToken));
    }

    [AllowAnonymous]
    [HttpPost("refresh")]
    public async Task<ActionResult<TokenPair>> Refresh(RefreshRequest request, CancellationToken cancellationToken)
    {
        return Ok(await authService.RefreshAsync(request.RefreshToken, CorrelationId(), cancellationToken));
    }

    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(RefreshRequest request, CancellationToken cancellationToken)
    {
        await authService.LogoutAsync(request.RefreshToken, ActorUserId(), CorrelationId(), cancellationToken);
        return NoContent();
    }

    private Guid CorrelationId()
    {
        return HttpContext.Items[CorrelationIdMiddleware.HeaderName] is Guid value ? value : Guid.NewGuid();
    }

    private Guid? ActorUserId()
    {
        return Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var value) ? value : null;
    }
}
