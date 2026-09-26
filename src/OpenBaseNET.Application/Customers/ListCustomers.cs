using OpenBaseNET.Application.Ports;

namespace OpenBaseNET.Application.Customers;

public sealed class ListCustomers(ICustomerQueries queries)
{
    public async Task<IReadOnlyList<CustomerResponse>> ExecuteAsync(
        int pageNumber = 1, int pageSize = CustomerPage.DefaultSize, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var page = new CustomerPage(pageNumber, pageSize);
        var customers = await queries.ListAsync(page, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return customers;
    }
}
