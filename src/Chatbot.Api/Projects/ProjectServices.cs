using System.Text.Json;
using Chatbot.Core.Errors;

namespace Chatbot.Api.Projects;

public sealed class ProjectService(IProjectStore store)
{
    public async Task<Guid> CreateAsync(ProjectRecord input, Guid? actorId, Guid correlationId, CancellationToken cancellationToken)
    {
        var project = input with { PublicId = Guid.NewGuid() };
        await store.CreateAsync(project, actorId, correlationId, cancellationToken);
        return project.PublicId;
    }

    public async Task<ProjectRecord> GetAsync(Guid publicId, Guid correlationId, CancellationToken cancellationToken)
    {
        return await store.GetAsync(publicId, correlationId, cancellationToken)
            ?? throw new AppException("Project was not found.", 404, "project_not_found");
    }

    public Task UpdateAsync(ProjectRecord project, Guid actorId, Guid correlationId, CancellationToken cancellationToken)
    {
        return store.UpdateAsync(project, actorId, correlationId, cancellationToken);
    }

    public Task SetActiveAsync(Guid publicId, bool isActive, Guid actorId, Guid correlationId, CancellationToken cancellationToken)
    {
        return store.SetActiveAsync(publicId, isActive, actorId, correlationId, cancellationToken);
    }
}

public sealed class ChannelService(IChannelStore store)
{
    private static readonly string[] ForbiddenConfigurationNames =
    [
        "secret",
        "password",
        "apikey",
        "api_key",
        "token"
    ];

    public async Task<Guid> CreateAsync(ChannelRecord input, Guid? actorId, Guid correlationId, CancellationToken cancellationToken)
    {
        ValidateConfiguration(input.ConfigurationJson);
        var channel = input with { PublicId = Guid.NewGuid() };
        await store.CreateAsync(channel, actorId, correlationId, cancellationToken);
        return channel.PublicId;
    }

    public async Task<ChannelRecord> GetAsync(Guid publicId, Guid correlationId, CancellationToken cancellationToken)
    {
        return await store.GetAsync(publicId, correlationId, cancellationToken)
            ?? throw new AppException("Channel was not found.", 404, "channel_not_found");
    }

    public Task UpdateAsync(ChannelRecord channel, Guid actorId, Guid correlationId, CancellationToken cancellationToken)
    {
        ValidateConfiguration(channel.ConfigurationJson);
        return store.UpdateAsync(channel, actorId, correlationId, cancellationToken);
    }

    public Task SetActiveAsync(Guid publicId, bool isActive, Guid actorId, Guid correlationId, CancellationToken cancellationToken)
    {
        return store.SetActiveAsync(publicId, isActive, actorId, correlationId, cancellationToken);
    }

    private static void ValidateConfiguration(string? configurationJson)
    {
        if (configurationJson is null)
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(configurationJson);
            RejectSecrets(document.RootElement);
        }
        catch (JsonException exception)
        {
            throw new AppException("ConfigurationJson is invalid.", 400, "invalid_configuration_json", exception);
        }
    }

    private static void RejectSecrets(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (ForbiddenConfigurationNames.Contains(property.Name, StringComparer.OrdinalIgnoreCase))
                {
                    throw new AppException("Secrets are not allowed in ConfigurationJson.", 400, "configuration_secret_forbidden");
                }
                RejectSecrets(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                RejectSecrets(item);
            }
        }
    }
}

public sealed class BusinessHoursService(IBusinessHoursStore store)
{
    public Task<IReadOnlyList<BusinessHourRecord>> GetAsync(Guid projectId, Guid correlationId, CancellationToken cancellationToken)
    {
        return store.GetAsync(projectId, correlationId, cancellationToken);
    }

    public async Task UpsertAsync(
        Guid projectId,
        IReadOnlyCollection<BusinessHourRecord> hours,
        Guid actorId,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        if (hours.Select(item => item.DayOfWeek).Distinct().Count() != hours.Count)
        {
            throw new AppException("Business hours contain duplicate days.", 400, "invalid_business_hours");
        }
        foreach (var item in hours)
        {
            Validate(item);
            await store.UpsertAsync(projectId, item, actorId, correlationId, cancellationToken);
        }
    }

    private static void Validate(BusinessHourRecord hours)
    {
        var validWorkingDay = hours.IsWorkingDay
            && hours.StartTime is not null
            && hours.EndTime is not null
            && hours.EndTime > hours.StartTime;
        var validNonWorkingDay = !hours.IsWorkingDay
            && hours.StartTime is null
            && hours.EndTime is null;
        if (hours.DayOfWeek > 6 || (!validWorkingDay && !validNonWorkingDay))
        {
            throw new AppException("Business hours are invalid.", 400, "invalid_business_hours");
        }
    }
}
