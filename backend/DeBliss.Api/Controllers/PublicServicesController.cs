using DeBliss.Application.ServiceCatalogue;
using DeBliss.Application.ServiceCatalogue.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DeBliss.Api.Controllers;

/// <summary>
/// The customer-facing service menu. Deliberately the only anonymous, read-only
/// surface on the API: it exposes exactly what the public website may show and
/// nothing else.
/// </summary>
[ApiController]
[Route("api/public/services")]
[Produces("application/json")]
[AllowAnonymous]
public sealed class PublicServicesController(IServiceCatalogueService catalogue) : ControllerBase
{
    /// <summary>
    /// The services a customer may book online right now: active, switched on
    /// for online booking, in an active category.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<PublicServiceDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<PublicServiceDto>>> GetCatalogue(CancellationToken cancellationToken)
    {
        return Ok(await catalogue.GetPublicCatalogueAsync(cancellationToken));
    }
}
