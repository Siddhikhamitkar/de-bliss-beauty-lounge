using DeBliss.Domain.Common;

namespace DeBliss.Domain.Services;

public class ServiceCategory : AuditableEntity
{
    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<Service> Services { get; set; } = new List<Service>();
}
