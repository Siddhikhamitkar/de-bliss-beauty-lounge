using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using DeBliss.Api.Common;
using DeBliss.Api.Controllers;
using DeBliss.Api.Security;
using DeBliss.Application.Common.Exceptions;
using DeBliss.Application.Identity;
using DeBliss.Application.Imports;
using DeBliss.Application.Imports.Dtos;
using DeBliss.Infrastructure.Imports;
using DeBliss.UnitTests.Imports;
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

// Deliberately not ApiTestBase/WebApplicationFactory: this host has no production
// Program, application/infrastructure registration, seeder, context or database.
public sealed class ServiceImportApiTests
{
    private const string Root = "/api/imports/services";
    private static readonly string[] Routes = ["/template", "/preview", "", "/error-report"];

    public static IEnumerable<object[]> AllowedRoutes() =>
        from role in new[] { RoleNames.Owner, RoleNames.Receptionist }
        from route in Routes
        select new object[] { role, route };

    public static IEnumerable<object?[]> DeniedRoutes() =>
        from role in new string?[] { RoleNames.Staff, null }
        from route in Routes
        select new object?[] { role, route, role is null ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden };

    [Theory]
    [MemberData(nameof(AllowedRoutes))]
    public async Task Owner_and_receptionist_reach_all_four_routes_through_real_multipart_and_controller_responses(string role, string route)
    {
        await using var host = await ImportHttpHost.StartAsync();
        using var request = Request(role, route, host.Imports.Upload);
        using var response = await host.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new[] { route }, host.Imports.Calls);

