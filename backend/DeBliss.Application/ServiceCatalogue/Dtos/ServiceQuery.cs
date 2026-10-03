namespace DeBliss.Application.ServiceCatalogue.Dtos;

/// <summary>
/// Everything the service list can be narrowed by. Page and page size are
/// normalised by <see cref="Normalized"/> before they reach the database so a
/// caller cannot ask for page zero or an unbounded page.
/// </summary>
public sealed record ServiceQuery(
    string? Search,
    Guid? CategoryId,
    ServiceStatusFilter Status,
    bool BookableOnly,
    int Page,
    int PageSize)
{
    public const int DefaultPageSize = 50;
    public const int MaximumPageSize = 200;

    public ServiceQuery Normalized()
    {
        var pageSize = PageSize <= 0 ? DefaultPageSize : Math.Min(PageSize, MaximumPageSize);

        return this with
        {
            Search = string.IsNullOrWhiteSpace(Search) ? null : Search.Trim(),
            Page = Math.Max(Page, 1),
            PageSize = pageSize
        };
    }
}
