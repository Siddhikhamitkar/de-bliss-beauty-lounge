namespace DeBliss.Application.Customers;

/// <summary>
/// One way of writing a phone number down. The salon takes numbers exactly as
/// the customer gives them, so two records can hold the same number with
/// different formatting. Both sides of a comparison reduce to digits only:
/// the request side here, the column side through a matching strip in the
/// repository's query.
/// </summary>
public static class CustomerPhone
{
    /// <summary>Digits only, or null when the value has no digits at all.</summary>
    public static string? Normalize(string? phoneNumber)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            return null;
        }

        var digits = string.Concat(phoneNumber.Where(char.IsAsciiDigit));

        return digits.Length == 0 ? null : digits;
    }
}
