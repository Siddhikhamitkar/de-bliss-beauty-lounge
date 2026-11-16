using DeBliss.Application.Common.Exceptions;
using DeBliss.Application.Customers.Ports;
using DeBliss.Application.Identity.Dtos;

namespace DeBliss.Application.Identity;

/// <summary>
/// Read side of the customer's own account. Sign-up no longer lives here: an
/// account is created only by proving the mobile number with a one-time code
/// (<see cref="CustomerAccountService.ActivateAsync"/>), so nobody can open an
/// account on a number they do not own.
/// </summary>
public sealed class CustomerAccessService(ICustomerRepository customers) : ICustomerAccessService
{
    public async Task<CustomerSelfDto> GetProfileAsync(Guid customerId, CancellationToken cancellationToken)
    {
        var customer = await customers.GetByIdAsync(customerId, cancellationToken)
            ?? throw new NotFoundException($"Customer '{customerId}' was not found.");

        return CustomerAccountService.ToSelf(customer);
    }
}
