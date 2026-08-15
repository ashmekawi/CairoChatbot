using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Chatbot.Api.Identity;
using Chatbot.Core.Errors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ApiUser = Chatbot.Api.Identity.IdentityUser;

if (args.Contains("authorization", StringComparer.OrdinalIgnoreCase))
{
    await AuthorizationTests.RunAsync();
}
else
{
    await IdentityTests.RunAsync();
}

internal static class AuthorizationTests
{
    public static async Task RunAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization(UserPermissions.AddPolicies);
        await using var provider = services.BuildServiceProvider();
        var authorization = provider.GetRequiredService<IAuthorizationService>();

        var withoutPermission = new ClaimsPrincipal(
            new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())], "Test"));
        var denied = await authorization.AuthorizeAsync(withoutPermission, null, UserPermissions.Read);
        var deniedStatus = denied.Succeeded ? 200 : 403;
        Assert(deniedStatus == 403, "Authenticated user without permission was not forbidden.");

        var withPermission = new ClaimsPrincipal(
            new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                new Claim("permission", UserPermissions.Read)
            ],
            "Test"));
        var allowed = await authorization.AuthorizeAsync(withPermission, null, UserPermissions.Read);
        Assert(allowed.Succeeded, "User with the required permission was not allowed.");

        Console.WriteLine("V0002 permission authorization tests passed.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}

internal static class IdentityTests
{
    private static readonly Guid CorrelationId = Guid.NewGuid();
    private static readonly PasswordHasher<ApiUser> PasswordHasher = new();

    public static async Task RunAsync()
    {
        await LoginSuccessAndPermissionLoadingAsync();
        await WrongPasswordAndLockoutAsync();
        await InactiveUserAsync();
        await RefreshRotationAndLogoutAsync();
        await UserCreateAndRoleChangesAsync();
        await SafeAuthenticationErrorAsync();
        Console.WriteLine("V0002 identity tests passed.");
    }

    private static async Task LoginSuccessAndPermissionLoadingAsync()
    {
        var store = CreateStore();
        store.Permissions.Add("USER.READ");
        var service = CreateAuthService(store);

        var tokens = await service.LoginAsync("admin", "correct-password", CorrelationId, default);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(tokens.AccessToken);

        Assert(!string.IsNullOrWhiteSpace(tokens.RefreshToken), "Refresh token was not created.");
        Assert(store.LoginSuccessCount == 1, "Login success was not recorded.");
        Assert(store.StoredRefreshHashes.Count == 1, "Refresh token hash was not stored.");
        Assert(!store.StoredRefreshHashes.Contains(tokens.RefreshToken), "Raw refresh token was stored.");
        Assert(jwt.Claims.Any(claim => claim.Type == "permission" && claim.Value == "USER.READ"), "Permission was not loaded into access token.");
    }

    private static async Task WrongPasswordAndLockoutAsync()
    {
        var store = CreateStore();
        var service = CreateAuthService(store, maximumAttempts: 2);

        await ExpectAppErrorAsync(() => service.LoginAsync("admin", "wrong", CorrelationId, default), "authentication_failed");
        await ExpectAppErrorAsync(() => service.LoginAsync("admin", "wrong", CorrelationId, default), "authentication_failed");
        await ExpectAppErrorAsync(() => service.LoginAsync("admin", "correct-password", CorrelationId, default), "user_locked");

        Assert(store.LoginFailureCount == 2, "Failed logins were not recorded.");
        Assert(store.User?.LockedUntilUtc > DateTime.UtcNow, "User was not locked after the configured attempts.");
    }

    private static async Task InactiveUserAsync()
    {
        var store = CreateStore();
        store.User = store.User! with { IsActive = false };
        await ExpectAppErrorAsync(
            () => CreateAuthService(store).LoginAsync("admin", "correct-password", CorrelationId, default),
            "user_inactive");
    }

    private static async Task RefreshRotationAndLogoutAsync()
    {
        var store = CreateStore();
        var service = CreateAuthService(store);
        var first = await service.LoginAsync("admin", "correct-password", CorrelationId, default);
        var second = await service.RefreshAsync(first.RefreshToken, CorrelationId, default);

        Assert(store.RotationCount == 1, "Refresh token was not rotated.");
        Assert(first.RefreshToken != second.RefreshToken, "Refresh returned the same raw token.");
        await ExpectAppErrorAsync(
            () => service.RefreshAsync(first.RefreshToken, CorrelationId, default),
            "authentication_failed");

        await service.LogoutAsync(second.RefreshToken, store.User!.PublicId, CorrelationId, default);
        Assert(store.RevokeCount == 1, "Logout did not revoke the refresh token.");
    }

    private static async Task UserCreateAndRoleChangesAsync()
    {
        var store = new FakeIdentityStore();
        var service = new UserAdminService(store, PasswordHasher);
        var actorId = Guid.NewGuid();
        var publicId = await service.CreateAsync(
            "new.user",
            "New User",
            "strong-password",
            true,
            actorId,
            CorrelationId,
            default);

        Assert(store.User?.PublicId == publicId, "User was not created.");
        Assert(store.User?.PasswordHash != "strong-password", "Password was not hashed.");
        await service.AssignRoleAsync(publicId, "ADMIN", actorId, CorrelationId, default);
        Assert(store.Roles.Contains("ADMIN"), "Role was not assigned.");
        await service.RemoveRoleAsync(publicId, "ADMIN", actorId, CorrelationId, default);
        Assert(!store.Roles.Contains("ADMIN"), "Role was not removed.");
    }

    private static async Task SafeAuthenticationErrorAsync()
    {
        var error = await CaptureAppErrorAsync(
            () => CreateAuthService(new FakeIdentityStore()).LoginAsync("missing", "password", CorrelationId, default));
        Assert(error.Message == "Invalid credentials.", "Authentication error exposed internal details.");
        Assert(error.StatusCode == 401, "Authentication error status is not safe.");
    }

    private static FakeIdentityStore CreateStore()
    {
        var store = new FakeIdentityStore();
        var user = new ApiUser(1, Guid.NewGuid(), "admin", "Administrator", null, true, 0, null);
        store.User = user with { PasswordHash = PasswordHasher.HashPassword(user, "correct-password") };
        return store;
    }

    private static AuthService CreateAuthService(FakeIdentityStore store, int maximumAttempts = 5)
    {
        var jwtOptions = Options.Create(new JwtOptions
        {
            Issuer = "CairoChatbot.Tests",
            Audience = "CairoChatbot.Api.Tests",
            SigningKey = "test-only-signing-key-with-at-least-32-characters",
            AccessTokenMinutes = 15,
            RefreshTokenDays = 30
        });
        return new AuthService(
            store,
            PasswordHasher,
            new TokenService(jwtOptions),
            Options.Create(new Chatbot.Api.Identity.LockoutOptions
            {
                MaximumFailedAttempts = maximumAttempts,
                LockoutMinutes = 15
            }));
    }

    private static async Task ExpectAppErrorAsync(Func<Task> action, string errorType)
    {
        var error = await CaptureAppErrorAsync(action);
        Assert(error.ErrorType == errorType, $"Expected {errorType}, received {error.ErrorType}.");
    }

    private static async Task<AppException> CaptureAppErrorAsync(Func<Task> action)
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

    private sealed class FakeIdentityStore : IIdentityStore
    {
        public ApiUser? User { get; set; }
        public HashSet<string> Permissions { get; } = [];
        public HashSet<string> Roles { get; } = [];
        public HashSet<string> StoredRefreshHashes { get; } = [];
        public int LoginSuccessCount { get; private set; }
        public int LoginFailureCount { get; private set; }
        public int RotationCount { get; private set; }
        public int RevokeCount { get; private set; }
        public int AuditedLoginFailureCount { get; private set; }

        public Task<ApiUser?> GetByUsernameAsync(string username, Guid correlationId, CancellationToken cancellationToken)
        {
            return Task.FromResult(User?.Username == username ? User : null);
        }

        public Task<ApiUser?> GetByPublicIdAsync(Guid publicId, Guid correlationId, CancellationToken cancellationToken)
        {
            return Task.FromResult(User?.PublicId == publicId ? User : null);
        }

        public Task CreateUserAsync(ApiUser user, Guid? actorUserId, Guid correlationId, CancellationToken cancellationToken)
        {
            User = user with { UserId = 1 };
            return Task.CompletedTask;
        }

        public Task SetActiveAsync(Guid publicId, bool isActive, Guid actorUserId, Guid correlationId, CancellationToken cancellationToken)
        {
            User = User! with { IsActive = isActive };
            return Task.CompletedTask;
        }

        public Task SetPasswordAsync(Guid publicId, string passwordHash, Guid actorUserId, Guid correlationId, CancellationToken cancellationToken)
        {
            User = User! with { PasswordHash = passwordHash };
            return Task.CompletedTask;
        }

        public Task RecordLoginSuccessAsync(long userId, Guid correlationId, CancellationToken cancellationToken)
        {
            LoginSuccessCount++;
            User = User! with { FailedLoginCount = 0, LockedUntilUtc = null };
            return Task.CompletedTask;
        }

        public Task RecordLoginFailureAsync(long userId, int maximumAttempts, int lockoutMinutes, Guid correlationId, CancellationToken cancellationToken)
        {
            LoginFailureCount++;
            var failedCount = User!.FailedLoginCount + 1;
            User = User with
            {
                FailedLoginCount = failedCount,
                LockedUntilUtc = failedCount >= maximumAttempts ? DateTime.UtcNow.AddMinutes(lockoutMinutes) : null
            };
            return Task.CompletedTask;
        }

        public Task AuditLoginFailureAsync(string username, Guid correlationId, CancellationToken cancellationToken)
        {
            AuditedLoginFailureCount++;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<string>> GetPermissionsAsync(long userId, Guid correlationId, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<string>>(Permissions.ToList());
        }

        public Task AssignRoleAsync(Guid publicId, string roleCode, Guid actorUserId, Guid correlationId, CancellationToken cancellationToken)
        {
            Roles.Add(roleCode);
            return Task.CompletedTask;
        }

        public Task RemoveRoleAsync(Guid publicId, string roleCode, Guid actorUserId, Guid correlationId, CancellationToken cancellationToken)
        {
            Roles.Remove(roleCode);
            return Task.CompletedTask;
        }

        public Task CreateRefreshTokenAsync(long userId, string tokenHash, DateTime expiresAtUtc, Guid correlationId, CancellationToken cancellationToken)
        {
            StoredRefreshHashes.Add(tokenHash);
            return Task.CompletedTask;
        }

        public Task<ValidRefreshToken?> GetValidRefreshTokenAsync(string tokenHash, Guid correlationId, CancellationToken cancellationToken)
        {
            if (User is null || !StoredRefreshHashes.Contains(tokenHash))
            {
                return Task.FromResult<ValidRefreshToken?>(null);
            }
            return Task.FromResult<ValidRefreshToken?>(new ValidRefreshToken(
                1,
                User.UserId,
                User.PublicId,
                User.Username,
                User.DisplayName,
                User.IsActive));
        }

        public Task RotateRefreshTokenAsync(string currentHash, string newHash, DateTime expiresAtUtc, Guid correlationId, CancellationToken cancellationToken)
        {
            if (!StoredRefreshHashes.Remove(currentHash))
            {
                throw new InvalidOperationException("Current refresh token is invalid.");
            }
            StoredRefreshHashes.Add(newHash);
            RotationCount++;
            return Task.CompletedTask;
        }

        public Task RevokeRefreshTokenAsync(string tokenHash, Guid? actorUserId, Guid correlationId, CancellationToken cancellationToken)
        {
            StoredRefreshHashes.Remove(tokenHash);
            RevokeCount++;
            return Task.CompletedTask;
        }
    }
}
