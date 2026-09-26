using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenBaseNET.Domain.Customers;

namespace OpenBaseNET.Infrastructure.Persistence.SqlServer;

internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("customers", "dbo");
        builder.HasKey(customer => customer.Id);
        builder.Property(customer => customer.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(customer => customer.Name).HasColumnName("name")
            .HasMaxLength(Customer.MaxNameLength).IsUnicode().IsRequired();
        builder.HasIndex(customer => new { customer.Name, customer.Id });
    }
}
