using DeBliss.Application.ServiceCatalogue;
using DeBliss.Application.ServiceCatalogue.Dtos;
using Microsoft.AspNetCore.Mvc;

using DeBliss.Api.Security;
using Microsoft.AspNetCore.Authorization;

namespace DeBliss.Api.Controllers;

/// <summary>
/// Service bundles on the service menu. Thin by design: every rule lives in
/// <see cref="IServiceBundleService"/>. The menu listing itself lives on
/// <see cref="ServicesController"/>, which merges bundles into the paged
/// service search.
/// </summary>
[ApiController]
[Route("api/service-bundles")]
[Produces("application/json")]
[Authorize(Policy = AuthPolicies.Operational)]
public sealed class ServiceBundlesController(IServiceBundleService bundles) : ControllerBase
{
    /// <summary>The bundles the booking flow may offer, with components in delivery sequence.</summary>
    [HttpGet("bookable")]
    [ProducesResponseType(typeof(IReadOnlyList<BundleDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<BundleDto>>> GetBookable(CancellationToken cancellationToken)
    {
        return Ok(await bundles.GetBookableAsync(cancellationToken));
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(BundleDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BundleDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        return Ok(await bundles.GetAsync(id, cancellationToken));
    }

    [HttpPost]
    [ProducesResponseType(typeof(BundleDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BundleDto>> Create(
        [FromBody] SaveBundleRequest request,
        CancellationToken cancellationToken)
    {
        var created = await bundles.CreateAsync(request, cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(BundleDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BundleDto>> Update(
        Guid id,
        [FromBody] SaveBundleRequest request,
        CancellationToken cancellationToken)
    {
        return Ok(await bundles.UpdateAsync(id, request, cancellationToken));
    }

    [HttpPatch("{id:guid}/status")]
    [ProducesResponseType(typeof(BundleDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BundleDto>> SetStatus(
        Guid id,
        [FromBody] SetStatusRequest request,
        CancellationToken cancellationToken)
    {
        return Ok(await bundles.SetStatusAsync(id, request.IsActive, cancellationToken));
    }
}
