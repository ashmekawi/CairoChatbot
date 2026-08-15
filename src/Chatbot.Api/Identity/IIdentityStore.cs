namespace Chatbot.Api.Identity;

public interface IIdentityStore
{
    Task<IdentityUser?> GetByUsernameAsync(string username, Guid correlationId, CancellationToken cancellationToken);
    Task<IdentityUser?> GetByPublicIdAsync(Guid publicId, Guid correlationId, CancellationToken cancellationToken);
    Task CreateUserAsync(IdentityUser user, Guid? actorUserId, Guid correlationId, CancellationToken cancellationToken);
    Task SetActiveAsync(Guid publicId, bool isActive, Guid actorUserId, Guid correlationId, CancellationToken cancellationToken);
    Task SetPasswordAsync(Guid publicId, string passwordHash, Guid actorUserId, Guid correlationId, CancellationToken cancellationToken);
    Task RecordLoginSuccessAsync(long userId, Guid correlationId, CancellationToken cancellationToken);
    Task RecordLoginFailureAsync(long userId, int maximumAttempts, int lockoutMinutes, Guid correlationId, CancellationToken cancellationToken);
    Task AuditLoginFailureAsync(string username, Guid correlationId, CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> GetPermissionsAsync(long userId, Guid correlationId, CancellationToken cancellationToken);
    Task AssignRoleAsync(Guid publicId, string roleCode, Guid actorUserId, Guid correlationId, CancellationToken cancellationToken);
    Task RemoveRoleAsync(Guid publicId, string roleCode, Guid actorUserId, Guid correlationId, CancellationToken cancellationToken);
    Task CreateRefreshTokenAsync(long userId, string tokenHash, DateTime expiresAtUtc, Guid correlationId, CancellationToken cancellationToken);
    Task<ValidRefreshToken?> GetValidRefreshTokenAsync(string tokenHash, Guid correlationId, CancellationToken cancellationToken);
    Task RotateRefreshTokenAsync(string currentHash, string newHash, DateTime expiresAtUtc, Guid correlationId, CancellationToken cancellationToken);
    Task RevokeRefreshTokenAsync(string tokenHash, Guid? actorUserId, Guid correlationId, CancellationToken cancellationToken);
}
