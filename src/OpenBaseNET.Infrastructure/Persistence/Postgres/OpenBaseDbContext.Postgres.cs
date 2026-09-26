using Microsoft.EntityFrameworkCore;
using OpenBaseNET.Infrastructure.Persistence.Postgres;

namespace OpenBaseNET.Infrastructure.Persistence;

public sealed partial class OpenBaseDbContext
{
    private partial void ConfigureProvider(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfiguration(new CustomerConfiguration());
}
