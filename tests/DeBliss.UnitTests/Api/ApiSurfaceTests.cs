using System.Net;
using System.Reflection;
using DeBliss.Application.Common.Security;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace DeBliss.UnitTests.Api;

/// <summary>
/// Authorisation is deliberately not built yet. These tests keep that honest: every
/// endpoint the module exposes has to name the permission it will require, so the
/// future authorization layer has a complete list to bind to instead of discovering
/// at release time that an endpoint was missed.
/// </summary>
public sealed class ApiSurfaceTests : ApiTestBase
{
    private static readonly Dictionary<string, string[]> ExpectedPermissions = new(StringComparer.Ordinal)
    {
        ["GET api/service-categories"] = [Permissions.ServiceCatalogue.ManageCategories],
        ["POST api/service-categories"] = [Permissions.ServiceCatalogue.ManageCategories],
        ["GET api/service-categories/{id:guid}"] = [Permissions.ServiceCatalogue.ManageCategories],
        ["PUT api/service-categories/{id:guid}"] = [Permissions.ServiceCatalogue.ManageCategories],
        ["PATCH api/service-categories/{id:guid}/status"] = [Permissions.ServiceCatalogue.ManageCategories],
        ["GET api/services"] = [Permissions.ServiceCatalogue.ViewServices],
        ["GET api/services/bookable"] = [Permissions.ServiceCatalogue.ViewServices],
        ["GET api/services/{id:guid}"] = [Permissions.ServiceCatalogue.ViewServices],
        ["POST api/services"] =
        [
            Permissions.ServiceCatalogue.CreateService,
            Permissions.ServiceCatalogue.AssignStaffToServices
        ],
        ["PUT api/services/{id:guid}"] =
        [
            Permissions.ServiceCatalogue.EditService,
            Permissions.ServiceCatalogue.AssignStaffToServices
        ],
        ["PATCH api/services/{id:guid}/status"] = [Permissions.ServiceCatalogue.ChangeServiceStatus],
        ["GET api/service-bundles/bookable"] = [Permissions.ServiceCatalogue.ViewServices],
        ["GET api/service-bundles/{id:guid}"] = [Permissions.ServiceCatalogue.ViewServices],
        ["POST api/service-bundles"] = [Permissions.ServiceCatalogue.CreateService],
        ["PUT api/service-bundles/{id:guid}"] = [Permissions.ServiceCatalogue.EditService],
        ["PATCH api/service-bundles/{id:guid}/status"] = [Permissions.ServiceCatalogue.ChangeServiceStatus],
        ["GET api/staff-members"] = [Permissions.ServiceCatalogue.AssignStaffToServices],
        ["GET api/staff"] = [Permissions.Staffing.ViewStaff],
        ["POST api/staff"] = [Permissions.Staffing.CreateStaff],
        ["GET api/staff/availability"] = [Permissions.Staffing.CheckAvailability],
        ["GET api/staff/{id:guid}"] = [Permissions.Staffing.ViewStaff],
        ["PUT api/staff/{id:guid}"] = [Permissions.Staffing.EditStaff],
        ["PATCH api/staff/{id:guid}/status"] = [Permissions.Staffing.ChangeStaffStatus],
        ["GET api/staff/{staffId:guid}/services"] = [Permissions.Staffing.ViewStaff],
        ["PUT api/staff/{staffId:guid}/services"] = [Permissions.Staffing.ManageStaffServices],
        ["GET api/staff/{staffId:guid}/working-hours"] = [Permissions.Staffing.ViewStaff],
        ["PUT api/staff/{staffId:guid}/working-hours"] = [Permissions.Staffing.ManageWorkingHours],
        ["GET api/staff/{staffId:guid}/time-off"] = [Permissions.Staffing.ViewStaff],
        ["POST api/staff/{staffId:guid}/time-off"] = [Permissions.Staffing.ManageTimeOff],
        ["PUT api/staff/{staffId:guid}/time-off/{timeOffId:guid}"] = [Permissions.Staffing.ManageTimeOff],
        ["DELETE api/staff/{staffId:guid}/time-off/{timeOffId:guid}"] = [Permissions.Staffing.ManageTimeOff],
        ["GET api/staff/{staffId:guid}/attendance"] = [Permissions.Staffing.ViewAttendance],
        ["GET api/staff/me"] = [StaffSelf],
        ["GET api/staff/me/appointments"] = [StaffSelf],
        ["GET api/staff/me/schedule"] = [StaffSelf],
        ["GET api/staff/me/attendance"] = [StaffSelf],
        ["GET api/staff/me/attendance/today"] = [StaffSelf],
        ["POST api/staff/me/attendance/check-in"] = [StaffSelf],
        ["POST api/staff/me/attendance/check-out"] = [StaffSelf],
        ["GET api/customers"] = [Permissions.Customers.ViewCustomers],
        ["POST api/customers"] = [Permissions.Customers.CreateCustomer],
        ["GET api/customers/{id:guid}"] = [Permissions.Customers.ViewCustomers],
        ["PUT api/customers/{id:guid}"] = [Permissions.Customers.EditCustomer],
        ["PATCH api/customers/{id:guid}/status"] = [Permissions.Customers.ChangeCustomerStatus],
        ["PUT api/customers/{id:guid}/whatsapp-opt-in"] = [Permissions.Customers.ManageWhatsAppOptIn],
        ["GET api/customers/{id:guid}/appointments"] = [Permissions.Customers.ViewCustomers],
        ["GET api/customers/{id:guid}/notes"] = [Permissions.Customers.ViewCustomers],
        ["POST api/customers/{id:guid}/notes"] = [Permissions.Customers.ManageCustomerNotes],
        ["PUT api/customers/{id:guid}/notes/{noteId:guid}"] = [Permissions.Customers.ManageCustomerNotes],
        ["GET api/appointments/{id:guid}"] = [Permissions.Scheduling.ViewAppointments],
        ["POST api/appointments"] = [Permissions.Scheduling.CreateAppointment],
        ["PUT api/appointments/{id:guid}"] = [Permissions.Scheduling.EditAppointment],
        ["PATCH api/appointments/{id:guid}/schedule"] = [Permissions.Scheduling.RescheduleAppointment],
        ["PATCH api/appointments/{id:guid}/status"] = [Permissions.Scheduling.ChangeAppointmentStatus],
        ["GET api/calendar"] = [Permissions.Scheduling.ViewCalendar],
        ["GET api/invoices"] = [Permissions.Billing.ViewInvoices],
        ["POST api/invoices"] = [Permissions.Billing.CreateCheckout],
        ["GET api/invoices/{id:guid}"] =
        [
            Permissions.Billing.ViewInvoices,
            Permissions.Billing.ViewPayments
        ],
        ["POST api/invoices/{id:guid}/checkout"] =
        [
            Permissions.Billing.CreateCheckout,
            Permissions.Billing.ApplyManualDiscount
        ],
        ["GET api/invoices/{id:guid}/receipt"] = [Permissions.Billing.ViewInvoices],
        ["POST api/invoices/{id:guid}/lines"] = [Permissions.Billing.CreateCheckout],
        ["PUT api/invoices/{id:guid}/lines/{lineId:guid}"] = [Permissions.Billing.CreateCheckout],
        ["DELETE api/invoices/{id:guid}/lines/{lineId:guid}"] = [Permissions.Billing.CreateCheckout],
        ["PUT api/invoices/{id:guid}/tip"] = [Permissions.Billing.CreateCheckout],
        ["GET api/settings/tax"] = [Permissions.Settings.ViewTaxSettings],
        ["PUT api/settings/tax"] = [Permissions.Settings.ManageTaxSettings],
        ["GET api/inventory"] = [Permissions.Inventory.ViewInventory],
        ["GET api/inventory/sellable"] = [Permissions.Inventory.ViewInventory],
        ["GET api/inventory/{id:guid}"] = [Permissions.Inventory.ViewInventory],
        ["GET api/inventory/{id:guid}/movements"] = [Permissions.Inventory.ViewInventory],
        ["POST api/inventory"] = [Permissions.Inventory.ManageInventory],
        ["PUT api/inventory/{id:guid}"] = [Permissions.Inventory.ManageInventory],
        ["PATCH api/inventory/{id:guid}/status"] = [Permissions.Inventory.ManageInventory],
        ["POST api/inventory/{id:guid}/stock"] = [Permissions.Inventory.ManageInventory],
        ["POST api/inventory/{id:guid}/usage"] = [Permissions.Inventory.ManageInventory],
        ["POST api/inventory/{id:guid}/adjustments"] = [Permissions.Inventory.ManageInventory],
        ["POST api/inventory/{id:guid}/movements/{movementId:guid}/reversal"] = [Permissions.Inventory.ManageInventory],
        ["GET api/coupons"] = [Permissions.Billing.ManageCoupons],
        ["POST api/coupons"] = [Permissions.Billing.ManageCoupons],
        ["GET api/coupons/{id:guid}"] = [Permissions.Billing.ManageCoupons],
        ["PUT api/coupons/{id:guid}"] = [Permissions.Billing.ManageCoupons],
        ["PATCH api/coupons/{id:guid}/status"] = [Permissions.Billing.ManageCoupons],
        ["POST api/coupons/validate"] = [Permissions.Billing.CreateCheckout],
        ["GET api/dashboard/receptionist"] = [Permissions.Dashboard.ViewReceptionistDashboard],
        ["GET api/dashboard/owner"] = [Permissions.Dashboard.ViewOwnerDashboard],
        ["GET api/reminders/history"] = [Permissions.Notifications.ViewReminders],
        ["GET api/reminders/rules"] = [Permissions.Notifications.ManageReminderSettings],
        ["PUT api/reminders/rules/{id:guid}"] = [Permissions.Notifications.ManageReminderSettings],
        ["GET api/reminders/provider"] = [Permissions.Notifications.ManageReminderSettings],
        ["POST api/reminders/test"] = [Permissions.Notifications.SendTestReminder],
        ["GET api/appointments/{appointmentId:guid}/reminders"] = [Permissions.Notifications.ViewReminders],
        ["POST api/auth/login"] = [Anonymous],
        ["GET api/auth/me"] = [Authenticated],
        ["POST api/auth/logout"] = [Anonymous],
        ["GET api/owner/workspace"] = [Permissions.Owner.AccessWorkspace],
        ["GET api/public/services"] = [Anonymous],
        ["GET api/public/availability"] = [Anonymous],
        ["POST api/public/customers/register"] = [Anonymous],
        ["GET api/customer/me"] = [CustomerSelf],
        ["POST api/customer/appointments"] = [CustomerSelf],
        ["GET api/customer/appointments"] = [CustomerSelf]
    };

