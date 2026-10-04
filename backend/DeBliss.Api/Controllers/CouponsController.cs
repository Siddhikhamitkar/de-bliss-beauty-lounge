using DeBliss.Application.Billing;
using DeBliss.Application.Billing.Dtos;
using DeBliss.Application.Common.Paging;
using Microsoft.AspNetCore.Mvc;

namespace DeBliss.Api.Controllers;

/// <summary>
/// Coupon management for the billing desk: the code list, the create/edit
/// form, the activate/deactivate toggle and the preview the checkout screen
/// calls before applying a code. Thin by design: code normalisation, duplicate
/// detection and validity rules all live in <see cref="ICouponService"/>.
/// </summary>
[ApiController]
[Route("api/coupons")]
[Produces("application/json")]
public sealed class CouponsController(ICouponService coupons) : ControllerBase
{
    /// <remarks>Will require <c>Permissions.Billing.ManageCoupons</c> once authorisation exists.</remarks>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<CouponDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<CouponDto>>> Search(
        [FromQuery] string? search,
        [FromQuery] CouponStatusFilter status,
        [FromQuery] int page,
        [FromQuery] int pageSize,
        CancellationToken cancellationToken)
    {
        var query = new CouponQuery(search, status, page, pageSize);

        return Ok(await coupons.SearchAsync(query, cancellationToken));
    }

    /// <remarks>Will require <c>Permissions.Billing.ManageCoupons</c> once authorisation exists.</remarks>
    [HttpPost]
    [ProducesResponseType(typeof(CouponDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CouponDto>> Create(
        [FromBody] SaveCouponRequest request,
        CancellationToken cancellationToken)
    {
        var created = await coupons.CreateAsync(request, cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    /// <remarks>Will require <c>Permissions.Billing.ManageCoupons</c> once authorisation exists.</remarks>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(CouponDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CouponDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        return Ok(await coupons.GetAsync(id, cancellationToken));
    }

    /// <remarks>Will require <c>Permissions.Billing.ManageCoupons</c> once authorisation exists.</remarks>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(CouponDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CouponDto>> Update(
        Guid id,
        [FromBody] SaveCouponRequest request,
        CancellationToken cancellationToken)
    {
        return Ok(await coupons.UpdateAsync(id, request, cancellationToken));
    }

    /// <remarks>Will require <c>Permissions.Billing.ManageCoupons</c> once authorisation exists.</remarks>
    [HttpPatch("{id:guid}/status")]
    [ProducesResponseType(typeof(CouponDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CouponDto>> SetStatus(
        Guid id,
        [FromBody] SetCouponStatusRequest request,
        CancellationToken cancellationToken)
    {
        return Ok(await coupons.SetStatusAsync(id, request.IsActive, cancellationToken));
    }

    /// <summary>
    /// Checks a code against the stored coupons and previews its effect on a
    /// subtotal. The checkout screen calls this when the receptionist presses
    /// Apply; the rules live here, never in the browser.
    /// </summary>
    /// <remarks>Will require <c>Permissions.Billing.CreateCheckout</c> once authorisation exists.</remarks>
    [HttpPost("validate")]
    [ProducesResponseType(typeof(CouponValidationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CouponValidationDto>> Validate(
        [FromBody] ValidateCouponRequest request,
        CancellationToken cancellationToken)
    {
        return Ok(await coupons.ValidateAsync(request, cancellationToken));
    }
}
