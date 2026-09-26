using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace OpenBaseNET.Infrastructure.Persistence.Oracle;

public sealed class OracleDesignTimeFactory : IDesignTimeDbContextFactory<OpenBaseDbContext>
{
    public OpenBaseDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Default");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("Set ConnectionStrings__Default before running EF tools.");
        return new(new DbContextOptionsBuilder<OpenBaseDbContext>().UseOracle(connectionString,
            sql => sql.UseOracleSQLCompatibility(OracleSQLCompatibility.DatabaseVersion23).MigrationsHistoryTable("__EFMigrationsHistory")).Options);
    }
}
