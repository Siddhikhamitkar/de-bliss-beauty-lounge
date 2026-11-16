using DeBliss.Domain.Common;
using DeBliss.Domain.Identity;
using DeBliss.Domain.Notifications;
using DeBliss.Domain.Scheduling;

namespace DeBliss.Domain.Customers;

public class Customer : AuditableEntity
{
    public string FirstName { get; set; } = null!;

    public string LastName { get; set; } = null!;

    public string? Email { get; set; }

    /// <summary>The mobile number as entered, kept for display.</summary>
    public string? PhoneNumber { get; set; }

    /// <summary>
    /// <see cref="PhoneNumber"/> in E.164 form (<see cref="MobileNumber"/>), derived
    /// on every save — never set it directly. Unique across customers, so one
    /// mobile number identifies exactly one customer record.
    /// </summary>
    public string? NormalizedPhoneNumber { get; set; }

    /// <summary>Whether the customer can sign in to the customer app yet.</summary>
    public CustomerAccountStatus AccountStatus { get; set; } = CustomerAccountStatus.PendingActivation;

    /// <summary>
    /// When the customer last proved they own <see cref="PhoneNumber"/> with a
    /// one-time code. Mobile-number sign-in requires it; it is cleared whenever
    /// the number changes without such proof.
    /// </summary>
    public DateTimeOffset? MobileVerifiedAt { get; set; }

    public DateOnly? DateOfBirth { get; set; }

    /// <summary>Free text rather than an enum so the salon can record it as given.</summary>
    public string? Gender { get; set; }

    public string? Address { get; set; }

    /// <summary>True for a customer record created ad hoc at the front desk.</summary>
    public bool IsWalkIn { get; set; }

    /// <summary>Consent to receive marketing messages. Not consent to be contacted about a booking.</summary>
    public bool MarketingConsent { get; set; }

    /// <summary>
    /// Consent to receive appointment notifications on WhatsApp. Unknown (the
    /// default) never counts as consent: the dispatcher only sends to customers
    /// explicitly marked as opted in.
    /// </summary>
    public WhatsAppOptInStatus WhatsAppOptInStatus { get; set; }

    public DateTimeOffset? WhatsAppOptInDate { get; set; }

    public DateTimeOffset? WhatsAppOptOutDate { get; set; }

    /// <summary>Where consent was captured, e.g. "Reception" or "Website".</summary>
    public string? WhatsAppOptInSource { get; set; }

    /// <summary>
    /// Inactive customers stay in the database so appointment history keeps its
    /// references, but drop out of the default pick lists the appointment
    /// calendar and front desk search use.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Optional link to a portal login, reserved for the future customer-facing
    /// website so it can reuse the same customer records.
    /// </summary>
    public Guid? UserId { get; set; }

    public User? User { get; set; }

    public ICollection<Appointment> Appointments { get; set; } = new List<Appointment>();

    public ICollection<CustomerNote> Notes { get; set; } = new List<CustomerNote>();
}
