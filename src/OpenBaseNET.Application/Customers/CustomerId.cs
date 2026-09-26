namespace OpenBaseNET.Application.Customers;

internal static class CustomerId
{
    public static void Validate(Guid id)
    {
        if (id == Guid.Empty)
            throw new InputValidationException("CUSTOMER_ID_REQUIRED", "id", "Customer ID must not be empty.");
    }
}
