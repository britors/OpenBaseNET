using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Data.SqlClient;
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
        adminConnectionString = Environment.GetEnvironmentVariable("OPENBASE_TEST_SQLSERVER")
            ?? throw new InvalidOperationException("Set OPENBASE_TEST_SQLSERVER to a SQL Server with CREATE DATABASE permission.");
        adminConnectionString = new SqlConnectionStringBuilder(adminConnectionString) { InitialCatalog = "master" }.ConnectionString;
        var builder = new SqlConnectionStringBuilder(adminConnectionString) { InitialCatalog = databaseName };
        ConnectionString = builder.ConnectionString;
        await using var admin = new SqlConnection(adminConnectionString);
        await admin.OpenAsync();
        await admin.ExecuteAsync($"CREATE DATABASE [{databaseName}]");
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
        await using var connection = new SqlConnection(ConnectionString);
        return await connection.ExecuteScalarAsync<long>("SELECT count_big(*) FROM dbo.customers");
    }

    protected async Task DropCustomerTable()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.ExecuteAsync("DROP TABLE dbo.customers");
    }

    public async Task DisposeAsync()
    {
        if (!created) return;
        // Name is generated internally and never taken from the configured database name.
        await using var admin = new SqlConnection(adminConnectionString);
        await admin.OpenAsync();
        await admin.ExecuteAsync($"ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{databaseName}]");
        created = false;
        using var poolKey = new SqlConnection(ConnectionString);
        SqlConnection.ClearPool(poolKey);
    }
}
