using System.Data;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using OpenBaseNET.Application.Customers;
using OpenBaseNET.Application.Ports;
using Oracle.ManagedDataAccess.Client;

namespace OpenBaseNET.Infrastructure.Persistence.Oracle;

internal sealed class OracleCustomerQueries(OpenBaseDbContext context) : ICustomerQueries
{
    public async Task<CustomerResponse?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var row = await ReadAsync(() => context.Database.GetDbConnection().QuerySingleOrDefaultAsync<CustomerRow>(new CommandDefinition(
            "SELECT ID AS \"Id\", NAME AS \"Name\" FROM CUSTOMERS WHERE ID = :Id",
            new BoundParameters(new OracleParameter("Id", OracleDbType.Raw, id.ToByteArray(), ParameterDirection.Input)),
            context.Database.CurrentTransaction?.GetDbTransaction(), cancellationToken: cancellationToken)), cancellationToken);
        return row?.ToResponse();
    }

    public async Task<IReadOnlyList<CustomerResponse>> ListAsync(CustomerPage page, CancellationToken cancellationToken)
    {
        var rows = await ReadAsync(() => context.Database.GetDbConnection().QueryAsync<CustomerRow>(new CommandDefinition(
            """
            SELECT ID AS "Id", NAME AS "Name" FROM CUSTOMERS
            ORDER BY NAME ASC, ID ASC OFFSET :page_offset ROWS FETCH NEXT :page_size ROWS ONLY
            """, new BoundParameters(
                new OracleParameter("page_size", OracleDbType.Int32, page.Size, ParameterDirection.Input),
                new OracleParameter("page_offset", OracleDbType.Int32, page.Offset, ParameterDirection.Input)),
            context.Database.CurrentTransaction?.GetDbTransaction(), cancellationToken: cancellationToken)), cancellationToken);
        return rows.Select(row => row.ToResponse()).ToArray();
    }

    private static async Task<T> ReadAsync<T>(Func<Task<T>> read, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try { return await read(); }
        catch (OracleException exception) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException("Database operation was canceled.", exception, cancellationToken);
        }
    }

    // Bind on each command, without changing ODP.NET process-wide defaults.
    private sealed class BoundParameters(params OracleParameter[] parameters) : SqlMapper.IDynamicParameters
    {
        public void AddParameters(IDbCommand command, SqlMapper.Identity identity)
        {
            var oracle = (OracleCommand)command;
            oracle.BindByName = true;
            oracle.Parameters.AddRange(parameters);
        }
    }

    private sealed class CustomerRow
    {
        public byte[] Id { get; set; } = [];
        public string Name { get; set; } = "";
        public CustomerResponse ToResponse() => new(new Guid(Id), Name);
    }
}
