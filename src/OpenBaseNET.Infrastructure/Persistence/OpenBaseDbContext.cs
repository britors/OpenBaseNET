using Microsoft.EntityFrameworkCore;
using OpenBaseNET.Domain.Customers;

namespace OpenBaseNET.Infrastructure.Persistence;

public sealed partial class OpenBaseDbContext(DbContextOptions<OpenBaseDbContext> options) : DbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        ConfigureProvider(modelBuilder);

    private partial void ConfigureProvider(ModelBuilder modelBuilder);
}
