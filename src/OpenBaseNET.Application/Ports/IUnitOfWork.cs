namespace OpenBaseNET.Application.Ports;

/// <summary>
/// Runs a callback once in a transaction, saves pending changes, and returns only after commit.
/// The supplied token must be propagated to the callback and asynchronous adapter operations.
/// Failure/cancellation rolls back and clears incompatible tracking state. Cleanup must not mask
/// the original exception. Nested transactions are rejected; callbacks are not silently retried.
/// EF and Dapper operations in the scope share the connection and transaction.
/// </summary>
public interface IUnitOfWork
{
    Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken);
}
