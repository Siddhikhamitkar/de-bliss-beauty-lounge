namespace DeBliss.Domain.Customers;

/// <summary>
/// Canonical form of a customer's mobile number: E.164 (<c>+</c> followed by
/// 8–15 digits), with the salon's home market — the UAE (+971) — understood in
/// every way front-desk staff and customers actually type it.
/// </summary>
/// <remarks>
/// The canonical form is the customer identity key: one person, one number,
/// one record. <c>050 123 4567</c>, <c>+971 50 123 4567</c>, <c>00971501234567</c>
/// and <c>501234567</c> all become <c>+971501234567</c>.
/// </remarks>
public static class MobileNumber
{
    public const string UaeCountryCode = "971";

    public const int MaxLength = 16;

    /// <summary>
    /// The E.164 form of <paramref name="raw"/>, or null when it cannot be read
    /// as a phone number (too short, too long, or nothing but formatting).
    /// </summary>
    public static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var trimmed = raw.Trim();
        var international = trimmed.StartsWith('+');
        var digits = string.Concat(trimmed.Where(char.IsAsciiDigit));

        if (digits.Length == 0)
        {
            return null;
        }

        if (!international && digits.StartsWith("00", StringComparison.Ordinal))
        {
            // 00971… is the international dialling prefix written out.
            digits = digits[2..];
            international = true;
        }

        if (!international)
        {
            if (digits.Length == 10 && digits.StartsWith("05", StringComparison.Ordinal))
            {
                // UAE trunk form: 05X XXX XXXX.
                digits = UaeCountryCode + digits[1..];
            }
            else if (digits.Length == 9 && digits[0] == '5')
            {
                // UAE mobile without the trunk zero: 5X XXX XXXX.
                digits = UaeCountryCode + digits;
            }
        }

        // A UAE number written with the country code and the trunk zero
        // (+971 050…) is common enough to accept.
        if (digits.StartsWith(UaeCountryCode + "0", StringComparison.Ordinal))
        {
            digits = UaeCountryCode + digits[(UaeCountryCode.Length + 1)..];
        }

        if (digits.Length is < 8 or > 15 || digits[0] == '0')
        {
            return null;
        }

        if (digits.StartsWith(UaeCountryCode, StringComparison.Ordinal) && digits.Length is not (11 or 12))
        {
            // +971 is followed by 8 (landline) or 9 (mobile) digits.
            return null;
        }

        return "+" + digits;
    }
}
