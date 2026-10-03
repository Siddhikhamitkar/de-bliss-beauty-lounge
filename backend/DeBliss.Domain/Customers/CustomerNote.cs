using DeBliss.Domain.Common;
using DeBliss.Domain.Identity;

namespace DeBliss.Domain.Customers;

public class CustomerNote : AuditableEntity
{
    public Guid CustomerId { get; set; }

    public Customer Customer { get; set; } = null!;

    public string Note { get; set; } = null!;

    public Guid? CreatedByUserId { get; set; }

    public User? CreatedByUser { get; set; }
}
