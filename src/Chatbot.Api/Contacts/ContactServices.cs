using Chatbot.Core.Errors;

namespace Chatbot.Api.Contacts;

public sealed class ContactService(IContactStore store)
{
    public async Task<Guid> CreateAsync(ContactRecord input, Guid? actorId, Guid correlationId, CancellationToken cancellationToken)
    {
        var contact = input with { PublicId = Guid.NewGuid() };
        await store.CreateAsync(contact, actorId, correlationId, cancellationToken);
        return contact.PublicId;
    }

    public async Task<ContactRecord> GetAsync(Guid publicId, Guid correlationId, CancellationToken cancellationToken)
    {
        return await store.GetAsync(publicId, correlationId, cancellationToken)
            ?? throw new AppException("Contact was not found.", 404, "contact_not_found");
    }

    public Task UpdateAsync(ContactRecord contact, Guid actorId, Guid correlationId, CancellationToken cancellationToken)
    {
        return store.UpdateAsync(contact, actorId, correlationId, cancellationToken);
    }

    public Task SetActiveAsync(Guid publicId, bool isActive, Guid actorId, Guid correlationId, CancellationToken cancellationToken)
    {
        return store.SetActiveAsync(publicId, isActive, actorId, correlationId, cancellationToken);
    }
}

public sealed class ChannelIdentityService(IChannelIdentityStore store, WhatsAppAddressNormalizer normalizer)
{
    public async Task<Guid> CreateAsync(ChannelIdentityRecord input, Guid? actorId, Guid correlationId, CancellationToken cancellationToken)
    {
        var identity = input with
        {
            PublicId = Guid.NewGuid(),
            NormalizedAddress = normalizer.Normalize(input.NormalizedAddress)
        };
        await store.CreateAsync(identity, actorId, correlationId, cancellationToken);
        return identity.PublicId;
    }

    public async Task<ChannelIdentityRecord> GetAsync(Guid publicId, Guid correlationId, CancellationToken cancellationToken)
    {
        return await store.GetAsync(publicId, correlationId, cancellationToken)
            ?? throw new AppException("Channel identity was not found.", 404, "channel_identity_not_found");
    }

    public Task<ChannelIdentityRecord?> GetByExternalIdAsync(
        Guid channelPublicId,
        string externalId,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        return store.GetByExternalIdAsync(channelPublicId, externalId, correlationId, cancellationToken);
    }

    public Task UpdateAsync(ChannelIdentityRecord identity, Guid actorId, Guid correlationId, CancellationToken cancellationToken)
    {
        return store.UpdateAsync(
            identity with { NormalizedAddress = normalizer.Normalize(identity.NormalizedAddress) },
            actorId,
            correlationId,
            cancellationToken);
    }

    public Task SetActiveAsync(Guid publicId, bool isActive, Guid actorId, Guid correlationId, CancellationToken cancellationToken)
    {
        return store.SetActiveAsync(publicId, isActive, actorId, correlationId, cancellationToken);
    }

    public Task SetVerifiedAsync(Guid publicId, bool isVerified, Guid actorId, Guid correlationId, CancellationToken cancellationToken)
    {
        return store.SetVerifiedAsync(publicId, isVerified, actorId, correlationId, cancellationToken);
    }
}
