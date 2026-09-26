using System.Runtime.ExceptionServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using OpenBaseNET.Application.Ports;

namespace OpenBaseNET.Infrastructure.Persistence.Postgres;

internal sealed class PostgresUnitOfWork(OpenBaseDbContext context, ILogger<PostgresUnitOfWork> logger) : IUnitOfWork
{
    private int active;

    public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.CompareExchange(ref active, 1, 0) != 0)
            throw new InvalidOperationException("Nested or concurrent units of work are not supported.");

        IDbContextTransaction? transaction = null;
        Exception? failure = null;
        try
        {
            if (context.Database.CurrentTransaction is not null)
                throw new InvalidOperationException("A transaction is already active in this scope.");

            transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            var result = await operation(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (Exception exception)
        {
            failure = exception;
            if (transaction is not null)
            {
                try
                {
                    // Cleanup must still run when the request token has been cancelled.
                    await transaction.RollbackAsync(CancellationToken.None);
                }
                catch (Exception cleanup)
                {
                    logger.LogWarning(cleanup, "Rollback failed; discard this scope before retrying an operation.");
                }
            }
            throw;
        }
        finally
        {
            Exception? cleanupFailure = null;
            try
            {
                if (transaction is not null) await transaction.DisposeAsync();
            }
            catch (Exception cleanup)
            {
                cleanupFailure = cleanup;
            }

            // Do not clear tracking owned by a caller's existing transaction.
            if (transaction is not null)
            {
                try { context.ChangeTracker.Clear(); }
                catch (Exception cleanup) { cleanupFailure ??= cleanup; }
            }
            Volatile.Write(ref active, 0);
            if (cleanupFailure is not null)
            {
                if (failure is null) ExceptionDispatchInfo.Capture(cleanupFailure).Throw();
                logger.LogWarning(cleanupFailure, "Transaction cleanup failed; the original exception was preserved.");
            }
        }
    }
}
