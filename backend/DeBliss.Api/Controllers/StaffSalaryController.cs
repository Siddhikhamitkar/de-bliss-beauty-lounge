using DeBliss.Api.Security;
using DeBliss.Application.Staffing;
using DeBliss.Application.Staffing.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DeBliss.Api.Controllers;

[ApiController]
[Route("api/staff/{id:guid}/salary")]
[Produces("application/json")]
[Authorize(Policy = AuthPolicies.OwnerOnly)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class StaffSalaryController(ISalarySlipService salarySlips) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(StaffSalaryDetails), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<StaffSalaryDetails>> Get(Guid id, CancellationToken cancellationToken)
    {
        return Ok(await salarySlips.GetStaffSalaryAsync(id, cancellationToken));
    }
}
