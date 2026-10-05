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
    /// receptionist never retypes what the customer booked.
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

    /// <remarks>Will require <c>Permissions.Billing.ViewInvoices</c> and <c>Permissions.Billing.ViewPayments</c> once authorisation exists.</remarks>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(InvoiceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<InvoiceDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        return Ok(await invoices.GetAsync(id, cancellationToken));
    }

    /// <summary>
    /// Applies the coupon and manual discount, records the payment and marks
    /// the invoice paid. One save: an invoice is never left half checked out.
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
        return Ok(await invoices.CheckoutAsync(id, request, cancellationToken));
    }

    /// <summary>
    /// The printable receipt for a paid invoice, built from the stored amounts
    /// — reprinting months later shows exactly what was charged on the day.
    /// </summary>
    /// <remarks>Will require <c>Permissions.Billing.ViewInvoices</c> once authorisation exists.</remarks>
    [HttpGet("{id:guid}/receipt")]
    [ProducesResponseType(typeof(ReceiptDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ReceiptDto>> GetReceipt(Guid id, CancellationToken cancellationToken)
    {
        return Ok(await invoices.GetReceiptAsync(id, cancellationToken));
    }
}
