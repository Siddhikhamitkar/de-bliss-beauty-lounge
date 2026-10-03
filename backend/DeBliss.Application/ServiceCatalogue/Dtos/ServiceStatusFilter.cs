namespace DeBliss.Application.ServiceCatalogue.Dtos;

/// <summary>Narrows a list to active, inactive or every record regardless of state.</summary>
public enum ServiceStatusFilter
{
    All = 0,
    Active = 1,
    Inactive = 2
}
