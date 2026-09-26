using OpenBaseNET.Application;
using OpenBaseNET.Application.Customers;
using OpenBaseNET.Domain;
using OpenBaseNET.Domain.Customers;

namespace OpenBaseNET.Tests.Unit;

public sealed class CustomerUseCaseTests
{
    private readonly List<string> events = [];
    private readonly RecordingCustomerRepository repository;
    private readonly RecordingUnitOfWork unitOfWork;
    private readonly RecordingCustomerQueries queries = new();

    public CustomerUseCaseTests()
    {
        repository = new(events);
        unitOfWork = new(events);
    }

    [Fact]
    public async Task Create_stages_valid_entity_inside_unit_of_work()
    {
        using var cancellation = new CancellationTokenSource();
        var response = await new CreateCustomer(repository, unitOfWork).ExecuteAsync("  Ana  ", cancellation.Token);

        Assert.Equal(new[] { "begin", "add", "commit" }, events);
        Assert.Equal(1, unitOfWork.Calls);
        Assert.Equal(cancellation.Token, unitOfWork.Token);
        Assert.NotNull(repository.Added);
        Assert.Equal(repository.Added.Id, response.Id);
        Assert.Equal("Ana", response.Name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Invalid_create_does_not_open_a_transaction(string? name)
    {
        await Assert.ThrowsAsync<DomainValidationException>(() => new CreateCustomer(repository, unitOfWork).ExecuteAsync(name!));
        Assert.Empty(events);
        Assert.Equal(0, unitOfWork.Calls);
    }

    [Fact]
    public async Task Long_names_are_rejected_before_create_or_update_transaction()
    {
        var name = new string('x', Customer.MaxNameLength + 1);
        await Assert.ThrowsAsync<DomainValidationException>(() => new CreateCustomer(repository, unitOfWork).ExecuteAsync(name));
        await Assert.ThrowsAsync<DomainValidationException>(() => new UpdateCustomer(repository, unitOfWork).ExecuteAsync(Guid.NewGuid(), name));
        Assert.Empty(events);
    }

    [Fact]
    public async Task Create_does_not_report_success_until_commit_finishes()
    {
        var commit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        unitOfWork.BeforeCommit = commit.Task;
        var operation = new CreateCustomer(repository, unitOfWork).ExecuteAsync("Ana");
        Assert.False(operation.IsCompleted);
        Assert.Equal(new[] { "begin", "add" }, events);
        commit.SetResult();
        var response = await operation;
        Assert.Equal("Ana", response.Name);
        Assert.Equal("commit", events.Last());
    }

    [Theory]
    [InlineData("create")]
    [InlineData("update")]
    [InlineData("delete")]
    public async Task Commit_failure_is_propagated_without_retry(string operation)
    {
        repository.Customer = Customer.Create("Ana");
        var failure = new InvalidOperationException("Commit failed");
        unitOfWork.CommitFailure = failure;
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Write(operation, repository.Customer.Id));
        Assert.Same(failure, error);
        Assert.Equal(1, unitOfWork.Calls);
        Assert.DoesNotContain("commit", events);
    }

    [Theory]
    [InlineData("create")]
    [InlineData("update")]
    [InlineData("delete")]
    public async Task Repository_failure_is_propagated_without_commit_or_retry(string operation)
    {
        var failure = new InvalidOperationException("Adapter failed");
        repository.Failure = failure;
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Write(operation, Guid.NewGuid()));
        Assert.Same(failure, error);
        Assert.Equal(1, unitOfWork.Calls);
        Assert.DoesNotContain("commit", events);
    }

    [Fact]
    public async Task Get_returns_projection_and_forwards_identity_and_token()
    {
        using var cancellation = new CancellationTokenSource();
        queries.Customer = new(Guid.NewGuid(), "Ana");
        var response = await new GetCustomer(queries).ExecuteAsync(queries.Customer.Id, cancellation.Token);
        Assert.Same(queries.Customer, response);
        Assert.Equal(response.Id, queries.RequestedId);
        Assert.Equal(cancellation.Token, queries.Token);
        Assert.Empty(events);
    }

