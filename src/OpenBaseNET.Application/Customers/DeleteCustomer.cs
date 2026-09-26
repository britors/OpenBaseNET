using OpenBaseNET.Application.Ports;

namespace OpenBaseNET.Application.Customers;

public sealed class DeleteCustomer(ICustomerRepository repository, IUnitOfWork unitOfWork)
{
    public async Task ExecuteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CustomerId.Validate(id);

        await unitOfWork.ExecuteAsync(async token =>
        {
            token.ThrowIfCancellationRequested();
            var customer = await repository.GetAsync(id, token);
            token.ThrowIfCancellationRequested();
            if (customer is null) throw new CustomerNotFoundException(id);
            repository.Remove(customer);
            return true;
        }, cancellationToken);
    }
}
