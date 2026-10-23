using System.Security.Claims;
using DeBliss.Api.Security;
using DeBliss.Application.Common.Paging;
using DeBliss.Application.Staffing;
using DeBliss.Application.Staffing.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DeBliss.Api.Controllers;

[ApiController]
[Route("api/salary-slips")]
[Produces("application/json")]
[Authorize(Policy = AuthPolicies.OwnerOnly)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class SalarySlipsController(ISalarySlipService salarySlips) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<SalarySlipSummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<SalarySlipSummaryDto>>> GetPaged(
        [FromQuery] Guid? staffId,
        [FromQuery] int? year,
        [FromQuery] int page,
        [FromQuery] int pageSize,
        CancellationToken cancellationToken)
    {
        return Ok(await salarySlips.GetPagedAsync(staffId, year, page, pageSize, cancellationToken));
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(SalarySlipDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SalarySlipDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        return Ok(await salarySlips.GetAsync(id, cancellationToken));
    }

    [HttpPost("preview")]
    [ProducesResponseType(typeof(SalarySlipPreview), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SalarySlipPreview>> Preview(
        [FromBody] SalarySlipRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetCreator(out var createdBy))
        {
            return Unauthorized();
        }

        return Ok(await salarySlips.PreviewAsync(request, createdBy, cancellationToken));
    }

    [HttpPost]
    [ProducesResponseType(typeof(SalarySlipDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SalarySlipDto>> Save(
        [FromBody] SaveSalarySlipRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetCreator(out var createdBy))
        {
            return Unauthorized();
        }

        var slip = await salarySlips.SaveAsync(request, createdBy, cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = slip.Id }, slip);
    }

    [HttpGet("{id:guid}/pdf")]
    [Produces("application/pdf")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadPdf(Guid id, CancellationToken cancellationToken)
    {
        var (slip, pdf) = await salarySlips.GetPdfAsync(id, cancellationToken);

        return File(pdf, "application/pdf", $"salary-slip-{slip.SalaryYear}-{slip.SalaryMonth:00}-{slip.Id}.pdf");
    }

    private bool TryGetCreator(out Guid createdBy)
    {
        createdBy = Guid.Empty;

        return User.Identity?.IsAuthenticated == true
            && Guid.TryParse(User.FindFirstValue("sub"), out createdBy)
            && createdBy != Guid.Empty;
    }
}
