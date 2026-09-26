namespace OpenBaseNET.Application.Customers;

public sealed class CustomerNotFoundException(Guid id) : Exception("Customer not found.")
{
    public Guid CustomerId { get; } = id;
    public string Code => "CUSTOMER_NOT_FOUND";
}
