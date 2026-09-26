using OpenBaseNET.Application.Ports;

namespace OpenBaseNET.Application.Customers;

public sealed class GetCustomer(ICustomerQueries queries)
{
    public async Task<CustomerResponse> ExecuteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CustomerId.Validate(id);
        var customer = await queries.GetAsync(id, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return customer ?? throw new CustomerNotFoundException(id);
    }
}
