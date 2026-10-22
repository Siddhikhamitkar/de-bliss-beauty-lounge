using DeBliss.Application.Settings;
using DeBliss.Application.Settings.Dtos;
using Microsoft.AspNetCore.Mvc;

using DeBliss.Api.Security;
using Microsoft.AspNetCore.Authorization;

namespace DeBliss.Api.Controllers;

/// <summary>
/// The salon's tax configuration. Reading it is part of running the desk — the
/// checkout screen needs the live rate to preview what is owed — so both roles
/// may; changing it is the owner's alone. Nothing here computes a bill: the
/// checkout snapshots the rate it charged onto each invoice it raises.
/// </summary>
[ApiController]
[Route("api/settings/tax")]
[Produces("application/json")]
[Authorize(Policy = AuthPolicies.Operational)]
public sealed class TaxSettingsController(ISettingsService settings) : ControllerBase
{
    /// <remarks>Will require <c>Permissions.Settings.ViewTaxSettings</c> once authorisation exists.</remarks>
    [HttpGet]
    [ProducesResponseType(typeof(TaxSettingsDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<TaxSettingsDto>> Get(CancellationToken cancellationToken)
    {
        return Ok(await settings.GetTaxAsync(cancellationToken));
    }

    /// <remarks>Will require <c>Permissions.Settings.ManageTaxSettings</c> once authorisation exists.</remarks>
    [HttpPut]
    [Authorize(Policy = AuthPolicies.OwnerOnly)]
    [ProducesResponseType(typeof(TaxSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<TaxSettingsDto>> Update(
        [FromBody] SaveTaxSettingsRequest request,
        CancellationToken cancellationToken)
    {
        return Ok(await settings.UpdateTaxAsync(request, cancellationToken));
    }
}
