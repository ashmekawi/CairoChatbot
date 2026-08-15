namespace Chatbot.Api.Logging;

public static class ErrorReferences
{
    public static string Create()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        return $"ERR-{DateTime.UtcNow:yyyyMMdd}-{suffix}";
    }
}
