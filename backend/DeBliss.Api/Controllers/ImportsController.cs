using DeBliss.Api.Security;
using DeBliss.Application.Common.Exceptions;
using DeBliss.Application.Imports;
using DeBliss.Application.Imports.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DeBliss.Api.Controllers;

/// <summary>
/// The Excel import endpoints: template downloads, previews, executions and
/// error reports for customers and for each inventory flavour. The permission
/// split matches the feature's own: customers follow the front-desk policy
/// everyone at the desk already holds, inventory is owner-only because the rest
/// of the inventory write API is — and the framework answers a forbidden
/// request with 403 before any controller code runs.
/// </summary>
[ApiController]
[Authorize]
[Route("api/imports")]
public sealed class ImportsController(
    ICustomerImportService customerImports,
    IInventoryImportService inventoryImports,
    IServiceImportService serviceImports) : ControllerBase
{
    private const long UploadLimitBytes = 6 * 1024 * 1024;

    [HttpGet("customers/template")]
    [Authorize(Policy = AuthPolicies.Operational)]
    public IActionResult CustomerTemplate()
    {
        return ExcelFile(
            customerImports.CreateTemplate(),
            ImportSheets.Describe(ImportType.Customers).TemplateFileName);
    }

    [HttpPost("customers/preview")]
    [Authorize(Policy = AuthPolicies.Operational)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(UploadLimitBytes)]
    public async Task<IActionResult> PreviewCustomers(IFormFile? file, CancellationToken cancellationToken)
    {
        var upload = RequireFile(file);

        return Ok(await customerImports.PreviewAsync(upload.OpenReadStream(), upload.FileName, cancellationToken));
    }

    [HttpPost("customers")]
    [Authorize(Policy = AuthPolicies.Operational)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(UploadLimitBytes)]
    public async Task<IActionResult> ImportCustomers(
        IFormFile? file,
        [FromForm] bool importDuplicates,
        CancellationToken cancellationToken)
    {
        var upload = RequireFile(file);

        var result = await customerImports.ExecuteAsync(
            upload.OpenReadStream(),
            upload.FileName,
            ActingUserId() ?? Guid.Empty,
            importDuplicates,
            cancellationToken);

        return Ok(result);
    }

    [HttpPost("customers/error-report")]
    [Authorize(Policy = AuthPolicies.Operational)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(UploadLimitBytes)]
    public async Task<IActionResult> CustomerErrorReport(IFormFile? file, CancellationToken cancellationToken)
    {
        var upload = RequireFile(file);

        var report = await customerImports.CreateErrorReportAsync(upload.OpenReadStream(), upload.FileName, cancellationToken);

        return ExcelFile(report, ErrorReportName(upload.FileName));
    }

    [HttpGet("inventory/{kind:regex(^(retail|salon-use)$)}/template")]
    [Authorize(Policy = AuthPolicies.OwnerOnly)]
    public IActionResult InventoryTemplate(string kind)
    {
        var type = InventoryType(kind);

        return ExcelFile(inventoryImports.CreateTemplate(type), ImportSheets.Describe(type).TemplateFileName);
    }

    [HttpPost("inventory/{kind:regex(^(retail|salon-use)$)}/preview")]
    [Authorize(Policy = AuthPolicies.OwnerOnly)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(UploadLimitBytes)]
    public async Task<IActionResult> PreviewInventory(string kind, IFormFile? file, CancellationToken cancellationToken)
    {
        var upload = RequireFile(file);

        return Ok(await inventoryImports.PreviewAsync(upload.OpenReadStream(), upload.FileName, InventoryType(kind), cancellationToken));
    }

    [HttpPost("inventory/{kind:regex(^(retail|salon-use)$)}")]
    [Authorize(Policy = AuthPolicies.OwnerOnly)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(UploadLimitBytes)]
    public async Task<IActionResult> ImportInventory(string kind, IFormFile? file, CancellationToken cancellationToken)
    {
        var upload = RequireFile(file);

        var result = await inventoryImports.ExecuteAsync(
            upload.OpenReadStream(),
            upload.FileName,
            InventoryType(kind),
            ActingUserId() ?? Guid.Empty,
            cancellationToken);

        return Ok(result);
    }

    [HttpPost("inventory/{kind:regex(^(retail|salon-use)$)}/error-report")]
    [Authorize(Policy = AuthPolicies.OwnerOnly)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(UploadLimitBytes)]
    public async Task<IActionResult> InventoryErrorReport(
        string kind,
        IFormFile? file,
        CancellationToken cancellationToken)
    {
        var upload = RequireFile(file);

        var report = await inventoryImports.CreateErrorReportAsync(
            upload.OpenReadStream(),
            upload.FileName,
            InventoryType(kind),
            cancellationToken);

        return ExcelFile(report, ErrorReportName(upload.FileName));
    }

    [HttpGet("services/template")]
    [Authorize(Policy = AuthPolicies.Operational)]
    public IActionResult ServiceTemplate() =>
        ExcelFile(serviceImports.CreateTemplate(), ImportSheets.Describe(ImportType.Services).TemplateFileName);

    [HttpPost("services/preview")]
    [Authorize(Policy = AuthPolicies.Operational)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(UploadLimitBytes)]
    public async Task<IActionResult> PreviewServices(IFormFile? file, CancellationToken cancellationToken)
    {
        var upload = RequireFile(file);
        using var stream = upload.OpenReadStream();
        return Ok(await serviceImports.PreviewAsync(stream, upload.FileName, cancellationToken));
    }

    [HttpPost("services")]
    [Authorize(Policy = AuthPolicies.Operational)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(UploadLimitBytes)]
    public async Task<IActionResult> ImportServices(IFormFile? file, CancellationToken cancellationToken)
    {
        var upload = RequireFile(file);
        using var stream = upload.OpenReadStream();
        return Ok(await serviceImports.ExecuteAsync(stream, upload.FileName, cancellationToken));
    }

    [HttpPost("services/error-report")]
    [Authorize(Policy = AuthPolicies.Operational)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(UploadLimitBytes)]
    public async Task<IActionResult> ServiceErrorReport(IFormFile? file, CancellationToken cancellationToken)
    {
        var upload = RequireFile(file);
        using var stream = upload.OpenReadStream();
        return ExcelFile(await serviceImports.CreateErrorReportAsync(stream, upload.FileName, cancellationToken),
            ErrorReportName(upload.FileName));
    }

    private static IFormFile RequireFile(IFormFile? file)
    {
        if (file is null || file.Length == 0)
        {
            throw new ValidationFailedException(new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["file"] = ["Choose an .xlsx file to import."]
            });
        }

        return file;
    }

    private IActionResult ExcelFile(byte[] content, string fileName) =>
        File(content, ImportRules.XlsxContentType, fileName);

    private static string ErrorReportName(string sourceName)
    {
        var withoutExtension = Path.GetFileNameWithoutExtension(sourceName);

        return $"{withoutExtension}{ImportRules.ErrorReportSuffix}";
    }

    private Guid? ActingUserId() =>
        Guid.TryParse(User.FindFirst("sub")?.Value, out var userId) ? userId : null;

    private static ImportType InventoryType(string kind) => kind == "retail"
        ? ImportType.RetailProducts
        : ImportType.SalonUseProducts;
}
