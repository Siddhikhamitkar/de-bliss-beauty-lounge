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

        /// <summary>
        /// A staff member's clock-in/clock-out history. Owner only: it is private
        /// staff data, and the front desk does not need it to run the day.
        /// </summary>
        public const string ViewAttendance = "staff.attendance.read";
    }

    public static class Customers
    {
        public const string ViewCustomers = "customers.read";
        public const string CreateCustomer = "customers.create";
        public const string EditCustomer = "customers.update";
        public const string ChangeCustomerStatus = "customers.status";
        public const string ManageCustomerNotes = "customers.notes";

        /// <summary>Recording or withdrawing the customer's WhatsApp appointment-message consent.</summary>
        public const string ManageWhatsAppOptIn = "customers.whatsapp-opt-in";
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

    public static class Inventory
    {
        /// <summary>The stock list, product details and movement history. Owner and receptionist.</summary>
        public const string ViewInventory = "inventory.read";

        /// <summary>
        /// Everything that changes a product or its stock: create, edit,
        /// activate/deactivate, receive, usage, adjustments, reversals. Owner
        /// only — the front desk sells but never restocks.
        /// </summary>
        public const string ManageInventory = "inventory.manage";
    }

    public static class Dashboard
    {
        public const string ViewReceptionistDashboard = "dashboard.receptionist.read";

        /// <summary>
        /// The owner dashboard's financial aggregates — net revenue, VAT, tips,
        /// outstanding money. Owner only; the front desk never sees money.
        /// </summary>
        public const string ViewOwnerDashboard = "dashboard.owner.read";
    }

    public static class Notifications
    {
        /// <summary>Reminder history and reminder status anywhere they are shown. Owner and receptionist.</summary>
        public const string ViewReminders = "reminders.read";

        /// <summary>Rule timing, enable/disable and the WhatsApp connection status. Owner only.</summary>
        public const string ManageReminderSettings = "reminders.settings";

        /// <summary>Sending the operator test message. Owner only; never available to customers.</summary>
        public const string SendTestReminder = "reminders.test";
    }

    public static class Owner
    {
        /// <summary>The future owner workspace (dashboard, reports, settings). Enforced now on the owner landing endpoint.</summary>
        public const string AccessWorkspace = "owner.workspace.access";
    }

    public static class Settings
    {
        /// <summary>
        /// The live tax rate and pricing mode, read by the checkout screen to
        /// preview what is owed. Owner and receptionist.
        /// </summary>
        public const string ViewTaxSettings = "settings.tax.read";

        /// <summary>Changing the salon's tax configuration. Owner only.</summary>
        public const string ManageTaxSettings = "settings.tax.manage";
    }
}
