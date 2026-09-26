using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenBaseNET.Application.Ports;
using OpenBaseNET.Infrastructure.Persistence;
using OpenBaseNET.Infrastructure.Persistence.SqlServer;

namespace OpenBaseNET.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddPersistence(this IServiceCollection services, string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        services.AddDbContext<OpenBaseDbContext>(options => options.UseSqlServer(connectionString,
            sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "dbo")));
        services.TryAddScoped<ICustomerRepository, EfCustomerRepository>();
        services.TryAddScoped<ICustomerQueries, SqlServerCustomerQueries>();
        services.TryAddScoped<IUnitOfWork, EfUnitOfWork>();
        return services;
    }
}
