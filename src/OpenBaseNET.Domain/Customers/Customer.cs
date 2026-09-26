namespace OpenBaseNET.Domain.Customers;

public sealed class Customer
{
    public const int MaxNameLength = 200;

    // EF can bind this constructor; there is no public path to an invalid entity.
    private Customer(Guid id, string name)
    {
        if (id == Guid.Empty)
            throw new DomainValidationException("CUSTOMER_ID_REQUIRED", "id", "Customer ID must not be empty.");

        Id = id;
        Name = ValidateName(name);
    }

    public Guid Id { get; }
    public string Name { get; private set; }

    public static Customer Create(string name) => new(Guid.NewGuid(), name);

    public void Rename(string name) => Name = ValidateName(name);

    // Update uses the same rule before it loads an entity or opens a transaction.
    public static string ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainValidationException("CUSTOMER_NAME_REQUIRED", "name", "Customer name is required.");

        var normalized = name.Trim();
        if (normalized.Length > MaxNameLength)
            throw new DomainValidationException("CUSTOMER_NAME_TOO_LONG", "name", $"Customer name must have at most {MaxNameLength} characters.");

        return normalized;
    }
}
