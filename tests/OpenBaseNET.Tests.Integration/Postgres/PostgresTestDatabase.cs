using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OpenBaseNET.Infrastructure;
using OpenBaseNET.Infrastructure.Persistence;

namespace OpenBaseNET.Tests.Integration;

// Each test owns a fresh database. Missing configuration/server is a failure, never a skip.
public abstract class TestDatabase : IAsyncLifetime
{
    private readonly string databaseName = "ob_test_" + Guid.NewGuid().ToString("N");
    private string adminConnectionString = "";
    private bool created;
    protected string ConnectionString { get; private set; } = "";

    public async Task InitializeAsync()
    {
        adminConnectionString = Environment.GetEnvironmentVariable("OPENBASE_TEST_POSTGRES")
            ?? throw new InvalidOperationException("Set OPENBASE_TEST_POSTGRES to a PostgreSQL server with CREATE DATABASE permission.");
        var builder = new NpgsqlConnectionStringBuilder(adminConnectionString) { Database = databaseName };
        ConnectionString = builder.ConnectionString;
        await using var admin = new NpgsqlConnection(adminConnectionString);
        await admin.OpenAsync();
        await admin.ExecuteAsync($"CREATE DATABASE \"{databaseName}\"");
        created = true;
        try
        {
            await using var provider = BuildProvider();
            await using var scope = provider.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<OpenBaseDbContext>().Database.MigrateAsync();
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    protected ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPersistence(ConnectionString);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    protected async Task<long> CountCustomersAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        return await connection.ExecuteScalarAsync<long>("SELECT count(*) FROM public.customers");
    }

    protected async Task DropCustomerTable()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.ExecuteAsync("DROP TABLE public.customers");
    }

    public async Task DisposeAsync()
    {
        if (!created) return;
        // Name is generated internally and never taken from the configured database name.
        await using var admin = new NpgsqlConnection(adminConnectionString);
        await admin.OpenAsync();
        await admin.ExecuteAsync($"DROP DATABASE \"{databaseName}\" WITH (FORCE)");
        created = false;
        using var poolKey = new NpgsqlConnection(ConnectionString);
        NpgsqlConnection.ClearPool(poolKey);
    }
}
