using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using OpenBaseNET.Application.Customers;
using OpenBaseNET.Application.Ports;

namespace OpenBaseNET.Infrastructure.Persistence.SqlServer;

internal sealed class SqlServerCustomerQueries(OpenBaseDbContext context) : ICustomerQueries
{
    public Task<CustomerResponse?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        context.Database.GetDbConnection().QuerySingleOrDefaultAsync<CustomerResponse>(new CommandDefinition(
            "SELECT [id] AS [Id], [name] AS [Name] FROM [dbo].[customers] WHERE [id] = @Id",
            new { Id = id }, context.Database.CurrentTransaction?.GetDbTransaction(), cancellationToken: cancellationToken));

    public async Task<IReadOnlyList<CustomerResponse>> ListAsync(CustomerPage page, CancellationToken cancellationToken)
    {
        var customers = await context.Database.GetDbConnection().QueryAsync<CustomerResponse>(new CommandDefinition(
            """
            SELECT [id] AS [Id], [name] AS [Name] FROM [dbo].[customers]
            ORDER BY [name] ASC, [id] ASC OFFSET @Offset ROWS FETCH NEXT @Size ROWS ONLY
            """, new { page.Offset, page.Size }, context.Database.CurrentTransaction?.GetDbTransaction(), cancellationToken: cancellationToken));
        return customers.AsList();
    }
}
