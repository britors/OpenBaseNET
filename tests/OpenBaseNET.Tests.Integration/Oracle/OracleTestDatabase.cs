using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Oracle.ManagedDataAccess.Client;
using OpenBaseNET.Infrastructure;
using OpenBaseNET.Infrastructure.Persistence;

namespace OpenBaseNET.Tests.Integration;

// Each test owns a generated schema inside the configured test PDB.
public abstract class TestDatabase : IAsyncLifetime
{
    static TestDatabase()
    {
        // Use in-band cancellation across host-to-container port forwarding.
        // Configure once, before any connection; application defaults remain unchanged.
        OracleConfiguration.DisableOOB = true;
    }

    protected string SchemaName { get; } = "OB_TEST_" + Guid.NewGuid().ToString("N").ToUpperInvariant();
    protected string AdminConnectionString { get; private set; } = "";
    protected string ConnectionString { get; private set; } = "";
    private bool created;

    public async Task InitializeAsync()
    {
        AdminConnectionString = Environment.GetEnvironmentVariable("OPENBASE_TEST_ORACLE")
            ?? throw new InvalidOperationException("Set OPENBASE_TEST_ORACLE to an administrative connection to a test PDB.");
        var password = "Ob_" + Guid.NewGuid().ToString("N");
        ConnectionString = new OracleConnectionStringBuilder(AdminConnectionString)
        {
            UserID = SchemaName, Password = password, Pooling = false
        }.ConnectionString;
        await using var admin = new OracleConnection(AdminConnectionString);
        await admin.OpenAsync();
        await admin.ExecuteAsync($"CREATE USER {SchemaName} IDENTIFIED BY \"{password}\" QUOTA 10M ON USERS");
        created = true;
        try
        {
            await admin.ExecuteAsync($"GRANT CREATE SESSION, CREATE TABLE, CREATE VIEW, CREATE PROCEDURE TO {SchemaName}");
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
        await using var connection = new OracleConnection(ConnectionString);
        return await connection.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM CUSTOMERS");
    }

    protected async Task DropCustomerTable()
    {
        await using var connection = new OracleConnection(ConnectionString);
        await connection.ExecuteAsync("DROP TABLE CUSTOMERS PURGE");
    }

    public async Task DisposeAsync()
    {
        if (!created) return;
        // Only the internally generated user is removed, never the configured admin user.
        await using var admin = new OracleConnection(AdminConnectionString);
        await admin.OpenAsync();
        await admin.ExecuteAsync($"DROP USER {SchemaName} CASCADE");
        created = false;
    }
}
