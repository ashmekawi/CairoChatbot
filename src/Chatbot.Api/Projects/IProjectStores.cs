namespace Chatbot.Api.Projects;

public interface IProjectStore
{
    Task CreateAsync(ProjectRecord project, Guid? actorId, Guid correlationId, CancellationToken cancellationToken);
    Task<ProjectRecord?> GetAsync(Guid publicId, Guid correlationId, CancellationToken cancellationToken);
    Task UpdateAsync(ProjectRecord project, Guid actorId, Guid correlationId, CancellationToken cancellationToken);
    Task SetActiveAsync(Guid publicId, bool isActive, Guid actorId, Guid correlationId, CancellationToken cancellationToken);
}

public interface IChannelStore
{
    Task CreateAsync(ChannelRecord channel, Guid? actorId, Guid correlationId, CancellationToken cancellationToken);
    Task<ChannelRecord?> GetAsync(Guid publicId, Guid correlationId, CancellationToken cancellationToken);
    Task UpdateAsync(ChannelRecord channel, Guid actorId, Guid correlationId, CancellationToken cancellationToken);
    Task SetActiveAsync(Guid publicId, bool isActive, Guid actorId, Guid correlationId, CancellationToken cancellationToken);
}

public interface IBusinessHoursStore
{
    Task<IReadOnlyList<BusinessHourRecord>> GetAsync(Guid projectPublicId, Guid correlationId, CancellationToken cancellationToken);
    Task UpsertAsync(Guid projectPublicId, BusinessHourRecord hours, Guid actorId, Guid correlationId, CancellationToken cancellationToken);
}
