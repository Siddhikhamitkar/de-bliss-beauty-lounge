using System.Globalization;
using DeBliss.Application.Imports.Dtos;

namespace DeBliss.Application.Imports;

/// <summary>
/// Cell-level readers shared by every import flavour. These are where "do not
/// silently change meaning" is enforced: a price may keep thousand separators
/// only when they unambiguously are thousands, a date must be unambiguous, and
/// a formula cell is never evaluated — it becomes a field error instead.
/// </summary>
public static class ImportValues
{
    public static bool IsBlank(ImportCell cell) =>
        cell.Text is null && cell.Number is null && cell.Date is null && !cell.HasFormula;

    /// <summary>The trimmed text of a text cell, or null when it holds nothing.</summary>
    public static string? Text(ImportCell cell)
    {
        var trimmed = cell.Text?.Trim();

        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    /// <summary>What previews and error reports show for a cell, whatever kind of value it held.</summary>
    public static string Display(ImportCell cell) => cell switch
    {
        _ when cell.Text is { } text => text.Trim(),
        _ when cell.Number is { } number => number.ToString("0.######", CultureInfo.InvariantCulture),
        _ when cell.Date is { } date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        _ => string.Empty
    };

    /// <summary>
    /// Reads a non-blank cell as a decimal. A typed number is taken as-is (with
    /// double-representation noise rounded away); typed text must be an
    /// invariant number, optionally with thousands-group commas. "12,50" is
    /// refused rather than guessed at, because the two readings differ.
    /// </summary>
    public static bool TryGetDecimal(ImportCell cell, out decimal value, out string? error)
    {
        if (cell.HasFormula)
        {
            value = 0m;
            error = "Formulas are not allowed. Enter a plain value.";
            return false;
        }

        if (cell.Number is { } number)
        {
            value = decimal.Round(number, 12);
            error = null;
            return true;
        }

        var raw = cell.Text?.Trim();

        if (string.IsNullOrEmpty(raw))
        {
            value = 0m;
            error = "Enter a number.";
            return false;
        }

        if (raw.Contains(',') && raw.Contains('.'))
        {
            value = 0m;
            error = $"'{raw}' is ambiguous. Use plain digits with a dot for decimals.";
            return false;
        }

        var candidate = raw;

        if (candidate.Contains(','))
        {
            if (!IsThousandsGrouping(candidate))
            {
                value = 0m;
                error = $"'{raw}' is not a number.";
                return false;
            }

            candidate = candidate.Replace(",", string.Empty);
        }

        if (!decimal.TryParse(candidate, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value))
        {
            error = $"'{raw}' is not a number.";
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>True when the value carries more decimal places than the column allows.</summary>
    public static bool ExceedsDecimalPlaces(decimal value, int places) => decimal.Round(value, places) != value;

    /// <summary>
    /// Reads a date cell, or text in YYYY-MM-DD or DD/MM/YYYY — the two formats
    /// the template guidance names. Anything else is refused with the format
    /// spelled out rather than parsed by guesswork.
    /// </summary>
    public static bool TryGetDate(ImportCell cell, out DateOnly value, out string? error)
    {
        if (cell.HasFormula)
        {
            value = default;
            error = "Formulas are not allowed. Enter a plain value.";
            return false;
        }

        if (cell.Date is { } date)
        {
            value = DateOnly.FromDateTime(date);
            error = null;
            return true;
        }

        var raw = cell.Text?.Trim();

        if (string.IsNullOrEmpty(raw))
        {
            value = default;
            error = "Enter a date.";
            return false;
        }

        if (DateOnly.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out value) ||
            DateOnly.TryParseExact(raw, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out value))
        {
            error = null;
            return true;
        }

        value = default;
        error = $"'{raw}' is not a date. Use YYYY-MM-DD or DD/MM/YYYY, or a real date cell.";
        return false;
    }

    /// <summary>
    /// Reads the Active column: yes/no in several spellings, 1/0, or the words
    /// active/inactive. Blank means yes and is handled by the caller.
    /// </summary>
    public static bool TryGetBool(ImportCell cell, out bool value, out string? error)
    {
        if (cell.HasFormula)
        {
            value = false;
            error = "Formulas are not allowed. Enter a plain value.";
            return false;
        }

        if (cell.Number is { } number)
        {
            if (number == 1m || number == 0m)
            {
                value = number == 1m;
                error = null;
                return true;
            }

            value = false;
            error = "Use yes or no.";
            return false;
        }

        switch (cell.Text?.Trim().ToLowerInvariant())
        {
            case "yes" or "y" or "true" or "1" or "active":
                value = true;
                error = null;
                return true;
            case "no" or "n" or "false" or "0" or "inactive":
                value = false;
                error = null;
                return true;
            default:
                value = false;
                error = "Use yes or no.";
                return false;
        }
    }

    /// <summary>The text a yes/no column shows when it holds something unparseable.</summary>
    public static string BoolText(ImportCell cell) =>
        cell.Number is { } number ? number.ToString("0.######", CultureInfo.InvariantCulture) : cell.Text?.Trim() ?? string.Empty;

    private static bool IsThousandsGrouping(string candidate)
    {
        // One to three digits, then groups of exactly three: 1,000 and 12,345,678 yes; 12,50 no.
        if (candidate.Length == 0 || candidate[0] == '-')
        {
            var unsigned = candidate.TrimStart('-');

            if (unsigned.Length == 0)
            {
                return false;
            }

            candidate = unsigned;
        }

        var parts = candidate.Split(',');

        if (parts[0].Length is < 1 or > 3 || !parts[0].All(char.IsAsciiDigit))
        {
            return false;
        }

        return parts.Skip(1).All(part => part.Length == 3 && part.All(char.IsAsciiDigit));
    }
}
