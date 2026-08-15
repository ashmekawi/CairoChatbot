namespace Chatbot.Api.Identity;

public sealed record IdentityUser(
    long UserId,
    Guid PublicId,
    string Username,
    string DisplayName,
    string? PasswordHash,
    bool IsActive,
    int FailedLoginCount,
    DateTime? LockedUntilUtc);

public sealed record ValidRefreshToken(
    long RefreshTokenId,
    long UserId,
    Guid PublicId,
    string Username,
    string DisplayName,
    bool IsActive);

public sealed record TokenPair(string AccessToken, string RefreshToken, DateTime ExpiresAtUtc);

public sealed class JwtOptions
{
    public string Issuer { get; init; } = string.Empty;
    public string Audience { get; init; } = string.Empty;
    public string SigningKey { get; init; } = string.Empty;
    public int AccessTokenMinutes { get; init; } = 15;
    public int RefreshTokenDays { get; init; } = 30;
}

public sealed class LockoutOptions
{
    public int MaximumFailedAttempts { get; init; } = 5;
    public int LockoutMinutes { get; init; } = 15;
}
