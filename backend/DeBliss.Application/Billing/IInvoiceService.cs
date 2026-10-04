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
    /// prices, so the receptionist never retypes what the customer booked.
    /// </summary>
    Task<InvoiceStartResult> StartFromAppointmentAsync(
        CreateInvoiceRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Applies the discounts, records the payment and marks the invoice paid, atomically.</summary>
    Task<ReceiptDto> CheckoutAsync(Guid id, CheckoutRequest request, CancellationToken cancellationToken = default);

    /// <summary>The printable receipt for a paid invoice, built from the stored amounts.</summary>
    Task<ReceiptDto> GetReceiptAsync(Guid id, CancellationToken cancellationToken = default);
}
