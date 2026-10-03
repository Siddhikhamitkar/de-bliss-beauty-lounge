namespace DeBliss.Domain.Scheduling;

/// <summary>
/// Lifecycle of an appointment. Cancelled, NoShow and Completed appointments are
/// retained permanently for history and reporting. Waitlisted supports a future
/// waitlist feature without a schema change.
/// </summary>
public enum AppointmentStatus
{
    Pending = 0,
    Confirmed = 1,
    CheckedIn = 2,
    InProgress = 3,
    Completed = 4,
    Cancelled = 5,
    NoShow = 6,
    Waitlisted = 7
}

/// <summary>
/// Where the booking originated. WalkIn covers appointments taken for a customer
/// who has no customer record; Online and MobileApp are reserved for the future
/// customer-facing website.
/// </summary>
public enum AppointmentSource
{
    Reception = 0,
    Phone = 1,
    WalkIn = 2,
    Online = 3,
    MobileApp = 4,
    Other = 99
}

/// <summary>
/// Reason a period was made non-bookable. Applies to a single staff member when
/// BlockedTime.StaffId is set, or to the whole salon when it is null.
/// </summary>
public enum BlockedTimeType
{
    Break = 0,
    Meeting = 1,
    Leave = 2,
    Holiday = 3,
    Training = 4,
    Maintenance = 5,
    Personal = 6,
    Other = 99
}
