using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenBaseNET.Domain.Customers;

namespace OpenBaseNET.Infrastructure.Persistence.Oracle;

internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("CUSTOMERS");
        builder.HasKey(customer => customer.Id);
        builder.Property(customer => customer.Id).HasColumnName("ID").HasColumnType("RAW(16)")
            .HasConversion(id => id.ToByteArray(), bytes => new Guid(bytes)).ValueGeneratedNever();
        builder.Property(customer => customer.Name).HasColumnName("NAME")
            .HasColumnType("NVARCHAR2(200)").HasMaxLength(Customer.MaxNameLength).IsUnicode().IsRequired();
        builder.HasIndex(customer => new { customer.Name, customer.Id });
    }
}