    /// <summary>Endpoints that deliberately allow anonymous access (login/logout).</summary>
    private const string Anonymous = "anonymous";

    /// <summary>Endpoints any authenticated user can reach, regardless of role.</summary>
    private const string Authenticated = "authenticated:any-role";

    /// <summary>Endpoints a signed-in user reaches only through the Staff record linked to their login.</summary>
    private const string StaffSelf = "authenticated:staff-self";

    /// <summary>Endpoints a signed-in user reaches only through the Customer record linked to their login.</summary>
    private const string CustomerSelf = "authenticated:customer-self";

    [Fact]
    public void Every_api_endpoint_declares_the_permissions_it_will_require()
    {
        var endpoints = ApiEndpoints().ToArray();

        var undeclared = endpoints.Except(ExpectedPermissions.Keys, StringComparer.Ordinal).Order().ToArray();
        var stale = ExpectedPermissions.Keys.Except(endpoints, StringComparer.Ordinal).Order().ToArray();

        Assert.True(
            undeclared.Length == 0 && stale.Length == 0,
            $"Endpoints with no declared permission: [{string.Join(", ", undeclared)}]. " +
            $"Declared permissions pointing at endpoints that no longer exist: [{string.Join(", ", stale)}]. " +
            $"The API now exposes: [{string.Join(", ", endpoints.Order())}].");
    }

