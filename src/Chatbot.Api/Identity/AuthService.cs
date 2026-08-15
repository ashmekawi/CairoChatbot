using Chatbot.Core.Errors;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Chatbot.Api.Identity;

public sealed class AuthService(
    IIdentityStore store,
    PasswordHasher<IdentityUser> passwordHasher,
    TokenService tokenService,
    IOptions<LockoutOptions> lockoutOptions)
{
    private readonly LockoutOptions _lockout = lockoutOptions.Value;

    public async Task<TokenPair> LoginAsync(string username, string password, Guid correlationId, CancellationToken cancellationToken)
    {
        var user = await store.GetByUsernameAsync(username.Trim(), correlationId, cancellationToken);
        if (user is null || user.PasswordHash is null)
        {
            await store.AuditLoginFailureAsync(username.Trim(), correlationId, cancellationToken);
            throw AuthenticationFailed();
        }
        if (!user.IsActive)
        {
            await store.AuditLoginFailureAsync(user.Username, correlationId, cancellationToken);
            throw new AppException("User is inactive.", 403, "user_inactive");
        }
        if (user.LockedUntilUtc > DateTime.UtcNow)
        {
            await store.AuditLoginFailureAsync(user.Username, correlationId, cancellationToken);
            throw new AppException("User is temporarily locked.", 423, "user_locked");
        }

        var verification = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (verification == PasswordVerificationResult.Failed)
        {
            await store.RecordLoginFailureAsync(
                user.UserId,
                _lockout.MaximumFailedAttempts,
                _lockout.LockoutMinutes,
                correlationId,
                cancellationToken);
            throw AuthenticationFailed();
        }

        await store.RecordLoginSuccessAsync(user.UserId, correlationId, cancellationToken);
        return await IssueInitialTokensAsync(user, correlationId, cancellationToken);
    }

    public async Task<TokenPair> RefreshAsync(string rawToken, Guid correlationId, CancellationToken cancellationToken)
    {
        var currentHash = tokenService.HashRefreshToken(rawToken);
        var validToken = await store.GetValidRefreshTokenAsync(currentHash, correlationId, cancellationToken)
            ?? throw AuthenticationFailed();
        if (!validToken.IsActive)
        {
            throw new AppException("User is inactive.", 403, "user_inactive");
        }

        var user = new IdentityUser(
            validToken.UserId,
            validToken.PublicId,
            validToken.Username,
            validToken.DisplayName,
            null,
            true,
            0,
            null);
        var newRawToken = tokenService.CreateRefreshToken();
        var expiresAtUtc = tokenService.GetRefreshExpiryUtc();
        await store.RotateRefreshTokenAsync(
            currentHash,
            tokenService.HashRefreshToken(newRawToken),
            expiresAtUtc,
            correlationId,
            cancellationToken);
        var permissions = await store.GetPermissionsAsync(user.UserId, correlationId, cancellationToken);
        return new TokenPair(tokenService.CreateAccessToken(user, permissions), newRawToken, expiresAtUtc);
    }

    public Task LogoutAsync(string rawToken, Guid? actorUserId, Guid correlationId, CancellationToken cancellationToken)
    {
        return store.RevokeRefreshTokenAsync(
            tokenService.HashRefreshToken(rawToken),
            actorUserId,
            correlationId,
            cancellationToken);
    }

    private async Task<TokenPair> IssueInitialTokensAsync(IdentityUser user, Guid correlationId, CancellationToken cancellationToken)
    {
        var permissions = await store.GetPermissionsAsync(user.UserId, correlationId, cancellationToken);
        var refreshToken = tokenService.CreateRefreshToken();
        var expiresAtUtc = tokenService.GetRefreshExpiryUtc();
        await store.CreateRefreshTokenAsync(
            user.UserId,
            tokenService.HashRefreshToken(refreshToken),
            expiresAtUtc,
            correlationId,
            cancellationToken);
        return new TokenPair(tokenService.CreateAccessToken(user, permissions), refreshToken, expiresAtUtc);
    }

    private static AppException AuthenticationFailed()
    {
        return new AppException("Invalid credentials.", 401, "authentication_failed");
    }
}
