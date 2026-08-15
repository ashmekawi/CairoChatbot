using Microsoft.AspNetCore.Authorization;

namespace Chatbot.Api.Identity;

public static class UserPermissions
{
    public const string Read = "users.read";
    public const string Create = "users.create";
    public const string Activate = "users.activate";
    public const string ResetPassword = "users.password.reset";
    public const string ManageRoles = "users.roles.manage";

    public static void AddPolicies(AuthorizationOptions options)
    {
        options.AddPolicy(Read, policy => policy.RequireClaim("permission", Read));
        options.AddPolicy(Create, policy => policy.RequireClaim("permission", Create));
        options.AddPolicy(Activate, policy => policy.RequireClaim("permission", Activate));
        options.AddPolicy(ResetPassword, policy => policy.RequireClaim("permission", ResetPassword));
        options.AddPolicy(ManageRoles, policy => policy.RequireClaim("permission", ManageRoles));
    }
}
