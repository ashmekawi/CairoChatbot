using Chatbot.Core.Errors;

namespace Chatbot.Api.Contacts;

public sealed class WhatsAppAddressNormalizer
{
    public string Normalize(string address)
    {
        var digits = new string(address.Where(character => character is >= '0' and <= '9').ToArray());
        if (digits.StartsWith("00", StringComparison.Ordinal))
        {
            digits = digits[2..];
        }
        if (string.IsNullOrWhiteSpace(digits))
        {
            throw new AppException("Address is invalid.", 400, "invalid_channel_address");
        }
        return digits;
    }
}
