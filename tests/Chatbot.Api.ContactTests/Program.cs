using System.Security.Claims;
using Chatbot.Api.Contacts;
using Chatbot.Core.Errors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

await ContactTests.RunAsync();

internal static class ContactTests
{
    private static readonly Guid CorrelationId = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();

    public static async Task RunAsync()
    {
        await ContactLifecycleAsync();
        await IdentityLifecycleAndLookupAsync();
        await InvalidReferencesAndDuplicateAreRejectedAsync();
        NormalizationWorks();
        await AuthorizationWorksAsync();
        await SafeErrorHandlingAsync();
        Console.WriteLine("V0004 contacts and identities tests passed.");
    }

    private static async Task ContactLifecycleAsync()
    {
        var store = new FakeStore();
        var service = new ContactService(store);
        var publicId = await service.CreateAsync(Contact(Guid.Empty, "Citizen"), ActorId, CorrelationId, default);
        Assert((await service.GetAsync(publicId, CorrelationId, default)).DisplayName == "Citizen", "Contact was not read.");
        await service.UpdateAsync(Contact(publicId, "Updated"), ActorId, CorrelationId, default);
        Assert(store.Contact?.DisplayName == "Updated", "Contact was not updated.");
        await service.SetActiveAsync(publicId, false, ActorId, CorrelationId, default);
        Assert(store.Contact?.IsActive == false, "Contact active state was not updated.");
    }

    private static async Task IdentityLifecycleAndLookupAsync()
    {
        var store = new FakeStore();
        var contactId = await new ContactService(store).CreateAsync(Contact(Guid.Empty, "Citizen"), ActorId, CorrelationId, default);
        var service = new ChannelIdentityService(store, new WhatsAppAddressNormalizer());
        var identityId = await service.CreateAsync(
            Identity(Guid.Empty, contactId, store.ChannelId, "wa-1", "+20 10 1234 5678"),
            ActorId,
            CorrelationId,
            default);
        var identity = await service.GetAsync(identityId, CorrelationId, default);
        Assert(identity.NormalizedAddress == "201012345678", "Identity address was not normalized.");

        var lookup = await service.GetByExternalIdAsync(store.ChannelId, "wa-1", CorrelationId, default);
        Assert(lookup?.PublicId == identityId, "Channel and external ID lookup failed.");
        await service.UpdateAsync(identity with { DisplayAddress = "Main" }, ActorId, CorrelationId, default);
        Assert(store.Identity?.DisplayAddress == "Main", "Identity was not updated.");
        await service.SetActiveAsync(identityId, false, ActorId, CorrelationId, default);
        Assert(store.Identity?.IsActive == false, "Identity active state was not updated.");
        await service.SetVerifiedAsync(identityId, true, ActorId, CorrelationId, default);
        Assert(store.Identity?.IsVerified == true && store.Identity.VerifiedAtUtc is not null, "Identity was not verified.");
        await service.SetVerifiedAsync(identityId, false, ActorId, CorrelationId, default);
        Assert(store.Identity?.IsVerified == false && store.Identity.VerifiedAtUtc is null, "Identity was not unverified.");
    }

    private static async Task InvalidReferencesAndDuplicateAreRejectedAsync()
    {
        var store = new FakeStore();
        var service = new ChannelIdentityService(store, new WhatsAppAddressNormalizer());
        await ExpectErrorAsync(
            () => service.CreateAsync(Identity(Guid.Empty, Guid.NewGuid(), store.ChannelId, "invalid-contact", "2010"), ActorId, CorrelationId, default),
            "contact_not_found");

        var contactId = await new ContactService(store).CreateAsync(Contact(Guid.Empty, null), ActorId, CorrelationId, default);
        await ExpectErrorAsync(
            () => service.CreateAsync(Identity(Guid.Empty, contactId, Guid.NewGuid(), "invalid-channel", "2010"), ActorId, CorrelationId, default),
            "channel_not_found");

        var identity = Identity(Guid.Empty, contactId, store.ChannelId, "duplicate", "2010");
        await service.CreateAsync(identity, ActorId, CorrelationId, default);
        await ExpectErrorAsync(
            () => service.CreateAsync(identity, ActorId, CorrelationId, default),
            "duplicate_channel_identity");
    }

    private static void NormalizationWorks()
    {
        var normalizer = new WhatsAppAddressNormalizer();
        Assert(normalizer.Normalize("+20 10 1234 5678") == "201012345678", "Plus format failed.");
        Assert(normalizer.Normalize("00201012345678") == "201012345678", "00 format failed.");
        Assert(normalizer.Normalize("201012345678") == "201012345678", "Canonical format changed.");
    }

