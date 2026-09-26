using OpenBaseNET.Domain;
using OpenBaseNET.Domain.Customers;

namespace OpenBaseNET.Tests.Unit;

public sealed class CustomerTests
{
    [Fact]
    public void Creation_assigns_identity_and_normalizes_name()
    {
        var customer = Customer.Create("  Ana Silva  ");
        Assert.NotEqual(Guid.Empty, customer.Id);
        Assert.Equal("Ana Silva", customer.Name);
        Assert.NotEqual(customer.Id, Customer.Create("Ana Silva").Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t\r\n ")]
    public void Empty_names_cannot_create_entities(string? name)
    {
        var error = Assert.Throws<DomainValidationException>(() => Customer.Create(name!));
        Assert.Equal("CUSTOMER_NAME_REQUIRED", error.Code);
        Assert.Equal("name", error.Field);
    }

    [Fact]
    public void Limit_applies_to_normalized_name()
    {
        var name = new string('a', Customer.MaxNameLength);
        Assert.Equal(name, Customer.Create($"  {name}  ").Name);
        var error = Assert.Throws<DomainValidationException>(() => Customer.Create(name + "a"));
        Assert.Equal("CUSTOMER_NAME_TOO_LONG", error.Code);
    }

    [Fact]
    public void Rename_preserves_identity_and_invalid_rename_preserves_state()
    {
        var customer = Customer.Create("Ana");
        var id = customer.Id;
        customer.Rename("  Beatriz  ");
        Assert.Equal(id, customer.Id);
        Assert.Equal("Beatriz", customer.Name);

        Assert.Throws<DomainValidationException>(() => customer.Rename(" "));
        Assert.Throws<DomainValidationException>(() => customer.Rename(new string('a', 201)));
        Assert.Equal("Beatriz", customer.Name);
        Assert.Equal(id, customer.Id);
    }

    [Fact]
    public void Public_api_cannot_bypass_invariants()
    {
        Assert.Empty(typeof(Customer).GetConstructors());
        Assert.All(typeof(Customer).GetProperties(), property => Assert.False(property.SetMethod?.IsPublic ?? false));
    }
}
