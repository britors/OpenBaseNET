using System.Data;
using System.Diagnostics;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OpenBaseNET.Application.Customers;
using OpenBaseNET.Application.Ports;
using OpenBaseNET.Domain.Customers;
using OpenBaseNET.Infrastructure;
using OpenBaseNET.Infrastructure.Persistence;

namespace OpenBaseNET.Tests.Integration;

public sealed class PostgresPersistenceTests : TestDatabase
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
        await using var connection = new NpgsqlConnection(ConnectionString);
        var ids = Enumerable.Range(1, 5).Select(i => Guid.Parse($"00000000-0000-0000-0000-{i:000000000000}")).ToArray();
        foreach (var index in new[] { 4, 2, 0, 3, 1 })
            await connection.ExecuteAsync("INSERT INTO public.customers (id, name) VALUES (@Id, @Name)",
                new { Id = ids[index], Name = index == 4 ? "Bea" : "Ana" });

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
                "INSERT INTO public.customers (id, name) VALUES (@Id, @Name)",
                new { customer.Id, customer.Name }, context.Database.CurrentTransaction!.GetDbTransaction(), cancellationToken: token));
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
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.ExecuteAsync("ALTER TABLE public.customers ADD CONSTRAINT unique_name UNIQUE (name) DEFERRABLE INITIALLY DEFERRED");
        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OpenBaseDbContext>();
        var repository = scope.ServiceProvider.GetRequiredService<ICustomerRepository>();
        var work = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var calls = 0;
        var error = await Assert.ThrowsAsync<PostgresException>(() => work.ExecuteAsync(async token =>
        {
            calls++;
            repository.Add(Customer.Create("Duplicate"));
            await context.SaveChangesAsync(token);
            await InsertWithDapper(context, "Duplicate", token);
            return true;
        }, default));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, error.SqlState);
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
    [InlineData(false)]
    [InlineData(true)]
    public async Task Running_query_honors_cancellation_and_releases_connection(bool ef)
    {
        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OpenBaseDbContext>();
        var work = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var blocker = new NpgsqlConnection(ConnectionString);
        await blocker.OpenAsync();
        await using var blockingTransaction = await blocker.BeginTransactionAsync();
        await blocker.ExecuteAsync("LOCK TABLE public.customers IN ACCESS EXCLUSIVE MODE", transaction: blockingTransaction);
        var operation = work.ExecuteAsync(async token =>
        {
            if (ef) await scope.ServiceProvider.GetRequiredService<ICustomerRepository>().GetAsync(Guid.NewGuid(), token);
            else await scope.ServiceProvider.GetRequiredService<ICustomerQueries>().ListAsync(new CustomerPage(), token);
            return true;
        }, cancellation.Token);

        await WaitForBlockedQueryAsync();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation.WaitAsync(TimeSpan.FromSeconds(5)));
        await blockingTransaction.RollbackAsync();
        Assert.Null(context.Database.CurrentTransaction);
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
        await new CreateCustomer(scope.ServiceProvider.GetRequiredService<ICustomerRepository>(), work).ExecuteAsync("After cancellation");
        Assert.Equal(1, await CountCustomersAsync());
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

    private async Task WaitForBlockedQueryAsync()
    {
        await using var observer = new NpgsqlConnection(ConnectionString);
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < TimeSpan.FromSeconds(5))
        {
            if (await observer.ExecuteScalarAsync<bool>("""
                SELECT EXISTS(SELECT 1 FROM pg_stat_activity
                WHERE datname = current_database() AND wait_event_type = 'Lock' AND state = 'active')
                """)) return;
            await Task.Delay(20);
        }
        Assert.Fail("The query did not reach a PostgreSQL lock wait before cancellation.");
    }

    private static Task<int> InsertWithDapper(OpenBaseDbContext context, string name, CancellationToken token) =>
        context.Database.GetDbConnection().ExecuteAsync(new CommandDefinition(
            "INSERT INTO public.customers (id, name) VALUES (@Id, @Name)", new { Id = Guid.NewGuid(), Name = name },
            context.Database.CurrentTransaction!.GetDbTransaction(), cancellationToken: token));
}