    private static async Task AuthorizationWorksAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization(ContactPermissions.AddPolicies);
        await using var provider = services.BuildServiceProvider();
        var authorization = provider.GetRequiredService<IAuthorizationService>();
        var deniedUser = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "user")], "Test"));
        var denied = await authorization.AuthorizeAsync(deniedUser, null, ContactPermissions.Read);
        Assert((denied.Succeeded ? 200 : 403) == 403, "User without permission was not forbidden.");

        var allowedUser = new ClaimsPrincipal(new ClaimsIdentity([new Claim("permission", ContactPermissions.Read)], "Test"));
        var allowed = await authorization.AuthorizeAsync(allowedUser, null, ContactPermissions.Read);
        Assert(allowed.Succeeded, "User with permission was not allowed.");
    }

    private static async Task SafeErrorHandlingAsync()
    {
        var error = await CaptureErrorAsync(
            () => new ContactService(new FakeStore()).GetAsync(Guid.NewGuid(), CorrelationId, default));
        Assert(error.StatusCode == 404 && error.ErrorType == "contact_not_found", "Contact error was not safe.");
    }

    private static ContactRecord Contact(Guid publicId, string? name)
    {
        return new ContactRecord(0, publicId, name, "ar", true);
    }

    private static ChannelIdentityRecord Identity(
        Guid publicId,
        Guid contactId,
        Guid channelId,
        string externalId,
        string address)
    {
        return new ChannelIdentityRecord(
            0, publicId, contactId, channelId, externalId, address, null, false, null, true, null);
    }

    private static async Task ExpectErrorAsync(Func<Task> action, string errorType)
    {
        var error = await CaptureErrorAsync(action);
        Assert(error.ErrorType == errorType, $"Expected {errorType}, received {error.ErrorType}.");
    }

    private static async Task<AppException> CaptureErrorAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (AppException exception)
        {
            return exception;
        }
        throw new InvalidOperationException("Expected AppException was not thrown.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class FakeStore : IContactStore, IChannelIdentityStore
    {
        public Guid ChannelId { get; } = Guid.NewGuid();
        public ContactRecord? Contact { get; private set; }
        public ChannelIdentityRecord? Identity { get; private set; }

        public Task CreateAsync(ContactRecord contact, Guid? actorId, Guid correlationId, CancellationToken cancellationToken)
        {
            Contact = contact with { ContactId = 1 };
            return Task.CompletedTask;
        }

        public Task<ContactRecord?> GetAsync(Guid publicId, Guid correlationId, CancellationToken cancellationToken)
        {
            return Task.FromResult(Contact?.PublicId == publicId ? Contact : null);
        }

        public Task UpdateAsync(ContactRecord contact, Guid actorId, Guid correlationId, CancellationToken cancellationToken)
        {
            Contact = contact with { ContactId = Contact?.ContactId ?? 1 };
            return Task.CompletedTask;
        }

        Task IContactStore.SetActiveAsync(Guid publicId, bool isActive, Guid actorId, Guid correlationId, CancellationToken cancellationToken)
        {
            Contact = Contact! with { IsActive = isActive };
            return Task.CompletedTask;
        }

        Task IChannelIdentityStore.CreateAsync(ChannelIdentityRecord identity, Guid? actorId, Guid correlationId, CancellationToken cancellationToken)
        {
            if (Contact?.PublicId != identity.ContactPublicId)
            {
                throw new AppException("Contact was not found.", 404, "contact_not_found");
            }
            if (ChannelId != identity.ChannelPublicId)
            {
                throw new AppException("Channel was not found.", 404, "channel_not_found");
            }
            if (Identity?.ExternalId == identity.ExternalId && Identity.ChannelPublicId == identity.ChannelPublicId)
            {
                throw new AppException("Identity already exists.", 409, "duplicate_channel_identity");
            }
            Identity = identity with { ChannelIdentityId = 1 };
            return Task.CompletedTask;
        }

        Task<ChannelIdentityRecord?> IChannelIdentityStore.GetAsync(Guid publicId, Guid correlationId, CancellationToken cancellationToken)
        {
            return Task.FromResult(Identity?.PublicId == publicId ? Identity : null);
        }

        Task<ChannelIdentityRecord?> IChannelIdentityStore.GetByExternalIdAsync(Guid channelPublicId, string externalId, Guid correlationId, CancellationToken cancellationToken)
        {
            return Task.FromResult(Identity?.ChannelPublicId == channelPublicId && Identity.ExternalId == externalId ? Identity : null);
        }

        Task IChannelIdentityStore.UpdateAsync(ChannelIdentityRecord identity, Guid actorId, Guid correlationId, CancellationToken cancellationToken)
        {
            Identity = identity with { ChannelIdentityId = Identity?.ChannelIdentityId ?? 1 };
            return Task.CompletedTask;
        }

        Task IChannelIdentityStore.SetActiveAsync(Guid publicId, bool isActive, Guid actorId, Guid correlationId, CancellationToken cancellationToken)
        {
            Identity = Identity! with { IsActive = isActive };
            return Task.CompletedTask;
        }

        Task IChannelIdentityStore.SetVerifiedAsync(Guid publicId, bool isVerified, Guid actorId, Guid correlationId, CancellationToken cancellationToken)
        {
            Identity = Identity! with
            {
                IsVerified = isVerified,
                VerifiedAtUtc = isVerified ? DateTime.UtcNow : null
            };
            return Task.CompletedTask;
        }
    }
}
