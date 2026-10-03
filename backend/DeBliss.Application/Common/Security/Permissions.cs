namespace DeBliss.Application.Common.Security;

/// <summary>
/// The operations the system will eventually authorise. Nothing enforces these
/// yet — authentication is deliberately out of scope — but every operation the
/// Services &amp; Pricing module exposes is named here so that the future
/// authorization layer binds to stable identifiers instead of inventing them at
/// the point it is built.
/// </summary>
public static class Permissions
{
    public static class ServiceCatalogue
    {
        public const string ViewServices = "services.read";
        public const string CreateService = "services.create";
        public const string EditService = "services.update";
        public const string ChangeServiceStatus = "services.status";
        public const string ManageCategories = "service-categories.manage";
        public const string AssignStaffToServices = "services.assign-staff";
    }

    public static class Staffing
    {
        public const string ViewStaff = "staff.read";
        public const string CreateStaff = "staff.create";
        public const string EditStaff = "staff.update";
        public const string ChangeStaffStatus = "staff.status";
        public const string ManageStaffServices = "staff.assign-services";
        public const string ManageWorkingHours = "staff.working-hours";
        public const string ManageTimeOff = "staff.time-off";
        public const string CheckAvailability = "staff.availability";
    }
}
