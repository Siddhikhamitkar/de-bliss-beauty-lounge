using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using DeBliss.Api.Common;
using DeBliss.Api.Controllers;
using DeBliss.Api.Security;
using DeBliss.Application.Identity;
using DeBliss.Application.ServiceCatalogue.Dtos;
using DeBliss.Application.Staffing;
using DeBliss.Application.Staffing.Dtos;
using DeBliss.UnitTests.Imports;
using DeBliss.UnitTests.Staffing;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DeBliss.UnitTests.Api;

public sealed class SalaryAuthorizationTests
{
    private const string Id = "22222222-2222-2222-2222-222222222222";
    public static IEnumerable<object?[]> DeniedRoutes() =>
        from role in new string?[] { RoleNames.Receptionist, RoleNames.Staff, "CUSTOMER", null }
        from route in new[] { $"/api/staff/{Id}/salary", "/api/salary-slips", "/api/salary-slips/preview", $"/api/salary-slips/{Id}", $"/api/salary-slips/{Id}/pdf", "SAVE" }
        select new object?[] { role, route };

    [Theory]
    [MemberData(nameof(DeniedRoutes))]
    public async Task Private_routes_reject_nonowners_before_financial_services_run(string? role, string route)
    {
        await using var host = await SalaryAuthorizationHost.StartAsync();
        using var request = new HttpRequestMessage(route is "SAVE" or "/api/salary-slips/preview" ? HttpMethod.Post : HttpMethod.Get,
            route == "SAVE" ? "/api/salary-slips" : route);
        if (role is not null) request.Headers.Add("X-Test-Role", role);
        request.Content = JsonContent.Create(new { });
        using var response = await host.Client.SendAsync(request);
        Assert.Equal(role is null ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, host.Work.Saves);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    public async Task Receptionist_cannot_write_nested_salary_via_existing_operational_routes(string method)
    {
        await using var host = await SalaryAuthorizationHost.StartAsync();
        var member = new DeBliss.Domain.Staffing.Staff { Id = Guid.Parse(Id), FirstName = "Original", LastName = "Employee", TotalSalary = 500 };
        host.Staff.Items.Add(member);
        using var request = new HttpRequestMessage(new HttpMethod(method), method == "POST" ? "/api/staff" : $"/api/staff/{Id}");
        request.Headers.Add("X-Test-Role", RoleNames.Receptionist);
        request.Content = JsonContent.Create(SalaryStaffTests.Request(SalaryStaffTests.Finance()));
        using var response = await host.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, host.Work.Saves);
        Assert.Single(host.Staff.Items);
        Assert.Equal(500, member.TotalSalary);
        Assert.Equal("Original", member.FirstName);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    public async Task Owner_writes_salary_atomically_but_shared_response_stays_private(string method)
    {
        await using var host = await SalaryAuthorizationHost.StartAsync();
        if (method == "PUT") host.Staff.Items.Add(new() { Id = Guid.Parse(Id), FirstName = "Original", LastName = "Employee" });
        using var request = new HttpRequestMessage(new HttpMethod(method), method == "POST" ? "/api/staff" : $"/api/staff/{Id}");
        request.Headers.Add("X-Test-Role", RoleNames.Owner);
        request.Content = JsonContent.Create(SalaryStaffTests.Request(SalaryStaffTests.Finance()));
        using var response = await host.Client.SendAsync(request);
        Assert.Equal(method == "POST" ? HttpStatusCode.Created : HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, host.Work.Saves);
        var saved = Assert.Single(host.Staff.Items);
        Assert.Equal(3100, saved.TotalSalary);
        Assert.Equal("0012 0345", saved.BankAccountNumber);
        var json = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("salary", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("bank", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("iban", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Receptionist_ordinary_edit_preserves_existing_finance_and_cannot_read_it()
    {
        await using var host = await SalaryAuthorizationHost.StartAsync();
        var member = new DeBliss.Domain.Staffing.Staff { Id = Guid.Parse(Id), FirstName = "Original", LastName = "Employee", TotalSalary = 500, BankAccountNumber = "0001234" };
        host.Staff.Items.Add(member);
        host.Client.DefaultRequestHeaders.Add("X-Test-Role", RoleNames.Receptionist);
        using var response = await host.Client.PutAsJsonAsync($"/api/staff/{Id}", SalaryStaffTests.Request(null));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(500, member.TotalSalary);
        Assert.Equal("0001234", member.BankAccountNumber);
        using var detail = await host.Client.GetAsync($"/api/staff/{Id}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        var json = await detail.Content.ReadAsStringAsync();
        Assert.DoesNotContain("salary", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("0001234", json);
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("not-money")]
    public async Task Invalid_money_json_is_a_real_400_without_writes(string value)
    {
        await using var host = await SalaryAuthorizationHost.StartAsync();
        host.Client.DefaultRequestHeaders.Add("X-Test-Role", RoleNames.Owner);
        using var response = await host.Client.PostAsJsonAsync("/api/staff", new
        {
            firstName = "Test", lastName = "Employee", isActive = true,
            salary = new { totalSalary = value }
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, host.Work.Saves);
        Assert.Empty(host.Staff.Items);
    }
}

internal sealed class SalaryAuthorizationHost(WebApplication app, HttpClient client,
    SalaryTestStaffRepository staff, SalaryTestUnitOfWork work) : IAsyncDisposable
{
    public HttpClient Client { get; } = client;
    public SalaryTestStaffRepository Staff { get; } = staff;
    public SalaryTestUnitOfWork Work { get; } = work;

    public static async Task<SalaryAuthorizationHost> StartAsync(Action<IServiceCollection>? configure = null)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing", ContentRootPath = AppContext.BaseDirectory
        });
        builder.Configuration.Sources.Clear();
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();
        builder.Services.AddDeblissSecurity(builder.Configuration);
        builder.Services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = "SalaryTest";
            options.DefaultChallengeScheme = "SalaryTest";
            options.DefaultForbidScheme = "SalaryTest";
        }).AddScheme<AuthenticationSchemeOptions, SalaryAuthenticationHandler>("SalaryTest", _ => { });
        builder.Services.AddControllers().AddApplicationPart(typeof(StaffController).Assembly)
            .ConfigureApplicationPartManager(parts => parts.FeatureProviders.Add(new OnlySalaryControllers()));
        builder.Services.AddProblemDetails();
        builder.Services.AddExceptionHandler<ApiExceptionHandler>();
        var staff = new SalaryTestStaffRepository();
        var work = new SalaryTestUnitOfWork();
        builder.Services.AddSingleton<IStaffService>(new DeBliss.Application.Staffing.StaffService(staff, new ServiceListRepository(), work));
        builder.Services.AddSingleton<IStaffAvailabilityService, UnusedSalaryAvailability>();
        builder.Services.AddSingleton<ISalarySlipService>(_ => throw new InvalidOperationException("A rejected request reached financial services."));
        configure?.Invoke(builder.Services);
        var app = builder.Build();
        app.UseExceptionHandler();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        await app.StartAsync();
        return new(app, app.GetTestClient(), staff, work);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await app.DisposeAsync();
    }

    private sealed class OnlySalaryControllers : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        {
            var allowed = new[] { typeof(StaffController), typeof(StaffSalaryController), typeof(SalarySlipsController), typeof(StaffSalarySlipsController) };
            foreach (var controller in feature.Controllers.Where(type => !allowed.Contains(type.AsType())).ToArray())
                feature.Controllers.Remove(controller);
        }
    }

    private sealed class SalaryAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-Role", out var role))
                return Task.FromResult(AuthenticateResult.NoResult());
            var subject = Request.Headers.TryGetValue("X-Test-Subject", out var supplied) ? supplied.ToString() : "11111111-1111-1111-1111-111111111111";
            var identity = new ClaimsIdentity([new Claim("sub", subject), new Claim(AuthClaimNames.Role, role.ToString())], Scheme.Name, "sub", AuthClaimNames.Role);
            if (Request.Headers.TryGetValue("X-Test-Staff-Id", out var staffId))
                identity.AddClaim(new Claim(AuthClaimNames.StaffId, staffId.ToString()));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }

    private sealed class UnusedSalaryAvailability : IStaffAvailabilityService
    {
        public Task<StaffAvailabilityCheckDto> CheckAsync(Guid staffId, DateTimeOffset start, DateTimeOffset end, Guid? excludeAppointmentId = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<StaffOptionDto>> GetAvailableStaffAsync(Guid serviceId, DateTimeOffset start, DateTimeOffset end, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
