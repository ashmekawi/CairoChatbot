namespace Chatbot.Api.Projects;

public sealed record ProjectRecord(
    long ProjectId,
    Guid PublicId,
    string Code,
    string NameAr,
    string? NameEn,
    string DefaultLanguage,
    string TimeZoneId,
    bool IsActive);

public sealed record ChannelRecord(
    long ChannelId,
    Guid PublicId,
    Guid ProjectPublicId,
    string Code,
    string ChannelType,
    string ProviderCode,
    bool IsActive,
    string? ConfigurationJson);

public sealed record BusinessHourRecord(
    byte DayOfWeek,
    bool IsWorkingDay,
    TimeSpan? StartTime,
    TimeSpan? EndTime);
