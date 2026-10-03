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
        ["GET api/customers"] = [Permissions.Customers.ViewCustomers],
        ["POST api/customers"] = [Permissions.Customers.CreateCustomer],
        ["GET api/customers/{id:guid}"] = [Permissions.Customers.ViewCustomers],
        ["PUT api/customers/{id:guid}"] = [Permissions.Customers.EditCustomer],
        ["PATCH api/customers/{id:guid}/status"] = [Permissions.Customers.ChangeCustomerStatus],
        ["GET api/customers/{id:guid}/appointments"] = [Permissions.Customers.ViewCustomers],
        ["GET api/customers/{id:guid}/notes"] = [Permissions.Customers.ViewCustomers],
        ["POST api/customers/{id:guid}/notes"] = [Permissions.Customers.ManageCustomerNotes],
        ["PUT api/customers/{id:guid}/notes/{noteId:guid}"] = [Permissions.Customers.ManageCustomerNotes]
    };

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
        // AllowAutoRedirect is off, so a UseHttpsRedirection that suddenly found an
        // HTTPS port would show up here as a 307 instead of a 200.
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