    [Fact]
    public void Every_declared_permission_is_bound_to_at_least_one_endpoint()
    {
        var bound = ExpectedPermissions.Values.SelectMany(permissions => permissions).ToHashSet(StringComparer.Ordinal);

        var unused = typeof(Permissions)
            .GetNestedTypes(BindingFlags.Public)
            .SelectMany(type => type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy))
            .Where(field => field.IsLiteral)
            .Select(field => (string)field.GetRawConstantValue()!)
            .Where(permission => !bound.Contains(permission))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            unused.Length == 0,
            $"Permissions nothing uses: [{string.Join(", ", unused)}]. " +
            "Either an endpoint is missing its authorization note or the permission is dead.");
    }

    [Fact]
    public async Task The_api_answers_over_plain_http_so_the_browser_dev_server_can_proxy_to_it()
    {
        // Anonymous traffic is turned away before the pipeline runs, and an
        // authenticated request still answers 200 — AllowAutoRedirect is off, so
        // a UseHttpsRedirection that suddenly found an HTTPS port would show up
        // here as a 307 instead.
        using var anonymous = AnonymousClient;
        var rejected = await anonymous.GetAsync("/api/service-categories");

        Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);

        var response = await Client.GetAsync("/api/service-categories");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private IEnumerable<string> ApiEndpoints()
    {
        var dataSource = Factory.Services.GetRequiredService<EndpointDataSource>();

        foreach (var endpoint in dataSource.Endpoints.OfType<RouteEndpoint>())
        {
            var pattern = endpoint.RoutePattern.RawText ?? string.Empty;

            if (!pattern.StartsWith("api/", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var method in endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [])
            {
                yield return $"{method} {pattern}";
            }
        }
    }
}
