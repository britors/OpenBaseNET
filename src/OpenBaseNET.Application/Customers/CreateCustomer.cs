using OpenBaseNET.Application.Ports;
using OpenBaseNET.Domain.Customers;

namespace OpenBaseNET.Application.Customers;

public sealed class CreateCustomer(ICustomerRepository repository, IUnitOfWork unitOfWork)
{
    public Task<CustomerResponse> ExecuteAsync(string name, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var customer = Customer.Create(name);

        return unitOfWork.ExecuteAsync(token =>
        {
            token.ThrowIfCancellationRequested();
            repository.Add(customer);
            return Task.FromResult(CustomerResponse.From(customer));
        }, cancellationToken);
    }
}
