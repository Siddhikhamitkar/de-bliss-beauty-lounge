using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using DeBliss.Api.Controllers;
using DeBliss.Api.Security;
using DeBliss.Application.Common.Paging;
using DeBliss.Application.Identity;
using DeBliss.Application.Staffing;
using DeBliss.Application.Staffing.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace DeBliss.UnitTests.Api;

public sealed class StaffSalarySlipAuthorizationTests
{
    private const string StaffId = "22222222-2222-2222-2222-222222222222";
    private const string Route = "/api/staff/me/salary-slips";

    public static IEnumerable<object?[]> DeniedIdentities() =>
        from role in new string?[] { RoleNames.Staff, RoleNames.Owner, RoleNames.Receptionist, "CUSTOMER", null }
        from staffLink in new string?[] { null, "not-a-guid", "00000000-0000-0000-0000-000000000000", StaffId }
        where role != RoleNames.Staff || staffLink != StaffId
        from route in new[] { Route, $"{Route}/{StaffId}/pdf" }
        select new object?[] { role, staffLink, route };

    [Theory]
    [MemberData(nameof(DeniedIdentities))]
    public async Task Both_routes_require_staff_role_and_nonempty_valid_staff_link_before_service_access(
        string? role, string? staffLink, string route)
    {
        var service = new RejectingSalarySlipService();
        await using var host = await SalaryAuthorizationHost.StartAsync(services => services.AddSingleton<ISalarySlipService>(service));
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{route}?staffId={StaffId}");
        if (role is not null) request.Headers.Add("X-Test-Role", role);
        if (staffLink is not null) request.Headers.Add("X-Test-Staff-Id", staffLink);
        request.Headers.Add("X-Test-Subject", StaffId);

        using var response = await host.Client.SendAsync(request);

        Assert.Equal(role is null ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, service.Calls);
        Assert.Equal(0, host.Work.Saves);
    }

    [Theory]
    [InlineData("GET", "/api/staff/22222222-2222-2222-2222-222222222222/salary")]
    [InlineData("GET", "/api/salary-slips")]
    [InlineData("GET", "/api/salary-slips/22222222-2222-2222-2222-222222222222")]
    [InlineData("GET", "/api/salary-slips/22222222-2222-2222-2222-222222222222/pdf")]
    [InlineData("POST", "/api/salary-slips/preview")]
    [InlineData("POST", "/api/salary-slips")]
    public async Task Staff_with_a_valid_link_still_cannot_use_any_owner_salary_route(string method, string route)
    {
        var service = new RejectingSalarySlipService();
        await using var host = await SalaryAuthorizationHost.StartAsync(services => services.AddSingleton<ISalarySlipService>(service));
        using var request = new HttpRequestMessage(new HttpMethod(method), route);
        request.Headers.Add("X-Test-Role", RoleNames.Staff);
        request.Headers.Add("X-Test-Staff-Id", StaffId);
        request.Content = JsonContent.Create(new { staffId = StaffId });

        using var response = await host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, service.Calls);
    }

    [Fact]
    public void Self_salary_controller_declares_only_list_and_pdf_gets_with_both_authorization_requirements()
    {
        var type = typeof(StaffSalarySlipsController);
        Assert.Equal("api/staff/me/salary-slips", type.GetCustomAttribute<RouteAttribute>()?.Template);
        var authorization = type.GetCustomAttributes<AuthorizeAttribute>().ToArray();
        Assert.Equal(2, authorization.Length);
        Assert.Contains(authorization, attribute => attribute.Policy == AuthPolicies.StaffSelf);
        Assert.Contains(authorization, attribute => attribute.Roles == RoleNames.Staff);
        Assert.Empty(type.GetCustomAttributes<AllowAnonymousAttribute>());
        Assert.True(type.GetCustomAttribute<ResponseCacheAttribute>()?.NoStore);
        var actions = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Assert.Equal(2, actions.Length);
        var templates = actions.Select(action =>
        {
            Assert.Empty(action.GetCustomAttributes<AllowAnonymousAttribute>());
            var verb = Assert.Single(action.GetCustomAttributes<HttpMethodAttribute>());
            Assert.Equal("GET", Assert.Single(verb.HttpMethods));
            return verb.Template ?? string.Empty;
        }).Order().ToArray();
        Assert.Equal(new[] { string.Empty, "{id:guid}/pdf" }, templates);
    }

    private sealed class RejectingSalarySlipService : ISalarySlipService
    {
        public int Calls { get; private set; }

        private Task<T> Unexpected<T>()
        {
            Calls++;
            throw new InvalidOperationException("A rejected request reached salary services.");
        }

        public Task<StaffSalaryDetails> GetStaffSalaryAsync(Guid staffId, CancellationToken cancellationToken = default) => Unexpected<StaffSalaryDetails>();
        public Task<PagedResult<SalarySlipSummaryDto>> GetPagedAsync(Guid? staffId, int? year, int page, int pageSize, CancellationToken cancellationToken = default) => Unexpected<PagedResult<SalarySlipSummaryDto>>();
        public Task<SalarySlipDto> GetAsync(Guid id, CancellationToken cancellationToken = default) => Unexpected<SalarySlipDto>();
        public Task<(SalarySlipDto Slip, byte[] Pdf)> GetPdfAsync(Guid id, CancellationToken cancellationToken = default) => Unexpected<(SalarySlipDto, byte[])>();
        public Task<PagedResult<StaffSalarySlipSummaryDto>> GetForStaffAsync(Guid staffId, int page, int pageSize, CancellationToken cancellationToken = default) => Unexpected<PagedResult<StaffSalarySlipSummaryDto>>();
        public Task<(StaffSalarySlipSummaryDto Slip, byte[] Pdf)> GetPdfForStaffAsync(Guid staffId, Guid id, CancellationToken cancellationToken = default) => Unexpected<(StaffSalarySlipSummaryDto, byte[])>();
        public Task<SalarySlipPreview> PreviewAsync(SalarySlipRequest request, Guid createdBy, CancellationToken cancellationToken = default) => Unexpected<SalarySlipPreview>();
        public Task<SalarySlipDto> SaveAsync(SaveSalarySlipRequest request, Guid createdBy, CancellationToken cancellationToken = default) => Unexpected<SalarySlipDto>();
    }
}
