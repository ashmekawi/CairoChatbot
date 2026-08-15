using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Chatbot.Api.Middleware;
using Chatbot.Api.Projects;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Chatbot.Api.Controllers;

[ApiController]
[Authorize]
public sealed class ChannelsController(ChannelService channels) : ControllerBase
{
    public sealed record ChannelRequest(
        [property: Required, StringLength(100)] string Code,
        [property: Required, StringLength(50)] string ChannelType,
        [property: Required, StringLength(50)] string ProviderCode,
        bool IsActive,
        string? ConfigurationJson);

    public sealed record ActiveRequest(bool IsActive);

    [Authorize(Policy = ProjectPermissions.ManageChannels)]
    [HttpPost("api/v1/projects/{projectPublicId:guid}/channels")]
    public async Task<IActionResult> Create(
        Guid projectPublicId,
        ChannelRequest request,
        CancellationToken cancellationToken)
    {
        var publicId = await channels.CreateAsync(
            ToRecord(Guid.Empty, projectPublicId, request),
            ActorIdOrNull(),
            CorrelationId(),
            cancellationToken);
        return CreatedAtAction(nameof(Get), new { channelPublicId = publicId }, new { publicId });
    }

    [Authorize(Policy = ProjectPermissions.ReadChannels)]
    [HttpGet("api/v1/channels/{channelPublicId:guid}")]
    public async Task<IActionResult> Get(Guid channelPublicId, CancellationToken cancellationToken)
    {
        return Ok(await channels.GetAsync(channelPublicId, CorrelationId(), cancellationToken));
    }

    [Authorize(Policy = ProjectPermissions.ManageChannels)]
    [HttpPut("api/v1/channels/{channelPublicId:guid}")]
    public async Task<IActionResult> Update(
        Guid channelPublicId,
        ChannelRequest request,
        CancellationToken cancellationToken)
    {
        await channels.UpdateAsync(
            ToRecord(channelPublicId, Guid.Empty, request),
            ActorId(),
            CorrelationId(),
            cancellationToken);
        return NoContent();
    }

    [Authorize(Policy = ProjectPermissions.ManageChannels)]
    [HttpPatch("api/v1/channels/{channelPublicId:guid}/active")]
    public async Task<IActionResult> SetActive(
        Guid channelPublicId,
        ActiveRequest request,
        CancellationToken cancellationToken)
    {
        await channels.SetActiveAsync(channelPublicId, request.IsActive, ActorId(), CorrelationId(), cancellationToken);
        return NoContent();
    }

    private static ChannelRecord ToRecord(Guid publicId, Guid projectPublicId, ChannelRequest request)
    {
        return new ChannelRecord(
            0,
            publicId,
            projectPublicId,
            request.Code,
            request.ChannelType,
            request.ProviderCode,
            request.IsActive,
            request.ConfigurationJson);
    }

    private Guid CorrelationId()
    {
        return HttpContext.Items[CorrelationIdMiddleware.HeaderName] is Guid id ? id : Guid.NewGuid();
    }

    private Guid ActorId()
    {
        return ActorIdOrNull() ?? throw new UnauthorizedAccessException("Authenticated user identifier is missing.");
    }

    private Guid? ActorIdOrNull()
    {
        return Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    }
}
