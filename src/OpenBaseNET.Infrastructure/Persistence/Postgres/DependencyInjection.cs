using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenBaseNET.Application.Ports;
using OpenBaseNET.Infrastructure.Persistence;
using OpenBaseNET.Infrastructure.Persistence.Postgres;

namespace OpenBaseNET.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddPersistence(this IServiceCollection services, string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        // DbContext owns its lazy connection. Dapper borrows it and the current EF transaction.
        services.AddDbContext<OpenBaseDbContext>(options => options.UseNpgsql(connectionString,
            postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", "public")));
        services.TryAddScoped<ICustomerRepository, EfCustomerRepository>();
        services.TryAddScoped<ICustomerQueries, PostgresCustomerQueries>();
        services.TryAddScoped<IUnitOfWork, EfUnitOfWork>();
        return services;
    }
}
