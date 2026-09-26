using OpenBaseNET.Application.Customers;
using OpenBaseNET.Application.Ports;
using OpenBaseNET.Domain.Customers;

namespace OpenBaseNET.Tests.Unit;

// Recording doubles verify orchestration only. They do not simulate database durability/rollback.
internal sealed class RecordingCustomerRepository(List<string> events) : ICustomerRepository
{
    public Customer? Customer { get; set; }
    public Customer? Added { get; private set; }
    public Customer? Removed { get; private set; }
    public Guid? RequestedId { get; private set; }
    public CancellationToken Token { get; private set; }
    public Action? OnGet { get; set; }
    public Exception? Failure { get; set; }

    public void Add(Customer customer)
    {
        events.Add("add");
        if (Failure is not null) throw Failure;
        Added = customer;
    }

    public Task<Customer?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        events.Add("load");
        RequestedId = id;
        Token = cancellationToken;
        OnGet?.Invoke();
        if (Failure is not null) throw Failure;
        return Task.FromResult(Customer?.Id == id ? Customer : null);
    }

    public void Remove(Customer customer)
    {
        events.Add("remove");
        if (Failure is not null) throw Failure;
        Removed = customer;
    }
}

internal sealed class RecordingUnitOfWork(List<string> events) : IUnitOfWork
{
    public int Calls { get; private set; }
    public CancellationToken Token { get; private set; }
    public Exception? CommitFailure { get; set; }
    public Task? BeforeCommit { get; set; }

    public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        Calls++;
        Token = cancellationToken;
        events.Add("begin");
        try
        {
            var result = await operation(cancellationToken);
            if (BeforeCommit is not null) await BeforeCommit.WaitAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (CommitFailure is not null) throw CommitFailure;
            events.Add("commit");
            return result;
        }
        catch
        {
            events.Add("failed");
            throw;
        }
    }
}

internal sealed class RecordingCustomerQueries : ICustomerQueries
{
    public CustomerResponse? Customer { get; set; }
    public IReadOnlyList<CustomerResponse> PageResult { get; set; } = [];
    public CustomerPage? Page { get; private set; }
    public Guid? RequestedId { get; private set; }
    public CancellationToken Token { get; private set; }
    public int Calls { get; private set; }
    public Exception? Failure { get; set; }
    public Action? OnRead { get; set; }

    public Task<CustomerResponse?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        Calls++;
        RequestedId = id;
        Token = cancellationToken;
        OnRead?.Invoke();
        if (Failure is not null) throw Failure;
        return Task.FromResult(Customer);
    }

    public Task<IReadOnlyList<CustomerResponse>> ListAsync(CustomerPage page, CancellationToken cancellationToken)
    {
        Calls++;
        Page = page;
        Token = cancellationToken;
        OnRead?.Invoke();
        if (Failure is not null) throw Failure;
        return Task.FromResult(PageResult);
    }
}
