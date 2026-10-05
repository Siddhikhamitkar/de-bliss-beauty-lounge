using DeBliss.Application.ServiceCatalogue;
using DeBliss.Application.ServiceCatalogue.Dtos;
using Microsoft.AspNetCore.Mvc;

using DeBliss.Api.Security;
using Microsoft.AspNetCore.Authorization;

namespace DeBliss.Api.Controllers;

/// <summary>
/// Service menu categories. Thin by design: every rule lives in
/// <see cref="IServiceCategoryService"/>.
/// </summary>
[ApiController]
[Route("api/service-categories")]
[Produces("application/json")]
[Authorize(Policy = AuthPolicies.Operational)]
public sealed class ServiceCategoriesController(IServiceCategoryService serviceCategories) : ControllerBase
{
    /// <remarks>Will require <c>Permissions.ServiceCatalogue.ManageCategories</c> once authorisation exists.</remarks>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ServiceCategoryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ServiceCategoryDto>>> Get(
        [FromQuery] ServiceStatusFilter status,
        CancellationToken cancellationToken)
    {
        return Ok(await serviceCategories.GetCategoriesAsync(status, cancellationToken));
    }

    /// <remarks>Will require <c>Permissions.ServiceCatalogue.ManageCategories</c> once authorisation exists.</remarks>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ServiceCategoryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ServiceCategoryDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        return Ok(await serviceCategories.GetCategoryAsync(id, cancellationToken));
    }

    /// <remarks>Will require <c>Permissions.ServiceCatalogue.ManageCategories</c> once authorisation exists.</remarks>
    [HttpPost]
    [ProducesResponseType(typeof(ServiceCategoryDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ServiceCategoryDto>> Create(
        [FromBody] SaveServiceCategoryRequest request,
        CancellationToken cancellationToken)
    {
        var created = await serviceCategories.CreateAsync(request, cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    /// <remarks>Will require <c>Permissions.ServiceCatalogue.ManageCategories</c> once authorisation exists.</remarks>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(ServiceCategoryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ServiceCategoryDto>> Update(
        Guid id,
        [FromBody] SaveServiceCategoryRequest request,
        CancellationToken cancellationToken)
    {
        return Ok(await serviceCategories.UpdateAsync(id, request, cancellationToken));
    }

    /// <remarks>Will require <c>Permissions.ServiceCatalogue.ManageCategories</c> once authorisation exists.</remarks>
    [HttpPatch("{id:guid}/status")]
    [ProducesResponseType(typeof(ServiceCategoryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ServiceCategoryDto>> SetStatus(
        Guid id,
        [FromBody] SetStatusRequest request,
        CancellationToken cancellationToken)
    {
        return Ok(await serviceCategories.SetStatusAsync(id, request.IsActive, cancellationToken));
    }
}
