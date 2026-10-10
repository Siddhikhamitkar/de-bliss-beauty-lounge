namespace DeBliss.Application.Inventory;

/// <summary>
/// Field limits the inventory DTO validation and the EF configuration must
/// agree on, following the billing module's pattern.
/// </summary>
internal static class InventoryLimits
{
    public const int NameMaxLength = 200;
    public const int BrandMaxLength = 100;
    public const int SkuMaxLength = 50;
    public const int CategoryMaxLength = 100;
    public const int UnitOfMeasureMaxLength = 20;
    public const int ReferenceMaxLength = 100;
    public const int NotesMaxLength = 500;

    /// <summary>The unit shown when the owner does not name one.</summary>
    public const string DefaultUnitOfMeasure = "pcs";
}