    [Fact]
    public async Task Missing_customer_has_explicit_not_found_error()
    {
        var id = Guid.NewGuid();
        var error = await Assert.ThrowsAsync<CustomerNotFoundException>(() => new GetCustomer(queries).ExecuteAsync(id));
        Assert.Equal(id, error.CustomerId);
        Assert.Equal("CUSTOMER_NOT_FOUND", error.Code);
    }

    [Theory]
    [InlineData("get")]
    [InlineData("update")]
    [InlineData("delete")]
    public async Task Empty_identity_is_rejected_before_using_ports(string operation)
    {
        var error = await Assert.ThrowsAsync<InputValidationException>(() =>
            operation == "get" ? new GetCustomer(queries).ExecuteAsync(Guid.Empty) : Write(operation, Guid.Empty));
        Assert.Equal("CUSTOMER_ID_REQUIRED", error.Code);
        Assert.Empty(events);
        Assert.Equal(0, queries.Calls);
    }

    [Fact]
    public async Task List_passes_validated_page_to_adapter_and_preserves_its_order()
    {
        using var cancellation = new CancellationTokenSource();
        queries.PageResult = [
            new(Guid.Parse("00000000-0000-0000-0000-000000000001"), "Ana"),
            new(Guid.Parse("00000000-0000-0000-0000-000000000002"), "Ana")
        ];
        var response = await new ListCustomers(queries).ExecuteAsync(3, 2, cancellation.Token);
        Assert.Same(queries.PageResult, response);
        Assert.NotNull(queries.Page);
        Assert.Equal(3, queries.Page.Number);
        Assert.Equal(2, queries.Page.Size);
        Assert.Equal(4, queries.Page.Offset);
        Assert.Equal(cancellation.Token, queries.Token);
        Assert.Empty(events);
    }

    [Fact]
    public async Task Empty_page_returns_empty_collection()
    {
        Assert.Empty(await new ListCustomers(queries).ExecuteAsync());
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    [InlineData(int.MaxValue, 100)]
    public async Task Invalid_pagination_does_not_reach_adapter(int number, int size)
    {
        await Assert.ThrowsAsync<InputValidationException>(() => new ListCustomers(queries).ExecuteAsync(number, size));
        Assert.Equal(0, queries.Calls);
        Assert.Empty(events);
    }

    [Fact]
    public async Task Update_changes_tracked_entity_inside_transaction()
    {
        using var cancellation = new CancellationTokenSource();
        repository.Customer = Customer.Create("Ana");
        var response = await new UpdateCustomer(repository, unitOfWork).ExecuteAsync(repository.Customer.Id, "  Bea  ", cancellation.Token);
        Assert.Equal("Bea", repository.Customer.Name);
        Assert.Equal(repository.Customer.Id, response.Id);
        Assert.Equal("Bea", response.Name);
        Assert.Equal(new[] { "begin", "load", "commit" }, events);
        Assert.Equal(cancellation.Token, repository.Token);
        Assert.Equal(cancellation.Token, unitOfWork.Token);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task Invalid_update_does_not_load_or_open_transaction(string? name)
    {
        await Assert.ThrowsAsync<DomainValidationException>(() => new UpdateCustomer(repository, unitOfWork).ExecuteAsync(Guid.NewGuid(), name!));
        Assert.Empty(events);
    }

    [Fact]
    public async Task Delete_stages_removal_inside_transaction()
    {
        using var cancellation = new CancellationTokenSource();
        repository.Customer = Customer.Create("Ana");
        await new DeleteCustomer(repository, unitOfWork).ExecuteAsync(repository.Customer.Id, cancellation.Token);
        Assert.Same(repository.Customer, repository.Removed);
        Assert.Equal(new[] { "begin", "load", "remove", "commit" }, events);
        Assert.Equal(cancellation.Token, repository.Token);
        Assert.Equal(cancellation.Token, unitOfWork.Token);
    }

    [Theory]
    [InlineData("update")]
    [InlineData("delete")]
    public async Task Missing_write_target_does_not_commit(string operation)
    {
        var id = Guid.NewGuid();
        var error = await Assert.ThrowsAsync<CustomerNotFoundException>(() => Write(operation, id));
        Assert.Equal(id, error.CustomerId);
        Assert.Equal(new[] { "begin", "load", "failed" }, events);
        Assert.Null(repository.Removed);
    }

    [Theory]
    [InlineData("create")]
    [InlineData("get")]
    [InlineData("list")]
    [InlineData("update")]
    [InlineData("delete")]
    public async Task Precancelled_operation_does_not_use_ports(string operation)
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation switch
        {
            "get" => new GetCustomer(queries).ExecuteAsync(Guid.NewGuid(), cancellation.Token),
            "list" => new ListCustomers(queries).ExecuteAsync(cancellationToken: cancellation.Token),
            _ => Write(operation, Guid.NewGuid(), cancellation.Token)
        });
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Empty(events);
        Assert.Equal(0, queries.Calls);
    }

