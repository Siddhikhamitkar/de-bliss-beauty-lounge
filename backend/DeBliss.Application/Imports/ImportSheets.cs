using DeBliss.Application.Imports.Dtos;

namespace DeBliss.Application.Imports;

public enum ImportType
{
    Customers = 0,
    RetailProducts = 1,
    SalonUseProducts = 2,
    Services = 3
}

/// <summary>
/// The fixed shape of one import: its sheet name, its exact columns, and the
/// example row and per-column guidance the downloadable template shows on a
/// separate "Example" sheet. The data sheet itself carries headers only, so the
/// example row can never be imported by accident — the parser never reads the
/// Example sheet at all.
/// </summary>
public sealed record ImportTypeDescriptor(
    ImportType Type,
    string SheetName,
    string TemplateFileName,
    IReadOnlyList<string> Headers,
    IReadOnlyList<string> ExampleValues,
    IReadOnlyList<string> Guidance);

/// <summary>Positional column indexes per import flavour, valid once the header row has been validated as an exact match.</summary>
internal static class CustomerColumns
{
    public const int FirstName = 0;
    public const int LastName = 1;
    public const int Mobile = 2;
    public const int Email = 3;
    public const int DateOfBirth = 4;
    public const int Gender = 5;
    public const int Address = 6;
    public const int Notes = 7;
    public const int Active = 8;
}

internal static class RetailColumns
{
    public const int Name = 0;
    public const int Brand = 1;
    public const int Sku = 2;
    public const int Category = 3;
    public const int CostPrice = 4;
    public const int SellingPrice = 5;
    public const int MinimumStock = 6;
    public const int OpeningStock = 7;
    public const int Active = 8;
}

internal static class SalonUseColumns
{
    public const int Name = 0;
    public const int Brand = 1;
    public const int Sku = 2;
    public const int Category = 3;
    public const int UnitOfMeasure = 4;
    public const int MinimumStock = 5;
    public const int OpeningStock = 6;
    public const int Active = 7;
}

public static class ImportSheets
{
    public static ImportTypeDescriptor Describe(ImportType type) => type switch
    {
        ImportType.Customers => new ImportTypeDescriptor(
            type,
            "Customers",
            "customer-import-template.xlsx",
            CustomerHeaders,
            [.. CustomerExample],
            [.. CustomerGuidance]),
        ImportType.RetailProducts => new ImportTypeDescriptor(
            type,
            "Retail Products",
            "retail-products-import-template.xlsx",
            RetailHeaders,
            [.. RetailExample],
            [.. RetailGuidance]),
        ImportType.SalonUseProducts => new ImportTypeDescriptor(
            type,
            "Salon-Use Products",
            "salon-use-products-import-template.xlsx",
            SalonUseHeaders,
            [.. SalonUseExample],
            [.. SalonUseGuidance]),
        ImportType.Services => new ImportTypeDescriptor(
            type,
            "Services",
            "services-import-template.xlsx",
            ["Service name", "Category name", "Price (AED)", "Duration (minutes)", "Description", "Active", "Eligible staff"],
            ["Haircut", "Hair", "150", "45", "Consultation and cut", "no", ""],
            [
                "Required. Up to 150 characters. Same name in the same category is skipped, never overwritten.",
                "Required. Existing category name, ignoring case and surrounding spaces. Categories are never created.",
                "Required. AED 0 to 100000, at most two decimal places. Use plain digits with a dot for decimals.",
                "Required. Whole minutes from 1 to 1440.",
                "Optional. Up to 1000 characters.",
                "Optional. Yes/no (also true/false, active/inactive, 1/0). Blank means yes. Active requires active eligible staff. Use no to assign staff later. Online booking is off and display position is 0.",
                "Existing staff full names separated by semicolons. Case and surrounding spaces are ignored. Unknown or ambiguous names are rejected. Active services need at least one active staff member."
            ]),
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown import type.")
    };

    public static bool IsInventory(ImportType type) => type is ImportType.RetailProducts or ImportType.SalonUseProducts;

    public static IReadOnlyList<ImportType> All() => [ImportType.Customers, ImportType.RetailProducts, ImportType.SalonUseProducts, ImportType.Services];

    /// <summary>The template layout for one import, ready for the file port.</summary>
    public static ImportTemplateSpec Template(ImportType type)
    {
        var descriptor = Describe(type);

        return new ImportTemplateSpec(descriptor.SheetName, descriptor.Headers, descriptor.ExampleValues, descriptor.Guidance);
    }

    private static readonly string[] CustomerHeaders =
    [
        "First name", "Last name", "Mobile", "Email", "Date of birth", "Gender", "Address", "Notes", "Active"
    ];

    private static readonly string[] CustomerExample =
    [
        "Amelia", "Fernandes", "+91 98765 43210", "amelia@example.com", "1992-04-18", "Female",
        "12 Rose Lane, Panjim", "Prefers evening appointments", "yes"
    ];

    private static readonly string[] CustomerGuidance =
    [
        "Required. Up to 100 characters.",
        "Required. Up to 100 characters.",
        "Required. Spaces and formatting are fine; at least 5 digits count.",
        "Optional. Must be a valid email address.",
        "Optional. A date cell, or text in YYYY-MM-DD or DD/MM/YYYY.",
        "Optional. Up to 20 characters.",
        "Optional. Up to 500 characters.",
        "Optional. Saved as a note on the customer's profile. Up to 2000 characters.",
        "Optional. Yes or no; blank means yes."
    ];

    private static readonly string[] RetailHeaders =
    [
        "Product name", "Brand", "SKU", "Category", "Cost price", "Selling price", "Minimum stock", "Opening stock", "Active"
    ];

    private static readonly string[] RetailExample =
    [
        "Keratin Shampoo 250ml", "Lumière", "LUM-KER-250", "Hair Care", "320.50", "549", "5", "24", "yes"
    ];

    private static readonly string[] RetailGuidance =
    [
        "Required. Up to 200 characters.",
        "Optional. Up to 100 characters.",
        "Required. Unique code; rows whose SKU already exists are skipped. Up to 50 characters.",
        "Optional. Up to 100 characters.",
        "Required. What the salon pays, in rupees, up to 2 decimals.",
        "Required. What the client pays; greater than zero, up to 2 decimals.",
        "Optional. Low-stock alert level; blank means zero.",
        "Optional. Recorded as an opening-stock movement with your name on it — never written silently onto the shelf.",
        "Optional. Yes or no; blank means yes."
    ];

    private static readonly string[] SalonUseHeaders =
    [
        "Product name", "Brand", "SKU", "Category", "Unit of measure", "Minimum stock", "Opening stock", "Active"
    ];

    private static readonly string[] SalonUseExample =
    [
        "Hydrogen Peroxide 3%", "Salon Basics", "SB-H2O2-1L", "Colour", "ml", "500", "2000", "yes"
    ];

    private static readonly string[] SalonUseGuidance =
    [
        "Required. Up to 200 characters.",
        "Optional. Up to 100 characters.",
        "Required. Unique code; rows whose SKU already exists are skipped. Up to 50 characters.",
        "Optional. Up to 100 characters.",
        "Optional. pcs, ml, g…; blank means pcs. Up to 20 characters.",
        "Optional. Low-stock alert level; blank means zero.",
        "Optional. Recorded as an opening-stock movement with your name on it — never written silently onto the shelf.",
        "Optional. Yes or no; blank means yes."
    ];
}
