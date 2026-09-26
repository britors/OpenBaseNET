using Microsoft.EntityFrameworkCore;
using OpenBaseNET.Application.Ports;
using OpenBaseNET.Domain.Customers;

namespace OpenBaseNET.Infrastructure.Persistence;

internal sealed class EfCustomerRepository(OpenBaseDbContext context) : ICustomerRepository
{
    public void Add(Customer customer)
    {
        RequireTransaction();
        context.Customers.Add(customer);
    }

    public Task<Customer?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        RequireTransaction();
        return context.Customers.SingleOrDefaultAsync(customer => customer.Id == id, cancellationToken);
    }

    public void Remove(Customer customer)
    {
        RequireTransaction();
        context.Customers.Remove(customer);
    }

    private void RequireTransaction()
    {
        if (context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Customer writes require an active unit of work.");
    }
}
