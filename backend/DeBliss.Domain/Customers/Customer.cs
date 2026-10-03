using DeBliss.Domain.Common;
using DeBliss.Domain.Identity;
using DeBliss.Domain.Scheduling;

namespace DeBliss.Domain.Customers;

public class Customer : AuditableEntity
{
    public string FirstName { get; set; } = null!;

    public string LastName { get; set; } = null!;

    public string? Email { get; set; }

    public string? PhoneNumber { get; set; }

    public DateOnly? DateOfBirth { get; set; }

    /// <summary>Free text rather than an enum so the salon can record it as given.</summary>
    public string? Gender { get; set; }

    public string? Address { get; set; }

    /// <summary>True for a customer record created ad hoc at the front desk.</summary>
    public bool IsWalkIn { get; set; }

    /// <summary>Consent to receive marketing messages. Not consent to be contacted about a booking.</summary>
    public bool MarketingConsent { get; set; }

    /// <summary>
    /// Optional link to a portal login, reserved for the future customer-facing
    /// website so it can reuse the same customer records.
    /// </summary>
    public Guid? UserId { get; set; }

    public User? User { get; set; }

    public ICollection<Appointment> Appointments { get; set; } = new List<Appointment>();

    public ICollection<CustomerNote> Notes { get; set; } = new List<CustomerNote>();
}
