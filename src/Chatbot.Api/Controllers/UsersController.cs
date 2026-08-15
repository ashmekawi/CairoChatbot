using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Chatbot.Api.Identity;
using Chatbot.Api.Middleware;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Chatbot.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/users")]
public sealed class UsersController(UserAdminService users) : ControllerBase
{
    public sealed record CreateUserRequest(
        [property: Required, StringLength(100)] string Username,
        [property: Required, StringLength(200)] string DisplayName,
        [property: Required, MinLength(12), StringLength(500)] string Password,
        bool IsActive = true);

    public sealed record ActiveRequest(bool IsActive);
    public sealed record PasswordRequest([property: Required, MinLength(12), StringLength(500)] string Password);
    public sealed record RoleRequest([property: Required, StringLength(100)] string RoleCode);

    [Authorize(Policy = UserPermissions.Create)]
    [HttpPost]
    public async Task<IActionResult> Create(CreateUserRequest request, CancellationToken cancellationToken)
    {
        var publicId = await users.CreateAsync(
            request.Username,
            request.DisplayName,
            request.Password,
            request.IsActive,
            ActorUserIdOrNull(),
            CorrelationId(),
            cancellationToken);
        return CreatedAtAction(nameof(Get), new { publicId }, new { publicId });
    }

    [Authorize(Policy = UserPermissions.Read)]
    [HttpGet("{publicId:guid}")]
    public async Task<IActionResult> Get(Guid publicId, CancellationToken cancellationToken)
    {
        var user = await users.GetAsync(publicId, CorrelationId(), cancellationToken);
        return Ok(new
        {
            user.PublicId,
            user.Username,
            user.DisplayName,
            user.IsActive,
            user.FailedLoginCount,
            user.LockedUntilUtc
        });
    }

    [Authorize(Policy = UserPermissions.Activate)]
    [HttpPatch("{publicId:guid}/active")]
    public async Task<IActionResult> SetActive(Guid publicId, ActiveRequest request, CancellationToken cancellationToken)
    {
        await users.SetActiveAsync(publicId, request.IsActive, ActorUserId(), CorrelationId(), cancellationToken);
        return NoContent();
    }

    [Authorize(Policy = UserPermissions.ResetPassword)]
    [HttpPost("{publicId:guid}/password")]
    public async Task<IActionResult> SetPassword(Guid publicId, PasswordRequest request, CancellationToken cancellationToken)
    {
        await users.SetPasswordAsync(publicId, request.Password, ActorUserId(), CorrelationId(), cancellationToken);
        return NoContent();
    }

    [Authorize(Policy = UserPermissions.ManageRoles)]
    [HttpPost("{publicId:guid}/roles")]
    public async Task<IActionResult> AssignRole(Guid publicId, RoleRequest request, CancellationToken cancellationToken)
    {
        await users.AssignRoleAsync(publicId, request.RoleCode, ActorUserId(), CorrelationId(), cancellationToken);
        return NoContent();
    }

    [Authorize(Policy = UserPermissions.ManageRoles)]
    [HttpDelete("{publicId:guid}/roles/{roleCode}")]
    public async Task<IActionResult> RemoveRole(Guid publicId, string roleCode, CancellationToken cancellationToken)
    {
        await users.RemoveRoleAsync(publicId, roleCode, ActorUserId(), CorrelationId(), cancellationToken);
        return NoContent();
    }

    private Guid CorrelationId()
    {
        return HttpContext.Items[CorrelationIdMiddleware.HeaderName] is Guid value ? value : Guid.NewGuid();
    }

    private Guid ActorUserId()
    {
        return ActorUserIdOrNull() ?? throw new UnauthorizedAccessException("Authenticated user identifier is missing.");
    }

    private Guid? ActorUserIdOrNull()
    {
        return Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var value) ? value : null;
    }
}
