using OpenBaseNET.Domain.Customers;

namespace OpenBaseNET.Application.Ports;

/// <summary>
/// Writes participate in the scoped unit of work. Add/Remove only stage changes;
/// GetAsync returns an entity whose changes are persisted at commit. Never saves independently.
/// </summary>
public interface ICustomerRepository
{
    void Add(Customer customer);
    Task<Customer?> GetAsync(Guid id, CancellationToken cancellationToken);
    void Remove(Customer customer);
}
