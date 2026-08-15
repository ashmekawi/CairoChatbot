using Chatbot.Core.Errors;
using Microsoft.AspNetCore.Identity;

namespace Chatbot.Api.Identity;

public sealed class UserAdminService(IIdentityStore store, PasswordHasher<IdentityUser> passwordHasher)
{
    public async Task<Guid> CreateAsync(
        string username,
        string displayName,
        string password,
        bool isActive,
        Guid? actorUserId,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        var user = new IdentityUser(
            0,
            Guid.NewGuid(),
            username.Trim(),
            displayName.Trim(),
            null,
            isActive,
            0,
            null);
        user = user with { PasswordHash = passwordHasher.HashPassword(user, password) };
        await store.CreateUserAsync(user, actorUserId, correlationId, cancellationToken);
        return user.PublicId;
    }

    public async Task<IdentityUser> GetAsync(Guid publicId, Guid correlationId, CancellationToken cancellationToken)
    {
        return await store.GetByPublicIdAsync(publicId, correlationId, cancellationToken)
            ?? throw new AppException("User was not found.", 404, "user_not_found");
    }

    public Task SetActiveAsync(Guid publicId, bool isActive, Guid actorUserId, Guid correlationId, CancellationToken cancellationToken)
    {
        return store.SetActiveAsync(publicId, isActive, actorUserId, correlationId, cancellationToken);
    }

    public async Task SetPasswordAsync(Guid publicId, string password, Guid actorUserId, Guid correlationId, CancellationToken cancellationToken)
    {
        var user = await GetAsync(publicId, correlationId, cancellationToken);
        var passwordHash = passwordHasher.HashPassword(user, password);
        await store.SetPasswordAsync(publicId, passwordHash, actorUserId, correlationId, cancellationToken);
    }

    public Task AssignRoleAsync(Guid publicId, string roleCode, Guid actorUserId, Guid correlationId, CancellationToken cancellationToken)
    {
        return store.AssignRoleAsync(publicId, roleCode.Trim(), actorUserId, correlationId, cancellationToken);
    }

    public Task RemoveRoleAsync(Guid publicId, string roleCode, Guid actorUserId, Guid correlationId, CancellationToken cancellationToken)
    {
        return store.RemoveRoleAsync(publicId, roleCode.Trim(), actorUserId, correlationId, cancellationToken);
    }
}
