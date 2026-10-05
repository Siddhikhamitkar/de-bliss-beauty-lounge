namespace DeBliss.Application.Notifications;

/// <summary>
/// E.164 normalisation for customer phone numbers. WhatsApp can only deliver
/// to E.164 numbers, so anything that cannot be normalised is treated as
/// unsendable rather than guessed at.
/// </summary>
public static class PhoneNumbers
{
    /// <summary>
    /// Normalises to E.164 ("+" followed by 8–15 digits), accepting spaces,
    /// dashes, dots, brackets and a leading "00" international prefix.
    /// Returns null when the input cannot be a valid E.164 number.
    /// </summary>
    public static string? TryNormalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var candidate = raw.Trim();

        if (candidate.StartsWith("00", StringComparison.Ordinal))
        {
            candidate = "+" + candidate[2..];
        }

        if (candidate.Length == 0 || candidate[0] != '+')
        {
            return null;
        }

        var digits = candidate[1..]
            .Replace(" ", string.Empty)
            .Replace("-", string.Empty)
            .Replace(".", string.Empty)
            .Replace("(", string.Empty)
            .Replace(")", string.Empty);

        if (digits.Length is < 8 or > 15 || digits[0] == '0' || !digits.All(char.IsAsciiDigit))
        {
            return null;
        }

        return "+" + digits;
    }

    /// <summary>Masked form for logs: keeps the leading prefix and the last two digits.</summary>
    public static string Mask(string e164)
    {
        if (e164.Length <= 4)
        {
            return "****";
        }

        return string.Concat(e164.AsSpan(0, 2), new string('*', e164.Length - 4), e164.AsSpan(e164.Length - 2));
    }
}
