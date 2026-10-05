using DeBliss.Application.Common.Paging;
using DeBliss.Application.Customers.Dtos;

namespace DeBliss.Application.Customers;

public interface ICustomerService
{
    Task<PagedResult<CustomerSummaryDto>> SearchAsync(CustomerQuery query, CancellationToken cancellationToken = default);

    Task<CustomerDto> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<CustomerDto> CreateAsync(SaveCustomerRequest request, CancellationToken cancellationToken = default);

    Task<CustomerDto> UpdateAsync(Guid id, SaveCustomerRequest request, CancellationToken cancellationToken = default);

    Task<CustomerDto> SetStatusAsync(Guid id, bool isActive, CancellationToken cancellationToken = default);

    Task<CustomerDto> SetWhatsAppOptInAsync(Guid id, string? status, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CustomerNoteDto>> ListNotesAsync(Guid id, CancellationToken cancellationToken = default);

    Task<CustomerNoteDto> AddNoteAsync(Guid id, SaveCustomerNoteRequest request, CancellationToken cancellationToken = default);

    Task<CustomerNoteDto> UpdateNoteAsync(Guid id, Guid noteId, SaveCustomerNoteRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CustomerAppointmentDto>> ListAppointmentsAsync(Guid id, CancellationToken cancellationToken = default);
}
