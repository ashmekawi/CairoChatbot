namespace Chatbot.Core.Errors;

public class AppException : Exception
{
    public int StatusCode { get; }
    public string ErrorType { get; }
    public string? ErrorReference { get; init; }

    public AppException(string message, int statusCode = 400, string errorType = "application_error", Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
        ErrorType = errorType;
    }
}
