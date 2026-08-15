namespace Chatbot.Api.Contacts;

public interface IContactStore
{
    Task CreateAsync(ContactRecord contact, Guid? actorId, Guid correlationId, CancellationToken cancellationToken);
    Task<ContactRecord?> GetAsync(Guid publicId, Guid correlationId, CancellationToken cancellationToken);
    Task UpdateAsync(ContactRecord contact, Guid actorId, Guid correlationId, CancellationToken cancellationToken);
    Task SetActiveAsync(Guid publicId, bool isActive, Guid actorId, Guid correlationId, CancellationToken cancellationToken);
}

public interface IChannelIdentityStore
{
    Task CreateAsync(ChannelIdentityRecord identity, Guid? actorId, Guid correlationId, CancellationToken cancellationToken);
    Task<ChannelIdentityRecord?> GetAsync(Guid publicId, Guid correlationId, CancellationToken cancellationToken);
    Task<ChannelIdentityRecord?> GetByExternalIdAsync(Guid channelPublicId, string externalId, Guid correlationId, CancellationToken cancellationToken);
    Task UpdateAsync(ChannelIdentityRecord identity, Guid actorId, Guid correlationId, CancellationToken cancellationToken);
    Task SetActiveAsync(Guid publicId, bool isActive, Guid actorId, Guid correlationId, CancellationToken cancellationToken);
    Task SetVerifiedAsync(Guid publicId, bool isVerified, Guid actorId, Guid correlationId, CancellationToken cancellationToken);
}
