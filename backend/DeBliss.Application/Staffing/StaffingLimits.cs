namespace DeBliss.Application.Staffing;

/// <summary>
/// The limits the staff module enforces. They mirror the column lengths in the
/// EF Core configurations so an over-long value is rejected with a clear 400
/// instead of failing inside SQL Server.
/// </summary>
public static class StaffingLimits
{
    public const int NameMaxLength = 100;
    public const int EmailMaxLength = 256;
    public const int PhoneMaxLength = 30;
    public const int JobTitleMaxLength = 100;

    /// <summary>#RGB or #RRGGBB, matching the calendar-colour column.</summary>
    public const int CalendarColorMaxLength = 9;

    public const int TimeOffReasonMaxLength = 500;
}
