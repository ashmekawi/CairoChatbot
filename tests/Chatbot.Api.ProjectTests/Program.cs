using System.Security.Claims;
using Chatbot.Api.Projects;
using Chatbot.Core.Errors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

await ProjectTests.RunAsync();

internal static class ProjectTests
{
    private static readonly Guid CorrelationId = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();

    public static async Task RunAsync()
    {
        await ProjectLifecycleAsync();
        await ChannelLifecycleAsync();
        await InvalidConfigurationIsRejectedAsync();
        await BusinessHoursValidationAndPersistenceAsync();
        await PermissionAuthorizationAsync();
        await SafeErrorHandlingAsync();
        Console.WriteLine("V0003 projects and channels tests passed.");
    }

    private static async Task ProjectLifecycleAsync()
    {
        var store = new FakeStore();
        var service = new ProjectService(store);
        var input = Project(Guid.Empty, "CAIRO");
        var publicId = await service.CreateAsync(input, ActorId, CorrelationId, default);

        Assert(publicId != Guid.Empty, "Project PublicId was not created.");
        Assert((await service.GetAsync(publicId, CorrelationId, default)).Code == "CAIRO", "Project was not read.");
        await service.UpdateAsync(Project(publicId, "CAIRO-UPDATED"), ActorId, CorrelationId, default);
        Assert(store.Project?.Code == "CAIRO-UPDATED", "Project was not updated.");
        await service.SetActiveAsync(publicId, false, ActorId, CorrelationId, default);
        Assert(store.Project?.IsActive == false, "Project active state was not updated.");
    }

    private static async Task ChannelLifecycleAsync()
    {
        var store = new FakeStore();
        var projects = new ProjectService(store);
        var projectId = await projects.CreateAsync(Project(Guid.Empty, "CAIRO"), ActorId, CorrelationId, default);
        var service = new ChannelService(store);
        var channelId = await service.CreateAsync(Channel(Guid.Empty, projectId, "PRIMARY", "{}"), ActorId, CorrelationId, default);

        Assert((await service.GetAsync(channelId, CorrelationId, default)).ProjectPublicId == projectId, "Channel project scope was lost.");
        await service.UpdateAsync(Channel(channelId, projectId, "UPDATED", "{\"label\":\"main\"}"), ActorId, CorrelationId, default);
        Assert(store.Channel?.Code == "UPDATED", "Channel was not updated.");
        await service.SetActiveAsync(channelId, false, ActorId, CorrelationId, default);
        Assert(store.Channel?.IsActive == false, "Channel active state was not updated.");

        await ExpectErrorAsync(
            () => new ChannelService(new FakeStore()).CreateAsync(Channel(Guid.Empty, Guid.NewGuid(), "ORPHAN", null), ActorId, CorrelationId, default),
            "project_not_found");
    }

    private static async Task InvalidConfigurationIsRejectedAsync()
    {
        var service = new ChannelService(new FakeStore());
        await ExpectErrorAsync(
            () => service.CreateAsync(Channel(Guid.Empty, Guid.NewGuid(), "INVALID", "{"), ActorId, CorrelationId, default),
            "invalid_configuration_json");
        await ExpectErrorAsync(
            () => service.CreateAsync(Channel(Guid.Empty, Guid.NewGuid(), "SECRET", "{\"apiKey\":\"value\"}"), ActorId, CorrelationId, default),
            "configuration_secret_forbidden");
    }

    private static async Task BusinessHoursValidationAndPersistenceAsync()
    {
        var store = new FakeStore();
        var service = new BusinessHoursService(store);
        var projectId = Guid.NewGuid();
        var valid = new BusinessHourRecord(1, true, TimeSpan.FromHours(9), TimeSpan.FromHours(17));
        await service.UpsertAsync(projectId, [valid], ActorId, CorrelationId, default);
        var saved = await service.GetAsync(projectId, CorrelationId, default);
        Assert(saved.Count == 1 && saved[0] == valid, "Business hours upsert/get failed.");

        var invalid = new BusinessHourRecord(7, true, TimeSpan.FromHours(17), TimeSpan.FromHours(9));
        await ExpectErrorAsync(
            () => service.UpsertAsync(projectId, [invalid], ActorId, CorrelationId, default),
            "invalid_business_hours");
    }

