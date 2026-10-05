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

    public static class Customers
    {
        public const string ViewCustomers = "customers.read";
        public const string CreateCustomer = "customers.create";
        public const string EditCustomer = "customers.update";
        public const string ChangeCustomerStatus = "customers.status";
        public const string ManageCustomerNotes = "customers.notes";
    }

    public static class Scheduling
    {
        public const string ViewAppointments = "appointments.read";
        public const string CreateAppointment = "appointments.create";
        public const string EditAppointment = "appointments.update";
        public const string RescheduleAppointment = "appointments.reschedule";
        public const string ChangeAppointmentStatus = "appointments.status";
        public const string ViewCalendar = "calendar.read";
    }

    public static class Billing
    {
        public const string ViewInvoices = "invoices.read";
        public const string CreateCheckout = "invoices.checkout";
        public const string ApplyManualDiscount = "invoices.manual-discount";
        public const string ViewPayments = "payments.read";
        public const string ManageCoupons = "coupons.manage";
    }

    public static class Dashboard
    {
        public const string ViewReceptionistDashboard = "dashboard.receptionist.read";
    }

    public static class Owner
    {
        /// <summary>The future owner workspace (dashboard, reports, settings). Enforced now on the owner landing endpoint.</summary>
        public const string AccessWorkspace = "owner.workspace.access";
    }
}
