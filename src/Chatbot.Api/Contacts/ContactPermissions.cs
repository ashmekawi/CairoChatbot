using Microsoft.AspNetCore.Authorization;

namespace Chatbot.Api.Contacts;

public static class ContactPermissions
{
    public const string Read = "contacts.read";
    public const string Manage = "contacts.manage";

    public static void AddPolicies(AuthorizationOptions options)
    {
        options.AddPolicy(Read, policy => policy.RequireClaim("permission", Read));
        options.AddPolicy(Manage, policy => policy.RequireClaim("permission", Manage));
    }
}