    private static async Task PermissionAuthorizationAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization(ProjectPermissions.AddPolicies);
        await using var provider = services.BuildServiceProvider();
        var authorization = provider.GetRequiredService<IAuthorizationService>();
        var deniedUser = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "user")], "Test"));
        var denied = await authorization.AuthorizeAsync(deniedUser, null, ProjectPermissions.ReadProjects);
        Assert((denied.Succeeded ? 200 : 403) == 403, "User without permission was not forbidden.");

        var allowedUser = new ClaimsPrincipal(
            new ClaimsIdentity([new Claim("permission", ProjectPermissions.ReadProjects)], "Test"));
        var allowed = await authorization.AuthorizeAsync(allowedUser, null, ProjectPermissions.ReadProjects);
        Assert(allowed.Succeeded, "User with permission was not allowed.");
    }

    private static async Task SafeErrorHandlingAsync()
    {
        var error = await CaptureErrorAsync(
            () => new ProjectService(new FakeStore()).GetAsync(Guid.NewGuid(), CorrelationId, default));
        Assert(error.StatusCode == 404 && error.ErrorType == "project_not_found", "Missing project error was not safe.");
    }

    private static ProjectRecord Project(Guid publicId, string code)
    {
        return new ProjectRecord(0, publicId, code, "مشروع", "Project", "ar", "Africa/Cairo", true);
    }

    private static ChannelRecord Channel(Guid publicId, Guid projectId, string code, string? configuration)
    {
        return new ChannelRecord(0, publicId, projectId, code, "WHATSAPP", "WAHA", true, configuration);
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

    private sealed class FakeStore : IProjectStore, IChannelStore, IBusinessHoursStore
    {
        public ProjectRecord? Project { get; private set; }
        public ChannelRecord? Channel { get; private set; }
        private readonly Dictionary<byte, BusinessHourRecord> _hours = [];

        public Task CreateAsync(ProjectRecord project, Guid? actorId, Guid correlationId, CancellationToken cancellationToken)
        {
            Project = project with { ProjectId = 1 };
            return Task.CompletedTask;
        }

        public Task<ProjectRecord?> GetAsync(Guid publicId, Guid correlationId, CancellationToken cancellationToken)
        {
            return Task.FromResult(Project?.PublicId == publicId ? Project : null);
        }

        public Task UpdateAsync(ProjectRecord project, Guid actorId, Guid correlationId, CancellationToken cancellationToken)
        {
            Project = project with { ProjectId = Project?.ProjectId ?? 1 };
            return Task.CompletedTask;
        }

        Task IProjectStore.SetActiveAsync(Guid publicId, bool isActive, Guid actorId, Guid correlationId, CancellationToken cancellationToken)
        {
            Project = Project! with { IsActive = isActive };
            return Task.CompletedTask;
        }

        Task IChannelStore.CreateAsync(ChannelRecord channel, Guid? actorId, Guid correlationId, CancellationToken cancellationToken)
        {
            if (Project?.PublicId != channel.ProjectPublicId)
            {
                throw new AppException("Project was not found.", 404, "project_not_found");
            }
            Channel = channel with { ChannelId = 1 };
            return Task.CompletedTask;
        }

        Task<ChannelRecord?> IChannelStore.GetAsync(Guid publicId, Guid correlationId, CancellationToken cancellationToken)
        {
            return Task.FromResult(Channel?.PublicId == publicId ? Channel : null);
        }

        Task IChannelStore.UpdateAsync(ChannelRecord channel, Guid actorId, Guid correlationId, CancellationToken cancellationToken)
        {
            Channel = channel with { ChannelId = Channel?.ChannelId ?? 1 };
            return Task.CompletedTask;
        }

        Task IChannelStore.SetActiveAsync(Guid publicId, bool isActive, Guid actorId, Guid correlationId, CancellationToken cancellationToken)
        {
            Channel = Channel! with { IsActive = isActive };
            return Task.CompletedTask;
        }

        Task<IReadOnlyList<BusinessHourRecord>> IBusinessHoursStore.GetAsync(Guid projectPublicId, Guid correlationId, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<BusinessHourRecord>>(_hours.Values.OrderBy(item => item.DayOfWeek).ToList());
        }

        Task IBusinessHoursStore.UpsertAsync(Guid projectPublicId, BusinessHourRecord hours, Guid actorId, Guid correlationId, CancellationToken cancellationToken)
        {
            _hours[hours.DayOfWeek] = hours;
            return Task.CompletedTask;
        }
    }
}
