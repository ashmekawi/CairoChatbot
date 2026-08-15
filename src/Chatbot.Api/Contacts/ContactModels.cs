namespace Chatbot.Api.Contacts;

public sealed record ContactRecord(
    long ContactId,
    Guid PublicId,
    string? DisplayName,
    string? PreferredLanguage,
    bool IsActive);

public sealed record ChannelIdentityRecord(
    long ChannelIdentityId,
    Guid PublicId,
    Guid ContactPublicId,
    Guid ChannelPublicId,
    string ExternalId,
    string NormalizedAddress,
    string? DisplayAddress,
    bool IsVerified,
    DateTime? VerifiedAtUtc,
    bool IsActive,
    DateTime? LastSeenAtUtc);
