using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenBaseNET.Application.Ports;
using OpenBaseNET.Infrastructure.Persistence;
using OpenBaseNET.Infrastructure.Persistence.Oracle;

namespace OpenBaseNET.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddPersistence(this IServiceCollection services, string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        services.AddDbContext<OpenBaseDbContext>(options => options.UseOracle(connectionString,
            sql => sql.UseOracleSQLCompatibility(OracleSQLCompatibility.DatabaseVersion23).MigrationsHistoryTable("__EFMigrationsHistory")));
        services.TryAddScoped<ICustomerRepository, EfCustomerRepository>();
        services.TryAddScoped<ICustomerQueries, OracleCustomerQueries>();
        services.TryAddScoped<IUnitOfWork, EfUnitOfWork>();
        return services;
    }
}
