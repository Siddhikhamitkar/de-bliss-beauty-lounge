using DeBliss.Application.Billing;
using DeBliss.Application.Billing.Dtos;
using DeBliss.Application.Common.Paging;
using Microsoft.AspNetCore.Mvc;

using DeBliss.Api.Security;
using Microsoft.AspNetCore.Authorization;

namespace DeBliss.Api.Controllers;

/// <summary>
/// The billing desk: the invoice list the receptionist searches, the checkout
/// that turns a completed appointment into a paid bill, and the printable
/// receipt. Thin by design: discount rules, coupon validation, payment rules
/// and the paid transition all live in <see cref="IInvoiceService"/>.
/// </summary>
[ApiController]
[Route("api/invoices")]
[Produces("application/json")]
[Authorize(Policy = AuthPolicies.Operational)]
public sealed class InvoicesController(IInvoiceService invoices) : ControllerBase
{
    [HttpPost("{id:guid}/checkout-preview")]
    public async Task<ActionResult<CheckoutPreviewDto>> PreviewCheckout(
        Guid id, [FromBody] CheckoutRequest request, CancellationToken cancellationToken) =>
        Ok(await invoices.PreviewCheckoutAsync(id, request, cancellationToken));

    /// <remarks>Will require <c>Permissions.Billing.ViewInvoices</c> once authorisation exists.</remarks>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<InvoiceSummaryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<InvoiceSummaryDto>>> Search(
        [FromQuery] string? search,
        [FromQuery] InvoiceStatusFilter status,
        [FromQuery] InvoicePaymentMethodFilter paymentMethod,
        [FromQuery] Guid? customerId,
        [FromQuery] int page,
        [FromQuery] int pageSize,
        CancellationToken cancellationToken)
    {
        var query = new InvoiceQuery(search, status, paymentMethod, customerId, page, pageSize);

        return Ok(await invoices.SearchAsync(query, cancellationToken));
    }

    /// <summary>
    /// Starts (or resumes) the checkout for a completed appointment. The
    /// appointment's services become the invoice lines automatically, so the
    /// receptionist never retypes what the customer booked. A request with no
    /// appointment starts a standalone retail sale.
    /// </summary>
    /// <remarks>Will require <c>Permissions.Billing.CreateCheckout</c> once authorisation exists.</remarks>
    [HttpPost]
    [ProducesResponseType(typeof(InvoiceDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(InvoiceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<InvoiceDto>> Start(
        [FromBody] CreateInvoiceRequest request,
        CancellationToken cancellationToken)
    {
        var result = await invoices.StartFromAppointmentAsync(request, cancellationToken);

        return result.WasCreated
            ? CreatedAtAction(nameof(GetById), new { id = result.Invoice.Id }, result.Invoice)
            : Ok(result.Invoice);
    }

    /// <summary>
    /// Adds a retail product to the bill. Product lines are the only lines a
    /// human adds by hand; service lines come from the booking.
    /// </summary>
    /// <remarks>Will require <c>Permissions.Billing.CreateCheckout</c> once authorisation exists.</remarks>
    [HttpPost("{id:guid}/lines")]
    [ProducesResponseType(typeof(InvoiceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<InvoiceDto>> AddLine(
        Guid id,
        [FromBody] AddInvoiceLineRequest request,
        CancellationToken cancellationToken)
    {
        return Ok(await invoices.AddLineAsync(id, request, cancellationToken));
    }

    /// <summary>Changes how many of a product the customer is buying. Product lines only.</summary>
    /// <remarks>Will require <c>Permissions.Billing.CreateCheckout</c> once authorisation exists.</remarks>
    [HttpPut("{id:guid}/lines/{lineId:guid}")]
    [ProducesResponseType(typeof(InvoiceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<InvoiceDto>> UpdateLine(
        Guid id,
        Guid lineId,
        [FromBody] UpdateInvoiceLineRequest request,
        CancellationToken cancellationToken)
    {
        return Ok(await invoices.UpdateLineAsync(id, lineId, request, cancellationToken));
    }

    /// <summary>Removes a product line from the bill. Service lines come from the booking and stay.</summary>
    /// <remarks>Will require <c>Permissions.Billing.CreateCheckout</c> once authorisation exists.</remarks>
    [HttpDelete("{id:guid}/lines/{lineId:guid}")]
    [ProducesResponseType(typeof(InvoiceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<InvoiceDto>> RemoveLine(
        Guid id,
        Guid lineId,
        CancellationToken cancellationToken)
    {
        return Ok(await invoices.RemoveLineAsync(id, lineId, cancellationToken));
    }

    /// <summary>
    /// Saves the receptionist's tip choice on the draft — a percentage or a
    /// keyed amount — so an interrupted checkout resumes with the tip already
    /// picked. The charged amount is recomputed at checkout, never trusted
    /// from the client.
    /// </summary>
    /// <remarks>Will require <c>Permissions.Billing.CreateCheckout</c> once authorisation exists.</remarks>
    [HttpPut("{id:guid}/tip")]
    [ProducesResponseType(typeof(InvoiceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<InvoiceDto>> SetTip(
        Guid id,
        [FromBody] SetTipRequest request,
        CancellationToken cancellationToken)
    {
        return Ok(await invoices.SetTipAsync(id, request, cancellationToken));
    }

    /// <remarks>Will require <c>Permissions.Billing.ViewInvoices</c> and <c>Permissions.Billing.ViewPayments</c> once authorisation exists.</remarks>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(InvoiceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<InvoiceDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        return Ok(await invoices.GetAsync(id, cancellationToken));
    }

    /// <summary>
    /// Applies the coupon and manual discount, records the payment, deducts the
    /// sold stock and marks the invoice paid. One save: an invoice is never
    /// left half checked out, and stock never leaves for a sale that failed.
    /// </summary>
    /// <remarks>Will require <c>Permissions.Billing.CreateCheckout</c> and <c>Permissions.Billing.ApplyManualDiscount</c> once authorisation exists.</remarks>
    [HttpPost("{id:guid}/checkout")]
    [ProducesResponseType(typeof(ReceiptDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ReceiptDto>> Checkout(
        Guid id,
        [FromBody] CheckoutRequest request,
        CancellationToken cancellationToken)
    {
        return Ok(await invoices.CheckoutAsync(id, request, ActingUserId(), cancellationToken));
    }

    /// <summary>The printable receipt for a paid invoice, built from the stored amounts.
    /// Reprinting months later shows exactly what was charged on the day.</summary>
    /// <remarks>Will require <c>Permissions.Billing.ViewInvoices</c> once authorisation exists.</remarks>
    [HttpGet("{id:guid}/receipt")]
    [ProducesResponseType(typeof(ReceiptDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ReceiptDto>> GetReceipt(Guid id, CancellationToken cancellationToken)
    {
        return Ok(await invoices.GetReceiptAsync(id, cancellationToken));
    }

    /// <summary>The signed-in login, so stock movements record who sold what. Null only for non-JWT callers, which the policy excludes anyway.</summary>
    private Guid? ActingUserId() =>
        Guid.TryParse(User.FindFirst("sub")?.Value, out var userId) ? userId : null;
}
