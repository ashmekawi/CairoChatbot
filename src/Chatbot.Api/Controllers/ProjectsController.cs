using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Chatbot.Api.Middleware;
using Chatbot.Api.Projects;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Chatbot.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/projects")]
public sealed class ProjectsController(ProjectService projects, BusinessHoursService businessHours) : ControllerBase
{
    public sealed record ProjectRequest(
        [property: Required, StringLength(100)] string Code,
        [property: Required, StringLength(200)] string NameAr,
        [property: StringLength(200)] string? NameEn,
        [property: Required, StringLength(10)] string DefaultLanguage,
        [property: Required, StringLength(100)] string TimeZoneId,
        bool IsActive = true);

    public sealed record ActiveRequest(bool IsActive);

    [Authorize(Policy = ProjectPermissions.ManageProjects)]
    [HttpPost]
    public async Task<IActionResult> Create(ProjectRequest request, CancellationToken cancellationToken)
    {
        var publicId = await projects.CreateAsync(ToRecord(Guid.Empty, request), ActorIdOrNull(), CorrelationId(), cancellationToken);
        return CreatedAtAction(nameof(Get), new { publicId }, new { publicId });
    }

    [Authorize(Policy = ProjectPermissions.ReadProjects)]
    [HttpGet("{publicId:guid}")]
    public async Task<IActionResult> Get(Guid publicId, CancellationToken cancellationToken)
    {
        return Ok(await projects.GetAsync(publicId, CorrelationId(), cancellationToken));
    }

    [Authorize(Policy = ProjectPermissions.ManageProjects)]
    [HttpPut("{publicId:guid}")]
    public async Task<IActionResult> Update(Guid publicId, ProjectRequest request, CancellationToken cancellationToken)
    {
        await projects.UpdateAsync(ToRecord(publicId, request), ActorId(), CorrelationId(), cancellationToken);
        return NoContent();
    }

    [Authorize(Policy = ProjectPermissions.ManageProjects)]
    [HttpPatch("{publicId:guid}/active")]
    public async Task<IActionResult> SetActive(Guid publicId, ActiveRequest request, CancellationToken cancellationToken)
    {
        await projects.SetActiveAsync(publicId, request.IsActive, ActorId(), CorrelationId(), cancellationToken);
        return NoContent();
    }

    [Authorize(Policy = ProjectPermissions.ReadProjects)]
    [HttpGet("{projectPublicId:guid}/business-hours")]
    public async Task<IActionResult> GetBusinessHours(Guid projectPublicId, CancellationToken cancellationToken)
    {
        return Ok(await businessHours.GetAsync(projectPublicId, CorrelationId(), cancellationToken));
    }

    [Authorize(Policy = ProjectPermissions.ManageProjects)]
    [HttpPut("{projectPublicId:guid}/business-hours")]
    public async Task<IActionResult> UpdateBusinessHours(
        Guid projectPublicId,
        IReadOnlyCollection<BusinessHourRecord> request,
        CancellationToken cancellationToken)
    {
        await businessHours.UpsertAsync(projectPublicId, request, ActorId(), CorrelationId(), cancellationToken);
        return NoContent();
    }

    private static ProjectRecord ToRecord(Guid publicId, ProjectRequest request)
    {
        return new ProjectRecord(
            0,
            publicId,
            request.Code,
            request.NameAr,
            request.NameEn,
            request.DefaultLanguage,
            request.TimeZoneId,
            request.IsActive);
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
