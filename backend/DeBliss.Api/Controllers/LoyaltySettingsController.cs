using DeBliss.Api.Security;
using DeBliss.Application.Loyalty;
using DeBliss.Application.Loyalty.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DeBliss.Api.Controllers;

[ApiController]
[Route("api/loyalty/settings")]
[Produces("application/json")]
[Authorize(Policy = AuthPolicies.OwnerOnly)]
public sealed class LoyaltySettingsController(ILoyaltyService loyalty) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<LoyaltySettingsDto>> Get(CancellationToken cancellationToken) =>
        Ok(await loyalty.GetSettingsAsync(cancellationToken));

    [HttpPut]
    public async Task<ActionResult<LoyaltySettingsDto>> Update(
        [FromBody] SaveLoyaltySettingsRequest request, CancellationToken cancellationToken) =>
        Ok(await loyalty.UpdateSettingsAsync(request,
            Guid.TryParse(User.FindFirst("sub")?.Value, out var userId) ? userId : null,
            cancellationToken));
}
