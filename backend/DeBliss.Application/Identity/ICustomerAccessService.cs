using DeBliss.Application.Identity.Dtos;

namespace DeBliss.Application.Identity;

public interface ICustomerAccessService
{
    Task<CustomerSelfDto> GetProfileAsync(Guid customerId, CancellationToken cancellationToken);
}
