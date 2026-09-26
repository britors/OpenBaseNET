using System.Data;
using System.Diagnostics;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Oracle.ManagedDataAccess.Client;
using OpenBaseNET.Application.Customers;
using OpenBaseNET.Application.Ports;
using OpenBaseNET.Domain.Customers;
using OpenBaseNET.Infrastructure;
using OpenBaseNET.Infrastructure.Persistence;

namespace OpenBaseNET.Tests.Integration;

public sealed class OraclePersistenceTests : TestDatabase
{
    [Fact]
    public async Task Migrations_are_repeatable_and_model_matches_snapshot()
    {
        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OpenBaseDbContext>();
        Assert.Single(await context.Database.GetAppliedMigrationsAsync());
        Assert.False(context.Database.HasPendingModelChanges());
        await context.Database.MigrateAsync();
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        Assert.Equal(0, await CountCustomersAsync());
    }

    [Fact]
    public async Task Connection_is_lazy_and_registration_is_scoped_and_idempotent()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPersistence(ConnectionString);
        services.AddPersistence(ConnectionString);
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OpenBaseDbContext>();
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
        Assert.Single(scope.ServiceProvider.GetServices<ICustomerRepository>());
        Assert.Single(scope.ServiceProvider.GetServices<IUnitOfWork>());
        Assert.Same(context, scope.ServiceProvider.GetRequiredService<OpenBaseDbContext>());
        var queries = scope.ServiceProvider.GetRequiredService<ICustomerQueries>();
        Assert.Empty(await queries.ListAsync(new CustomerPage(), default));
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
        await using var other = provider.CreateAsyncScope();
        Assert.NotSame(context, other.ServiceProvider.GetRequiredService<OpenBaseDbContext>());
    }

    [Fact]
    public async Task Customer_CRUD_roundtrips_unicode_quotes_and_maximum_name()
    {
        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<ICustomerRepository>();
        var queries = scope.ServiceProvider.GetRequiredService<ICustomerQueries>();
        var work = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var created = await new CreateCustomer(repository, work).ExecuteAsync("  João d'Ávila 東京  ");
        Assert.Equal("João d'Ávila 東京", (await new GetCustomer(queries).ExecuteAsync(created.Id)).Name);
        var updated = await new UpdateCustomer(repository, work).ExecuteAsync(created.Id, new string('á', 200));
        Assert.Equal(updated, await new GetCustomer(queries).ExecuteAsync(created.Id));
        await new DeleteCustomer(repository, work).ExecuteAsync(created.Id);
        await Assert.ThrowsAsync<CustomerNotFoundException>(() => new GetCustomer(queries).ExecuteAsync(created.Id));
        Assert.Equal(0, await CountCustomersAsync());
    }

    [Fact]
    public async Task Pagination_orders_before_limiting_and_uses_unique_tiebreaker()
    {
        await using var connection = new OracleConnection(ConnectionString);
        var ids = Enumerable.Range(1, 5).Select(i => Guid.Parse($"00000000-0000-0000-0000-{i:000000000000}")).ToArray();
        foreach (var index in new[] { 4, 2, 0, 3, 1 })
            await connection.ExecuteAsync("INSERT INTO CUSTOMERS (ID, NAME) VALUES (:Id, :Name)",
                new { Id = ids[index].ToByteArray(), Name = index == 4 ? "Bea" : "Ana" });

        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var list = new ListCustomers(scope.ServiceProvider.GetRequiredService<ICustomerQueries>());
        Assert.Equal(ids[..2], (await list.ExecuteAsync(1, 2)).Select(customer => customer.Id));
        Assert.Equal(ids[2..4], (await list.ExecuteAsync(2, 2)).Select(customer => customer.Id));
        Assert.Equal(ids[4..], (await list.ExecuteAsync(3, 2)).Select(customer => customer.Id));
        Assert.Empty(await list.ExecuteAsync(4, 2));
        Assert.Equal(ids[2..4], (await list.ExecuteAsync(2, 2)).Select(customer => customer.Id));
    }

    [Fact]
    public async Task EF_and_Dapper_commit_on_same_connection_and_transaction()
    {
        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OpenBaseDbContext>();
        var repository = scope.ServiceProvider.GetRequiredService<ICustomerRepository>();
        var work = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var queries = scope.ServiceProvider.GetRequiredService<ICustomerQueries>();
        await work.ExecuteAsync(async token =>
        {
            var customer = Customer.Create("EF");
            repository.Add(customer);
            await context.SaveChangesAsync(token);
            Assert.NotNull(await queries.GetAsync(customer.Id, token));
            await InsertWithDapper(context, "Dapper", token);
            Assert.Equal(2, (await queries.ListAsync(new CustomerPage(), token)).Count);
            Assert.Equal(0, await CountCustomersAsync());
            return true;
        }, default);
        Assert.Equal(2, await CountCustomersAsync());
        Assert.Null(context.Database.CurrentTransaction);
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    [Fact]
    public async Task Raw_IDs_use_dotnet_byte_layout_and_binary_order()
    {
        var ids = new[] { Guid.Parse("00000100-0000-0000-0000-000000000000"), Guid.Parse("00000001-0000-0000-0000-000000000000") };
        Assert.True(ids[0].CompareTo(ids[1]) > 0);
        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OpenBaseDbContext>();
        await using var connection = new OracleConnection(ConnectionString);
        foreach (var id in ids.Reverse())
            await connection.ExecuteAsync("INSERT INTO CUSTOMERS (ID, NAME) VALUES (:Id, 'Ana')", new { Id = id.ToByteArray() });
        var queries = scope.ServiceProvider.GetRequiredService<ICustomerQueries>();
        Assert.Equal(ids[0], Assert.Single(await queries.ListAsync(new CustomerPage(1, 1), default)).Id);
        Assert.Equal(ids[1], Assert.Single(await queries.ListAsync(new CustomerPage(2, 1), default)).Id);
        var loaded = await context.Customers.SingleAsync(customer => customer.Id == ids[0]);
        Assert.Equal(ids[0], loaded.Id);
        Assert.Equal("00010000000000000000000000000000", await connection.ExecuteScalarAsync<string>(
            "SELECT RAWTOHEX(ID) FROM CUSTOMERS WHERE ID = :Id", new { Id = ids[0].ToByteArray() }));
    }

    [Fact]
    public async Task SQL_error_preserves_exception_and_rolls_back_both_writers_without_retry()
    {
        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OpenBaseDbContext>();
        var repository = scope.ServiceProvider.GetRequiredService<ICustomerRepository>();
        var work = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var calls = 0;
        var error = await Assert.ThrowsAsync<OracleException>(() => work.ExecuteAsync(async token =>
        {
            calls++;
            repository.Add(Customer.Create("EF"));
            await context.SaveChangesAsync(token);
            await InsertWithDapper(context, "Dapper", token);
            await context.Database.GetDbConnection().ExecuteAsync(new CommandDefinition(
                "BEGIN RAISE_APPLICATION_ERROR(-20001, 'Integration failure'); END;", transaction: context.Database.CurrentTransaction!.GetDbTransaction(), cancellationToken: token));
            return true;
        }, default));
        Assert.Equal(20001, error.Number);
        Assert.Equal(1, calls);
        Assert.Equal(0, await CountCustomersAsync());
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Null(context.Database.CurrentTransaction);
        await new CreateCustomer(repository, work).ExecuteAsync("After SQL failure");
        Assert.Equal(1, await CountCustomersAsync());
    }

    [Fact]
    public async Task Callback_failure_rolls_back_both_and_scope_can_be_reused()
    {
        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OpenBaseDbContext>();
        var repository = scope.ServiceProvider.GetRequiredService<ICustomerRepository>();
        var work = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var failure = new InvalidOperationException("Callback failed");
        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => work.ExecuteAsync<int>(async token =>
        {
            repository.Add(Customer.Create("EF"));
            await context.SaveChangesAsync(token);
            await InsertWithDapper(context, "Dapper", token);
            throw failure;
        }, default));
        Assert.Same(failure, thrown);
        Assert.Equal(0, await CountCustomersAsync());
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Null(context.Database.CurrentTransaction);
        await new CreateCustomer(repository, work).ExecuteAsync("After rollback");
        Assert.Equal(1, await CountCustomersAsync());
    }

    [Fact]
    public async Task Save_failure_rolls_back_earlier_Dapper_write_and_clears_tracking()
    {
        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OpenBaseDbContext>();
        var repository = scope.ServiceProvider.GetRequiredService<ICustomerRepository>();
        var work = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var customer = Customer.Create("Duplicate ID");
        var calls = 0;
        await Assert.ThrowsAsync<DbUpdateException>(() => work.ExecuteAsync(async token =>
        {
            calls++;
            repository.Add(customer);
            await context.Database.GetDbConnection().ExecuteAsync(new CommandDefinition(
                "INSERT INTO CUSTOMERS (ID, NAME) VALUES (:Id, :Name)",
                new { Id = customer.Id.ToByteArray(), customer.Name }, context.Database.CurrentTransaction!.GetDbTransaction(), cancellationToken: token));
            return true;
        }, default));
        Assert.Equal(1, calls);
        Assert.Equal(0, await CountCustomersAsync());
        Assert.Empty(context.ChangeTracker.Entries());
        await new CreateCustomer(repository, work).ExecuteAsync("After SQL failure");
        Assert.Equal(1, await CountCustomersAsync());
    }

    [Fact]
    public async Task Nested_execution_is_rejected_without_breaking_outer_transaction()
    {
        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var work = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var repository = scope.ServiceProvider.GetRequiredService<ICustomerRepository>();
        await work.ExecuteAsync(async token =>
        {
            repository.Add(Customer.Create("Outer"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => work.ExecuteAsync(_ => Task.FromResult(0), token));
            return true;
        }, default);
        Assert.Equal(1, await CountCustomersAsync());
    }

    [Fact]
    public async Task Deferred_constraint_failure_at_commit_preserves_original_error_without_retry()
    {
        await using var connection = new OracleConnection(ConnectionString);
        await connection.ExecuteAsync("ALTER TABLE CUSTOMERS ADD CONSTRAINT unique_name UNIQUE (name) DEFERRABLE INITIALLY DEFERRED");
        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OpenBaseDbContext>();
        var repository = scope.ServiceProvider.GetRequiredService<ICustomerRepository>();
        var work = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var calls = 0;
        var error = await Assert.ThrowsAsync<OracleException>(() => work.ExecuteAsync(async token =>
        {
            calls++;
            repository.Add(Customer.Create("Duplicate"));
            await context.SaveChangesAsync(token);
            await InsertWithDapper(context, "Duplicate", token);
            return true;
        }, default));
        Assert.Equal(2091, error.Number); // ORA-02091: transaction rolled back at commit
        Assert.Equal(1, calls);
        Assert.Equal(0, await CountCustomersAsync());
        Assert.Null(context.Database.CurrentTransaction);
        Assert.Empty(context.ChangeTracker.Entries());
        await new CreateCustomer(repository, work).ExecuteAsync("After commit failure");
        Assert.Equal(1, await CountCustomersAsync());
    }

    [Fact]
    public async Task Disposing_uncommitted_transaction_rolls_back_and_allows_next_transaction()
    {
        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OpenBaseDbContext>();
        await using (await context.Database.BeginTransactionAsync())
            await InsertWithDapper(context, "Never committed", default);
        Assert.Null(context.Database.CurrentTransaction);
        Assert.Equal(0, await CountCustomersAsync());
        await new CreateCustomer(scope.ServiceProvider.GetRequiredService<ICustomerRepository>(),
            scope.ServiceProvider.GetRequiredService<IUnitOfWork>()).ExecuteAsync("Next transaction");
        Assert.Equal(1, await CountCustomersAsync());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Running_query_honors_cancellation_and_releases_connection(bool ef, bool transactional)
    {
        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OpenBaseDbContext>();
        var work = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var customerId = await InstallSlowCustomerViewAsync();
        async Task<bool> QueryAsync(CancellationToken token)
        {
            if (ef) await scope.ServiceProvider.GetRequiredService<ICustomerRepository>().GetAsync(customerId, token);
            else await scope.ServiceProvider.GetRequiredService<ICustomerQueries>().ListAsync(new CustomerPage(), token);
            return true;
        }
        var operation = transactional ? work.ExecuteAsync(QueryAsync, cancellation.Token) : QueryAsync(cancellation.Token);

        await WaitForBlockedQueryAsync();
        cancellation.Cancel();
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(cancellation.Token, error.CancellationToken);
        await RemoveSlowCustomerViewAsync();
        Assert.Null(context.Database.CurrentTransaction);
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
        await new CreateCustomer(scope.ServiceProvider.GetRequiredService<ICustomerRepository>(), work).ExecuteAsync("After cancellation");
        Assert.Equal(2, await CountCustomersAsync());
    }

    [Fact]
    public async Task Cancellation_after_mixed_writes_rolls_back_both()
    {
        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OpenBaseDbContext>();
        var repository = scope.ServiceProvider.GetRequiredService<ICustomerRepository>();
        var work = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => work.ExecuteAsync(async token =>
        {
            repository.Add(Customer.Create("EF"));
            await context.SaveChangesAsync(token);
            await InsertWithDapper(context, "Dapper", token);
            cancellation.Cancel();
            return true;
        }, cancellation.Token));
        Assert.Equal(0, await CountCustomersAsync());
        Assert.Empty(context.ChangeTracker.Entries());
        await new CreateCustomer(repository, work).ExecuteAsync("Next");
        Assert.Equal(1, await CountCustomersAsync());
    }

    // Ordinary Oracle reads use MVCC and do not wait for row locks. A temporary
    // view invokes a sleeping function so the real repository/query SQL is running.
    private async Task<Guid> InstallSlowCustomerViewAsync()
    {
        await using var connection = new OracleConnection(ConnectionString);
        var id = Guid.NewGuid();
        await connection.ExecuteAsync("INSERT INTO CUSTOMERS (ID, NAME) VALUES (:Id, 'Slow')", new { Id = id.ToByteArray() });
        await connection.ExecuteAsync("ALTER TABLE CUSTOMERS RENAME TO CUSTOMER_ROWS");
        await connection.ExecuteAsync("""
            CREATE FUNCTION SLOW_NAME(value NVARCHAR2) RETURN NVARCHAR2 AS
            BEGIN DBMS_SESSION.SLEEP(30); RETURN value; END;
            """);
        await connection.ExecuteAsync("CREATE VIEW CUSTOMERS AS SELECT ID, SLOW_NAME(NAME) NAME FROM CUSTOMER_ROWS");
        return id;
    }

    private async Task RemoveSlowCustomerViewAsync()
    {
        await using var connection = new OracleConnection(ConnectionString);
        await connection.ExecuteAsync("DROP VIEW CUSTOMERS");
        await connection.ExecuteAsync("ALTER TABLE CUSTOMER_ROWS RENAME TO CUSTOMERS");
    }

    private async Task WaitForBlockedQueryAsync()
    {
        await using var observer = new OracleConnection(AdminConnectionString);
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < TimeSpan.FromSeconds(10))
        {
            if (await observer.ExecuteScalarAsync<int>("""
                SELECT COUNT(*) FROM V$SESSION
                WHERE USERNAME = :Username AND EVENT = 'PL/SQL lock timer' AND STATE = 'WAITING'
                """, new { Username = SchemaName }) > 0) return;
            await Task.Delay(20);
        }
        Assert.Fail("The query did not reach the Oracle sleep function before cancellation.");
    }

    private static Task<int> InsertWithDapper(OpenBaseDbContext context, string name, CancellationToken token) =>
        context.Database.GetDbConnection().ExecuteAsync(new CommandDefinition(
            "INSERT INTO CUSTOMERS (ID, NAME) VALUES (:Id, :Name)", new { Id = Guid.NewGuid().ToByteArray(), Name = name },
            context.Database.CurrentTransaction!.GetDbTransaction(), cancellationToken: token));
}