        if (route != "/template")
        {
            Assert.Equal("services.xlsx", host.Imports.FileName);
            Assert.Equal(host.Imports.Upload, host.Imports.Received);
            Assert.True(host.Imports.Token.CanBeCanceled);
        }
        if (route is "/template" or "/error-report")
        {
            Assert.Equal(ImportRules.XlsxContentType, response.Content.Headers.ContentType?.MediaType);
            Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
            var fileName = response.Content.Headers.ContentDisposition?.FileNameStar
                ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"');
            Assert.Equal(route == "/template" ? "services-import-template.xlsx" : "services-errors.xlsx", fileName);
            Assert.Equal(route == "/template" ? host.Imports.Template : host.Imports.Report, await response.Content.ReadAsByteArrayAsync());
        }
        else if (route == "/preview")
        {
            var preview = await response.Content.ReadFromJsonAsync<ImportPreview>();
            Assert.NotNull(preview);
            Assert.Equal("Services", preview.Type);
            Assert.Equal("services.xlsx", preview.FileName);
            Assert.Equal(1, preview.ValidRows);
            Assert.Equal(1, preview.InvalidRows);
            Assert.Equal("Cut", Assert.Single(preview.Rows).Label);
            Assert.Equal(host.Imports.Error, Assert.Single(preview.Errors));
        }
        else
        {
            var result = await response.Content.ReadFromJsonAsync<ImportResult>();
            Assert.NotNull(result);
            Assert.Equal("Services", result.Type);
            Assert.Equal("services.xlsx", result.FileName);
            Assert.True(result.Succeeded);
            Assert.Equal(1, result.Created);
            Assert.Equal(1, result.SkippedInvalid);
            Assert.Equal(1, result.SkippedExisting);
            Assert.Equal(0, result.OpeningStockMovements);
            Assert.Equal(host.Imports.Error, Assert.Single(result.Errors));
        }
    }

    [Theory]
    [MemberData(nameof(DeniedRoutes))]
    public async Task Staff_is_forbidden_and_anonymous_is_challenged_on_every_route_before_ports_run(
        string? role, string route, HttpStatusCode expected)
    {
        await using var host = await ImportHttpHost.StartAsync();
        using var request = Request(role, route, host.Imports.Upload);
        using var response = await host.Client.SendAsync(request);
        Assert.Equal(expected, response.StatusCode);
        Assert.Empty(host.Imports.Calls);
    }

    [Theory]
    [InlineData("/preview", false)]
    [InlineData("", false)]
    [InlineData("/error-report", false)]
    [InlineData("/preview", true)]
    [InlineData("", true)]
    [InlineData("/error-report", true)]
    public async Task Missing_or_empty_file_is_a_real_400_validation_response(string route, bool emptyFile)
    {
        await using var host = await ImportHttpHost.StartAsync();
        using var request = Request(RoleNames.Receptionist, route, emptyFile ? [] : null);
        using var response = await host.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(400, document.RootElement.GetProperty("status").GetInt32());
        Assert.Contains("Choose an .xlsx", document.RootElement.GetProperty("errors").GetProperty("file")[0].GetString());
        Assert.Empty(host.Imports.Calls);
    }

    [Theory]
    [InlineData("/preview")]
    [InlineData("")]
    [InlineData("/error-report")]
    public async Task Import_validation_exceptions_use_the_actual_api_problem_handler(string route)
    {
        await using var host = await ImportHttpHost.StartAsync();
        host.Imports.RejectUpload = true;
        using var request = Request(RoleNames.Owner, route, host.Imports.Upload);
        using var response = await host.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Workbook refused.", document.RootElement.GetProperty("errors").GetProperty("file")[0].GetString());
        Assert.Equal(new[] { route }, host.Imports.Calls);
    }

    [Fact]
    public async Task A_rolled_back_result_is_returned_as_a_summary_not_a_successful_creation()
    {
        await using var host = await ImportHttpHost.StartAsync();
        host.Imports.FailExecution = true;
        using var request = Request(RoleNames.Owner, "", host.Imports.Upload);
        using var response = await host.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ImportResult>();
        Assert.NotNull(result);
        Assert.False(result.Succeeded);
        Assert.Equal(0, result.Created);
        Assert.Equal(2, result.Failed);
        Assert.Equal("The import failed and nothing was saved.", result.Message);
    }

    private static HttpRequestMessage Request(string? role, string route, byte[]? upload)
    {
        var request = new HttpRequestMessage(route == "/template" ? HttpMethod.Get : HttpMethod.Post, Root + route);
        if (role is not null)
            request.Headers.Add("X-Test-Role", role);
        if (route != "/template")
        {
            var form = new MultipartFormDataContent();
            // The customer-only override must not change the service API contract.
            form.Add(new StringContent("true"), "importDuplicates");
            if (upload is not null)
            {
                var file = new ByteArrayContent(upload);
                file.Headers.ContentType = new MediaTypeHeaderValue(ImportRules.XlsxContentType);
                form.Add(file, "file", "services.xlsx");
            }
            request.Content = form;
        }
        return request;
    }

    private sealed class ImportHttpHost(WebApplication app, HttpClient client, RecordingServiceImports imports) : IAsyncDisposable
    {
        public HttpClient Client { get; } = client;
        public RecordingServiceImports Imports { get; } = imports;

        public static async Task<ImportHttpHost> StartAsync()
        {
            var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
            {
                EnvironmentName = "Testing", ContentRootPath = AppContext.BaseDirectory
            });
            builder.Configuration.Sources.Clear();
            builder.Logging.ClearProviders();
            builder.WebHost.UseTestServer();
            // Register the production policies, then replace only authentication.
            builder.Services.AddDeblissSecurity(builder.Configuration);
            builder.Services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = "ImportTest";
                options.DefaultChallengeScheme = "ImportTest";
                options.DefaultForbidScheme = "ImportTest";
            }).AddScheme<AuthenticationSchemeOptions, ImportAuthenticationHandler>("ImportTest", _ => { });
            builder.Services.AddControllers().AddApplicationPart(typeof(ImportsController).Assembly)
                .ConfigureApplicationPartManager(parts => parts.FeatureProviders.Add(new OnlyImportsController()));
            builder.Services.AddProblemDetails();
            builder.Services.AddExceptionHandler<ApiExceptionHandler>();
            var imports = new RecordingServiceImports();
            var unused = new UnusedImports();
            builder.Services.AddSingleton<IServiceImportService>(imports);
            builder.Services.AddSingleton<ICustomerImportService>(unused);
            builder.Services.AddSingleton<IInventoryImportService>(unused);
            var app = builder.Build();
            app.UseExceptionHandler();
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapControllers();
            await app.StartAsync();
            return new ImportHttpHost(app, app.GetTestClient(), imports);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await app.DisposeAsync();
        }
    }

    private sealed class OnlyImportsController : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        {
            foreach (var controller in feature.Controllers.Where(type => type.AsType() != typeof(ImportsController)).ToArray())
                feature.Controllers.Remove(controller);
        }
    }

    private sealed class ImportAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-Role", out var role))
                return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity(
                [new Claim("sub", "11111111-1111-1111-1111-111111111111"), new Claim(AuthClaimNames.Role, role.ToString())],
                Scheme.Name, "sub", AuthClaimNames.Role);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }

    private sealed class RecordingServiceImports : IServiceImportService
    {
        public byte[] Upload { get; } = ImportTestExcel.For(ImportType.Services, ["Cut", "Hair", 150, 30, null, "no", null]);
        public byte[] Template { get; } = new ClosedXmlImportFiles().CreateTemplate(ImportSheets.Template(ImportType.Services));
        public ImportFieldError Error { get; } = new(3, "Price (AED)", "Price cannot be negative.", "-1");
        public byte[] Report { get; }
        public List<string> Calls { get; } = [];
        public string? FileName { get; private set; }
        public byte[]? Received { get; private set; }
        public CancellationToken Token { get; private set; }
        public bool RejectUpload { get; set; }
        public bool FailExecution { get; set; }

        public RecordingServiceImports() =>
            Report = new ClosedXmlImportFiles().CreateErrorReport(new ImportErrorReportSpec("services.xlsx", [], [Error]));

        public byte[] CreateTemplate()
        {
            Calls.Add("/template");
            return Template;
        }

        public async Task<ImportPreview> PreviewAsync(Stream content, string fileName, CancellationToken cancellationToken = default)
        {
            await Capture("/preview", content, fileName, cancellationToken);
            return new ImportPreview("Services", fileName, 2, 1, 1, 0, 0, 0,
                [new ImportPreviewRow(2, "Cut", "Valid", ["Cut", "Hair", "150", "30", "", "no", ""])], [Error]);
        }

        public async Task<ImportResult> ExecuteAsync(Stream content, string fileName, CancellationToken cancellationToken = default)
        {
            await Capture("", content, fileName, cancellationToken);
            return new ImportResult("Services", fileName, !FailExecution, FailExecution ? 0 : 1, 1, 1, FailExecution ? 2 : 0, 0,
                FailExecution ? "The import failed and nothing was saved." : null, [Error]);
        }

        public async Task<byte[]> CreateErrorReportAsync(Stream content, string fileName, CancellationToken cancellationToken = default)
        {
            await Capture("/error-report", content, fileName, cancellationToken);
            return Report;
        }

        private async Task Capture(string route, Stream stream, string fileName, CancellationToken token)
        {
            Calls.Add(route);
            FileName = fileName;
            Token = token;
            using var copy = new MemoryStream();
            await stream.CopyToAsync(copy, token);
            Received = copy.ToArray();
            if (RejectUpload)
                throw new ValidationFailedException(new Dictionary<string, string[]> { ["file"] = ["Workbook refused."] });
        }
    }

    private sealed class UnusedImports : ICustomerImportService, IInventoryImportService
    {
        public byte[] CreateTemplate() => throw new NotSupportedException();
        public byte[] CreateTemplate(ImportType type) => throw new NotSupportedException();
        public Task<ImportPreview> PreviewAsync(Stream content, string fileName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ImportPreview> PreviewAsync(Stream content, string fileName, ImportType type, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ImportResult> ExecuteAsync(Stream content, string fileName, Guid actorUserId, bool importDuplicates, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ImportResult> ExecuteAsync(Stream content, string fileName, ImportType type, Guid actorUserId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<byte[]> CreateErrorReportAsync(Stream content, string fileName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<byte[]> CreateErrorReportAsync(Stream content, string fileName, ImportType type, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
