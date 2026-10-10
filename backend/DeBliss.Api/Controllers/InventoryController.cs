using DeBliss.Api.Security;
using DeBliss.Application.Common.Paging;
using DeBliss.Application.Inventory;
using DeBliss.Application.Inventory.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DeBliss.Api.Controllers;

/// <summary>
/// The inventory surface. Reads — the stock list, product details, movement
/// history and the sellable catalogue the checkout picks from — are operational:
/// the front desk needs them to sell and to answer "do we have any left".
/// Every write is owner-only, enforced by policy here so a receptionist calling
/// the API directly receives 403, not just a hidden button. Thin by design:
/// product rules, stock arithmetic and the append-only ledger live in
/// <see cref="IInventoryService"/>.
/// </summary>
[ApiController]
[Route("api/inventory")]
[Produces("application/json")]
[Authorize(Policy = AuthPolicies.Operational)]
public sealed class InventoryController(IInventoryService inventory) : ControllerBase
{
    /// <remarks>Will require <c>Permissions.Inventory.ViewInventory</c> once authorisation exists.</remarks>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<InventoryProductDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<InventoryProductDto>>> Search(
        [FromQuery] string? search,
        [FromQuery] InventoryTypeFilter type,
        [FromQuery] InventoryProductStatusFilter status,
        [FromQuery] InventoryStockStatusFilter stockStatus,
        [FromQuery] string? category,
        [FromQuery] int page,
        [FromQuery] int pageSize,
        CancellationToken cancellationToken)
    {
        var query = new InventoryQuery(search, type, status, stockStatus, category, page, pageSize);

        return Ok(await inventory.SearchAsync(query, cancellationToken));
    }

    /// <summary>
    /// The active retail products the checkout screen offers, with their price
    /// and the stock actually on the shelf.
    /// </summary>
    /// <remarks>Will require <c>Permissions.Inventory.ViewInventory</c> once authorisation exists.</remarks>
    [HttpGet("sellable")]
    [ProducesResponseType(typeof(IReadOnlyList<InventoryProductOptionDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<InventoryProductOptionDto>>> GetSellable(
        [FromQuery] string? search,
        CancellationToken cancellationToken)
    {
        return Ok(await inventory.GetSellableAsync(search, cancellationToken));
    }

    /// <remarks>Will require <c>Permissions.Inventory.ViewInventory</c> once authorisation exists.</remarks>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(InventoryProductDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<InventoryProductDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        return Ok(await inventory.GetAsync(id, cancellationToken));
    }

    /// <summary>The product's append-only stock history, newest first.</summary>
    /// <remarks>Will require <c>Permissions.Inventory.ViewInventory</c> once authorisation exists.</remarks>
    [HttpGet("{id:guid}/movements")]
    [ProducesResponseType(typeof(PagedResult<StockMovementDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PagedResult<StockMovementDto>>> GetMovements(
        Guid id,
        [FromQuery] StockMovementTypeFilter type,
        [FromQuery] int page,
        [FromQuery] int pageSize,
        CancellationToken cancellationToken)
    {
        var query = new InventoryMovementQuery(id, type, page, pageSize);

        return Ok(await inventory.GetMovementsAsync(id, query, cancellationToken));
    }

    /// <remarks>Will require <c>Permissions.Inventory.ManageInventory</c> once authorisation exists.</remarks>
    [HttpPost]
    [Authorize(Policy = AuthPolicies.OwnerOnly)]
    [ProducesResponseType(typeof(InventoryProductDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<InventoryProductDto>> Create(
        [FromBody] SaveProductRequest request,
        CancellationToken cancellationToken)
    {
        var created = await inventory.CreateAsync(request, cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    /// <summary>Edits the product's fields. Stock itself changes only through movements, never here.</summary>
    /// <remarks>Will require <c>Permissions.Inventory.ManageInventory</c> once authorisation exists.</remarks>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = AuthPolicies.OwnerOnly)]
    [ProducesResponseType(typeof(InventoryProductDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<InventoryProductDto>> Update(
        Guid id,
        [FromBody] SaveProductRequest request,
        CancellationToken cancellationToken)
    {
        return Ok(await inventory.UpdateAsync(id, request, cancellationToken));
    }

    /// <summary>Retires a product or puts it back. History is kept either way.</summary>
    /// <remarks>Will require <c>Permissions.Inventory.ManageInventory</c> once authorisation exists.</remarks>
    [HttpPatch("{id:guid}/status")]
    [Authorize(Policy = AuthPolicies.OwnerOnly)]
    [ProducesResponseType(typeof(InventoryProductDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<InventoryProductDto>> SetStatus(
        Guid id,
        [FromBody] SetProductStatusRequest request,
        CancellationToken cancellationToken)
    {
        return Ok(await inventory.SetStatusAsync(id, request.IsActive, cancellationToken));
    }

    /// <summary>
    /// Records stock arriving (or an explicit opening count). Each call is its
    /// own movement; history is never overwritten.
    /// </summary>
    /// <remarks>Will require <c>Permissions.Inventory.ManageInventory</c> once authorisation exists.</remarks>
    [HttpPost("{id:guid}/stock")]
    [Authorize(Policy = AuthPolicies.OwnerOnly)]
    [ProducesResponseType(typeof(StockMovementDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<StockMovementDto>> ReceiveStock(
        Guid id,
        [FromBody] ReceiveStockRequest request,
        CancellationToken cancellationToken)
    {
        var movement = await inventory.ReceiveStockAsync(id, request, ActingUserId(), cancellationToken);

        return CreatedAtAction(
            nameof(GetMovements), new { id }, movement);
    }

    /// <summary>Records salon-use stock consumed in service or lost to wastage, with the reason.</summary>
    /// <remarks>Will require <c>Permissions.Inventory.ManageInventory</c> once authorisation exists.</remarks>
    [HttpPost("{id:guid}/usage")]
    [Authorize(Policy = AuthPolicies.OwnerOnly)]
    [ProducesResponseType(typeof(StockMovementDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<StockMovementDto>> RecordUsage(
        Guid id,
        [FromBody] RecordUsageRequest request,
        CancellationToken cancellationToken)
    {
        var movement = await inventory.RecordUsageAsync(id, request, ActingUserId(), cancellationToken);

        return CreatedAtAction(
            nameof(GetMovements), new { id }, movement);
    }

    /// <summary>Applies a signed stock correction, such as reconciling to a physical count.</summary>
    /// <remarks>Will require <c>Permissions.Inventory.ManageInventory</c> once authorisation exists.</remarks>
    [HttpPost("{id:guid}/adjustments")]
    [Authorize(Policy = AuthPolicies.OwnerOnly)]
    [ProducesResponseType(typeof(StockMovementDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<StockMovementDto>> AdjustStock(
        Guid id,
        [FromBody] AdjustStockRequest request,
        CancellationToken cancellationToken)
    {
        var movement = await inventory.AdjustStockAsync(id, request, ActingUserId(), cancellationToken);

        return CreatedAtAction(
            nameof(GetMovements), new { id }, movement);
    }

    /// <summary>
    /// Undoes a mistaken movement with an audited reversal. The original row
    /// stays exactly as it was; each movement can be reversed once, ever.
    /// </summary>
    /// <remarks>Will require <c>Permissions.Inventory.ManageInventory</c> once authorisation exists.</remarks>
    [HttpPost("{id:guid}/movements/{movementId:guid}/reversal")]
    [Authorize(Policy = AuthPolicies.OwnerOnly)]
    [ProducesResponseType(typeof(StockMovementDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<StockMovementDto>> ReverseMovement(
        Guid id,
        Guid movementId,
        [FromBody] ReverseStockMovementRequest request,
        CancellationToken cancellationToken)
    {
        var movement = await inventory.ReverseMovementAsync(
            id, movementId, request, ActingUserId(), cancellationToken);

        return CreatedAtAction(
            nameof(GetMovements), new { id }, movement);
    }

    /// <summary>The signed-in login, so the ledger records who moved what. Null only for non-JWT callers, which the policy excludes anyway.</summary>
    private Guid? ActingUserId() =>
        Guid.TryParse(User.FindFirst("sub")?.Value, out var userId) ? userId : null;
}
