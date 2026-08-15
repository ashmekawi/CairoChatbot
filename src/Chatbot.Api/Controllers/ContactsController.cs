using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Chatbot.Api.Contacts;
using Chatbot.Api.Middleware;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Chatbot.Api.Controllers;

[ApiController]
[Authorize]
public sealed class ContactsController(ContactService contacts, ChannelIdentityService identities) : ControllerBase
{
    public sealed record ContactRequest(
        [property: StringLength(200)] string? DisplayName,
        [property: StringLength(10)] string? PreferredLanguage,
        bool IsActive = true);

    public sealed record IdentityRequest(
        Guid ChannelPublicId,
        [property: Required, StringLength(200)] string ExternalId,
        [property: Required, StringLength(200)] string Address,
        [property: StringLength(200)] string? DisplayAddress,
        bool IsVerified = false,
        bool IsActive = true);

    public sealed record ActiveRequest(bool IsActive);
    public sealed record VerifiedRequest(bool IsVerified);

    [Authorize(Policy = ContactPermissions.Manage)]
    [HttpPost("api/v1/contacts")]
    public async Task<IActionResult> Create(ContactRequest request, CancellationToken cancellationToken)
    {
        var publicId = await contacts.CreateAsync(ToContact(Guid.Empty, request), ActorIdOrNull(), CorrelationId(), cancellationToken);
        return CreatedAtAction(nameof(Get), new { contactPublicId = publicId }, new { publicId });
    }

    [Authorize(Policy = ContactPermissions.Read)]
    [HttpGet("api/v1/contacts/{contactPublicId:guid}")]
    public async Task<IActionResult> Get(Guid contactPublicId, CancellationToken cancellationToken)
    {
        return Ok(await contacts.GetAsync(contactPublicId, CorrelationId(), cancellationToken));
    }

    [Authorize(Policy = ContactPermissions.Manage)]
    [HttpPut("api/v1/contacts/{contactPublicId:guid}")]
    public async Task<IActionResult> Update(Guid contactPublicId, ContactRequest request, CancellationToken cancellationToken)
    {
        await contacts.UpdateAsync(ToContact(contactPublicId, request), ActorId(), CorrelationId(), cancellationToken);
        return NoContent();
    }

    [Authorize(Policy = ContactPermissions.Manage)]
    [HttpPatch("api/v1/contacts/{contactPublicId:guid}/active")]
    public async Task<IActionResult> SetActive(Guid contactPublicId, ActiveRequest request, CancellationToken cancellationToken)
    {
        await contacts.SetActiveAsync(contactPublicId, request.IsActive, ActorId(), CorrelationId(), cancellationToken);
        return NoContent();
    }

    [Authorize(Policy = ContactPermissions.Manage)]
    [HttpPost("api/v1/contacts/{contactPublicId:guid}/identities")]
    public async Task<IActionResult> CreateIdentity(
        Guid contactPublicId,
        IdentityRequest request,
        CancellationToken cancellationToken)
    {
        var publicId = await identities.CreateAsync(
            ToIdentity(Guid.Empty, contactPublicId, request),
            ActorIdOrNull(),
            CorrelationId(),
            cancellationToken);
        return CreatedAtAction(nameof(GetIdentity), new { identityPublicId = publicId }, new { publicId });
    }

    [Authorize(Policy = ContactPermissions.Read)]
    [HttpGet("api/v1/contact-identities/{identityPublicId:guid}")]
    public async Task<IActionResult> GetIdentity(Guid identityPublicId, CancellationToken cancellationToken)
    {
        return Ok(await identities.GetAsync(identityPublicId, CorrelationId(), cancellationToken));
    }

    [Authorize(Policy = ContactPermissions.Manage)]
    [HttpPut("api/v1/contact-identities/{identityPublicId:guid}")]
    public async Task<IActionResult> UpdateIdentity(
        Guid identityPublicId,
        IdentityRequest request,
        CancellationToken cancellationToken)
    {
        await identities.UpdateAsync(
            ToIdentity(identityPublicId, Guid.Empty, request),
            ActorId(),
            CorrelationId(),
            cancellationToken);
        return NoContent();
    }

    [Authorize(Policy = ContactPermissions.Manage)]
    [HttpPatch("api/v1/contact-identities/{identityPublicId:guid}/active")]
    public async Task<IActionResult> SetIdentityActive(
        Guid identityPublicId,
        ActiveRequest request,
        CancellationToken cancellationToken)
    {
        await identities.SetActiveAsync(identityPublicId, request.IsActive, ActorId(), CorrelationId(), cancellationToken);
        return NoContent();
    }

    [Authorize(Policy = ContactPermissions.Manage)]
    [HttpPatch("api/v1/contact-identities/{identityPublicId:guid}/verified")]
    public async Task<IActionResult> SetIdentityVerified(
        Guid identityPublicId,
        VerifiedRequest request,
        CancellationToken cancellationToken)
    {
        await identities.SetVerifiedAsync(identityPublicId, request.IsVerified, ActorId(), CorrelationId(), cancellationToken);
        return NoContent();
    }

    private static ContactRecord ToContact(Guid publicId, ContactRequest request)
    {
        return new ContactRecord(0, publicId, request.DisplayName, request.PreferredLanguage, request.IsActive);
    }

    private static ChannelIdentityRecord ToIdentity(Guid publicId, Guid contactId, IdentityRequest request)
    {
        return new ChannelIdentityRecord(
            0, publicId, contactId, request.ChannelPublicId, request.ExternalId, request.Address,
            request.DisplayAddress, request.IsVerified, null, request.IsActive, null);
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
