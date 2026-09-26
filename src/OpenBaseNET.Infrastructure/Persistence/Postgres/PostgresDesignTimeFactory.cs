using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace OpenBaseNET.Infrastructure.Persistence.Postgres;

public sealed class PostgresDesignTimeFactory : IDesignTimeDbContextFactory<OpenBaseDbContext>
{
    public OpenBaseDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Default");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("Set ConnectionStrings__Default before running EF tools.");

        return new(new DbContextOptionsBuilder<OpenBaseDbContext>().UseNpgsql(connectionString,
            postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", "public")).Options);
    }
}
