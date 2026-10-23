using DeBliss.Application.Billing.Dtos;
using DeBliss.Application.Common.Paging;

namespace DeBliss.Application.Billing;

public interface IInvoiceService
{
    Task<PagedResult<InvoiceSummaryDto>> SearchAsync(InvoiceQuery query, CancellationToken cancellationToken = default);

    Task<InvoiceDto> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Raises (or resumes) the open invoice for a completed appointment. The
    /// appointment's services become the invoice lines at their booking-time
    /// prices, so the receptionist never retypes what the customer booked. A
    /// request without an appointment starts a standalone retail-sale draft the
    /// receptionist adds product lines to.
    /// </summary>
    Task<InvoiceStartResult> StartFromAppointmentAsync(
        CreateInvoiceRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a retail product line to a draft invoice at the catalogue price,
    /// merging into the existing line for the same product rather than raising
    /// a second one.
    /// </summary>
    Task<InvoiceDto> AddLineAsync(
        Guid id,
        AddInvoiceLineRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Changes the quantity of a product line on a draft invoice.</summary>
    Task<InvoiceDto> UpdateLineAsync(
        Guid id,
        Guid lineId,
        UpdateInvoiceLineRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Removes a product line from a draft invoice. Service lines come from the booking and stay.</summary>
    Task<InvoiceDto> RemoveLineAsync(
        Guid id,
        Guid lineId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves the receptionist's tip choice on a draft invoice so an interrupted
    /// checkout resumes with the tip already picked. The choice is a percentage
    /// or a keyed amount; the charged amount itself is computed fresh at
    /// checkout, when the discounts and tax it depends on are final.
    /// </summary>
    Task<InvoiceDto> SetTipAsync(
        Guid id,
        SetTipRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies the discounts, records the payment, deducts the sold stock and
    /// marks the invoice paid — all in one save, so an invoice is never left
    /// half checked out and stock is never taken for a sale that did not go
    /// through.
    /// </summary>
    Task<ReceiptDto> CheckoutAsync(
        Guid id,
        CheckoutRequest request,
        Guid? actorUserId = null,
        CancellationToken cancellationToken = default);

    /// <summary>The printable receipt for a paid invoice, built from the stored amounts.</summary>
    Task<ReceiptDto> GetReceiptAsync(Guid id, CancellationToken cancellationToken = default);

    Task<CheckoutPreviewDto> PreviewCheckoutAsync(Guid id, CheckoutRequest request, CancellationToken cancellationToken = default);
}
