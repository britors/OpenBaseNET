using OpenBaseNET.Application.Customers;

namespace OpenBaseNET.Application.Ports;

/// <summary>Read-only projections; no change tracking or write transaction is required.</summary>
public interface ICustomerQueries
{
    Task<CustomerResponse?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Orders by Name ASC, then unique Id ASC before applying offset/limit.
    /// Returns an empty collection past the end; never returns null.
    /// </summary>
    Task<IReadOnlyList<CustomerResponse>> ListAsync(CustomerPage page, CancellationToken cancellationToken);
}
