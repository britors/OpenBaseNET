using Microsoft.AspNetCore.Mvc;
using OpenBaseNET.Application.Customers;

namespace OpenBaseNET.Api.Controllers;

[ApiController]
[Route("api/customers")]
public sealed class CustomersController : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<CustomerResponse>> Create(CustomerInput input,
        [FromServices] CreateCustomer create, CancellationToken cancellationToken)
    {
        var customer = await create.ExecuteAsync(input.Name!, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = customer.Id }, customer);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CustomerResponse>> Get(Guid id,
        [FromServices] GetCustomer get, CancellationToken cancellationToken) =>
        Ok(await get.ExecuteAsync(id, cancellationToken));

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CustomerResponse>>> List([FromServices] ListCustomers list,
        CancellationToken cancellationToken, [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20) =>
        Ok(await list.ExecuteAsync(pageNumber, pageSize, cancellationToken));

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<CustomerResponse>> Update(Guid id, CustomerInput input,
        [FromServices] UpdateCustomer update, CancellationToken cancellationToken) =>
        Ok(await update.ExecuteAsync(id, input.Name!, cancellationToken));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id,
        [FromServices] DeleteCustomer delete, CancellationToken cancellationToken)
    {
        await delete.ExecuteAsync(id, cancellationToken);
        return NoContent();
    }
}

public sealed record CustomerInput(string? Name);
