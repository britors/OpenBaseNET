using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace OpenBaseNET.Infrastructure.Persistence.SqlServer;

public sealed class SqlServerDesignTimeFactory : IDesignTimeDbContextFactory<OpenBaseDbContext>
{
    public OpenBaseDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Default");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("Set ConnectionStrings__Default before running EF tools.");
        return new(new DbContextOptionsBuilder<OpenBaseDbContext>().UseSqlServer(connectionString,
            sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "dbo")).Options);
    }
}
