using Microsoft.EntityFrameworkCore;
using OpenBaseNET.Domain.Customers;
using OpenBaseNET.Infrastructure.Persistence.Postgres;

namespace OpenBaseNET.Infrastructure.Persistence;

public sealed class OpenBaseDbContext(DbContextOptions<OpenBaseDbContext> options) : DbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfiguration(new CustomerConfiguration());
}