    [Theory]
    [InlineData("update")]
    [InlineData("delete")]
    public async Task Cancellation_during_load_prevents_mutation(string operation)
    {
        using var cancellation = new CancellationTokenSource();
        repository.Customer = Customer.Create("Ana");
        repository.OnGet = cancellation.Cancel;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Write(operation, repository.Customer.Id, cancellation.Token));
        Assert.Equal("Ana", repository.Customer.Name);
        Assert.Null(repository.Removed);
        Assert.Equal(new[] { "begin", "load", "failed" }, events);
    }

    [Fact]
    public async Task Cancellation_while_waiting_for_commit_does_not_report_success()
    {
        using var cancellation = new CancellationTokenSource();
        unitOfWork.BeforeCommit = new TaskCompletionSource().Task;
        var operation = new CreateCustomer(repository, unitOfWork).ExecuteAsync("Ana", cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
        Assert.DoesNotContain("commit", events);
    }

    [Theory]
    [InlineData("get")]
    [InlineData("list")]
    public async Task Read_adapter_failure_is_not_converted_to_not_found_or_empty(string operation)
    {
        var failure = new InvalidOperationException("Read failed");
        queries.Failure = failure;
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => operation == "get"
            ? (Task)new GetCustomer(queries).ExecuteAsync(Guid.NewGuid())
            : new ListCustomers(queries).ExecuteAsync());
        Assert.Same(failure, error);
    }

    [Theory]
    [InlineData("get")]
    [InlineData("list")]
    [InlineData("update")]
    [InlineData("delete")]
    public async Task Cancellation_during_lookup_is_not_reported_as_missing_or_empty(string operation)
    {
        using var cancellation = new CancellationTokenSource();
        queries.OnRead = cancellation.Cancel;
        repository.OnGet = cancellation.Cancel;
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation switch
        {
            "get" => new GetCustomer(queries).ExecuteAsync(Guid.NewGuid(), cancellation.Token),
            "list" => new ListCustomers(queries).ExecuteAsync(cancellationToken: cancellation.Token),
            _ => Write(operation, Guid.NewGuid(), cancellation.Token)
        });
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.DoesNotContain("commit", events);
    }

    private Task Write(string operation, Guid id, CancellationToken cancellationToken = default) => operation switch
    {
        "create" => new CreateCustomer(repository, unitOfWork).ExecuteAsync("Bea", cancellationToken),
        "update" => new UpdateCustomer(repository, unitOfWork).ExecuteAsync(id, "Bea", cancellationToken),
        "delete" => new DeleteCustomer(repository, unitOfWork).ExecuteAsync(id, cancellationToken),
        _ => throw new ArgumentOutOfRangeException(nameof(operation))
    };
}
