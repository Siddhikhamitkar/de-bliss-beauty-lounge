using DeBliss.Application.Common.Paging;
using DeBliss.Application.ServiceCatalogue;
using DeBliss.Application.ServiceCatalogue.Dtos;
using Microsoft.AspNetCore.Mvc;

using DeBliss.Api.Security;
using Microsoft.AspNetCore.Authorization;

namespace DeBliss.Api.Controllers;

/// <summary>
/// The bookable service menu. Thin by design: every rule lives in
/// <see cref="IServiceCatalogueService"/>.
/// </summary>
[ApiController]
[Route("api/services")]
[Produces("application/json")]
[Authorize(Policy = AuthPolicies.Operational)]
public sealed class ServicesController(IServiceCatalogueService catalogue) : ControllerBase
{
    /// <remarks>Will require <c>Permissions.ServiceCatalogue.ViewServices</c> once authorisation exists.</remarks>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<ServiceSummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<ServiceSummaryDto>>> Search(
        [FromQuery] string? search,
        [FromQuery] Guid? categoryId,
        [FromQuery] ServiceStatusFilter status,
        [FromQuery] bool bookableOnly,
        [FromQuery] int page,
        [FromQuery] int pageSize,
        CancellationToken cancellationToken)
    {
        var query = new ServiceQuery(search, categoryId, status, bookableOnly, page, pageSize);

        return Ok(await catalogue.SearchAsync(query, cancellationToken));
    }

    /// <summary>
    /// The subset the future appointment calendar offers to a booker: active
    /// services in active categories with at least one active staff member able to
    /// perform them.
    /// </summary>
    /// <remarks>Will require <c>Permissions.ServiceCatalogue.ViewServices</c> once authorisation exists.</remarks>
    [HttpGet("bookable")]
    [ProducesResponseType(typeof(IReadOnlyList<ServiceDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ServiceDto>>> GetBookable(CancellationToken cancellationToken)
    {
        return Ok(await catalogue.GetBookableAsync(cancellationToken));
    }

    /// <remarks>Will require <c>Permissions.ServiceCatalogue.ViewServices</c> once authorisation exists.</remarks>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ServiceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ServiceDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        return Ok(await catalogue.GetAsync(id, cancellationToken));
    }

    /// <remarks>Will require <c>Permissions.ServiceCatalogue.CreateService</c> and
    /// <c>AssignStaffToServices</c> once authorisation exists.</remarks>
    [HttpPost]
    [ProducesResponseType(typeof(ServiceDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ServiceDto>> Create(
        [FromBody] SaveServiceRequest request,
        CancellationToken cancellationToken)
    {
        var created = await catalogue.CreateAsync(request, cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    /// <remarks>Will require <c>Permissions.ServiceCatalogue.EditService</c> and
    /// <c>AssignStaffToServices</c> once authorisation exists.</remarks>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(ServiceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ServiceDto>> Update(
        Guid id,
        [FromBody] SaveServiceRequest request,
        CancellationToken cancellationToken)
    {
        return Ok(await catalogue.UpdateAsync(id, request, cancellationToken));
    }

    /// <remarks>Will require <c>Permissions.ServiceCatalogue.ChangeServiceStatus</c> once authorisation exists.</remarks>
    [HttpPatch("{id:guid}/status")]
    [ProducesResponseType(typeof(ServiceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ServiceDto>> SetStatus(
        Guid id,
        [FromBody] SetStatusRequest request,
        CancellationToken cancellationToken)
    {
        return Ok(await catalogue.SetStatusAsync(id, request.IsActive, cancellationToken));
    }
}
