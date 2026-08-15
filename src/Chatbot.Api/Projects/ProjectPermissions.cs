using Microsoft.AspNetCore.Authorization;

namespace Chatbot.Api.Projects;

public static class ProjectPermissions
{
    public const string ReadProjects = "projects.read";
    public const string ManageProjects = "projects.manage";
    public const string ReadChannels = "channels.read";
    public const string ManageChannels = "channels.manage";

    public static void AddPolicies(AuthorizationOptions options)
    {
        options.AddPolicy(ReadProjects, policy => policy.RequireClaim("permission", ReadProjects));
        options.AddPolicy(ManageProjects, policy => policy.RequireClaim("permission", ManageProjects));
        options.AddPolicy(ReadChannels, policy => policy.RequireClaim("permission", ReadChannels));
        options.AddPolicy(ManageChannels, policy => policy.RequireClaim("permission", ManageChannels));
    }
}
