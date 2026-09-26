using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using OpenBaseNET.Application.Customers;
using OpenBaseNET.Application.Ports;

namespace OpenBaseNET.Infrastructure.Persistence.SqlServer;

internal sealed class SqlServerCustomerQueries(OpenBaseDbContext context) : ICustomerQueries
{
    public Task<CustomerResponse?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        ReadAsync(() => context.Database.GetDbConnection().QuerySingleOrDefaultAsync<CustomerResponse>(new CommandDefinition(
            "SELECT [id] AS [Id], [name] AS [Name] FROM [dbo].[customers] WHERE [id] = @Id",
            new { Id = id }, context.Database.CurrentTransaction?.GetDbTransaction(), cancellationToken: cancellationToken)), cancellationToken);

    public async Task<IReadOnlyList<CustomerResponse>> ListAsync(CustomerPage page, CancellationToken cancellationToken)
    {
        var customers = await ReadAsync(() => context.Database.GetDbConnection().QueryAsync<CustomerResponse>(new CommandDefinition(
            """
            SELECT [id] AS [Id], [name] AS [Name] FROM [dbo].[customers]
            ORDER BY [name] ASC, [id] ASC OFFSET @Offset ROWS FETCH NEXT @Size ROWS ONLY
            """, new { page.Offset, page.Size }, context.Database.CurrentTransaction?.GetDbTransaction(), cancellationToken: cancellationToken)), cancellationToken);
        return customers.AsList();
    }

    private static async Task<T> ReadAsync<T>(Func<Task<T>> read, CancellationToken cancellationToken)
    {
        try { return await read(); }
        catch (SqlException exception) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException("Database operation was canceled.", exception, cancellationToken);
        }
    }
}
