namespace DeBliss.Application.Scheduling;

/// <summary>
/// Field and rule limits for the scheduling module. Values mirror the column
/// sizes in the appointment configurations, so an over-long value is rejected
/// with a clear 400 instead of failing inside SQL Server.
/// </summary>
public static class SchedulingLimits
{
    public const int WalkInNameMaxLength = 200;

    public const int WalkInPhoneMaxLength = 30;

    public const int NotesMaxLength = 2000;

    public const int CancellationReasonMaxLength = 500;

    /// <summary>The smallest total duration a booking or a resize may produce.</summary>
    public const int MinimumDurationMinutes = 5;
}
