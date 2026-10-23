using DeBliss.Api.Security;
using DeBliss.Application.Common.Paging;
using DeBliss.Application.Identity;
using DeBliss.Application.Staffing;
using DeBliss.Application.Staffing.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DeBliss.Api.Controllers;

[ApiController]
[Route("api/staff/me/salary-slips")]
[Produces("application/json")]
[Authorize(Policy = AuthPolicies.StaffSelf)]
[Authorize(Roles = RoleNames.Staff)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class StaffSalarySlipsController(ISalarySlipService salarySlips) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<StaffSalarySlipSummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PagedResult<StaffSalarySlipSummaryDto>>> GetPaged(
        [FromQuery] int page,
        [FromQuery] int pageSize,
        CancellationToken cancellationToken)
    {
        var staffId = StaffSelfIdentity.Resolve(User);
        if (staffId == Guid.Empty)
        {
            return Forbid();
        }

        return Ok(await salarySlips.GetForStaffAsync(staffId, page, pageSize, cancellationToken));
    }

    [HttpGet("{id:guid}/pdf")]
    [Produces("application/pdf")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadPdf(Guid id, CancellationToken cancellationToken)
    {
        var staffId = StaffSelfIdentity.Resolve(User);
        if (staffId == Guid.Empty)
        {
            return Forbid();
        }

        var (slip, pdf) = await salarySlips.GetPdfForStaffAsync(staffId, id, cancellationToken);

        return File(pdf, "application/pdf", $"salary-slip-{slip.SalaryYear:0000}-{slip.SalaryMonth:00}-{slip.Id}.pdf");
    }
}
