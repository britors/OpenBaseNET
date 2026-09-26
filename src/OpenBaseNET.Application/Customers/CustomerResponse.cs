using OpenBaseNET.Domain.Customers;

namespace OpenBaseNET.Application.Customers;

public sealed record CustomerResponse(Guid Id, string Name)
{
    internal static CustomerResponse From(Customer customer) => new(customer.Id, customer.Name);
}
