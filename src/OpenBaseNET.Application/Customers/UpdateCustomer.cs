using OpenBaseNET.Application.Ports;
using OpenBaseNET.Domain.Customers;

namespace OpenBaseNET.Application.Customers;

public sealed class UpdateCustomer(ICustomerRepository repository, IUnitOfWork unitOfWork)
{
    public Task<CustomerResponse> ExecuteAsync(Guid id, string name, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CustomerId.Validate(id);
        var normalizedName = Customer.ValidateName(name);

        return unitOfWork.ExecuteAsync(async token =>
        {
            token.ThrowIfCancellationRequested();
            var customer = await repository.GetAsync(id, token);
            token.ThrowIfCancellationRequested();
            if (customer is null) throw new CustomerNotFoundException(id);
            customer.Rename(normalizedName);
            return CustomerResponse.From(customer);
        }, cancellationToken);
    }
}
