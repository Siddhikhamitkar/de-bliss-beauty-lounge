namespace DeBliss.Domain.Staffing;

public enum TimeOffType
{
    Leave = 0,
    Sick = 1,
    Holiday = 2,
    Training = 3,
    Personal = 4,
    Other = 99
}

/// <summary>
/// Whether a staff member's day is still open. There is no "absent" value: a
/// record only exists once somebody has clocked in, so absence is the absence
/// of a row rather than an inferred state.
/// </summary>
public enum AttendanceStatus
{
    CheckedIn = 0,
    CheckedOut = 1
}
