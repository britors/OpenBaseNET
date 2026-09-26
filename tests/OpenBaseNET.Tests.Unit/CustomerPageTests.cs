using OpenBaseNET.Application;
using OpenBaseNET.Application.Customers;

namespace OpenBaseNET.Tests.Unit;

public sealed class CustomerPageTests
{
    [Fact]
    public void Defaults_start_at_first_page()
    {
        var page = new CustomerPage();
        Assert.Equal(1, page.Number);
        Assert.Equal(20, page.Size);
        Assert.Equal(0, page.Offset);
    }

    [Theory]
    [InlineData(0, 20, "PAGE_NUMBER_OUT_OF_RANGE", "pageNumber")]
    [InlineData(-1, 20, "PAGE_NUMBER_OUT_OF_RANGE", "pageNumber")]
    [InlineData(int.MinValue, 20, "PAGE_NUMBER_OUT_OF_RANGE", "pageNumber")]
    [InlineData(1, 0, "PAGE_SIZE_OUT_OF_RANGE", "pageSize")]
    [InlineData(1, -1, "PAGE_SIZE_OUT_OF_RANGE", "pageSize")]
    [InlineData(1, 101, "PAGE_SIZE_OUT_OF_RANGE", "pageSize")]
    [InlineData(int.MaxValue, 100, "PAGE_NUMBER_OUT_OF_RANGE", "pageNumber")]
    public void Invalid_or_overflowing_pages_are_rejected(int number, int size, string code, string field)
    {
        var error = Assert.Throws<InputValidationException>(() => new CustomerPage(number, size));
        Assert.Equal(code, error.Code);
        Assert.Equal(field, error.Field);
    }

    [Theory]
    [InlineData(1, 1, 0)]
    [InlineData(3, 100, 200)]
    [InlineData(int.MaxValue, 1, int.MaxValue - 1)]
    [InlineData(21474837, 100, 2147483600)]
    public void Offset_is_bounded_without_integer_overflow(int number, int size, int offset)
    {
        var page = new CustomerPage(number, size);
        Assert.Equal(offset, page.Offset);
        Assert.Equal(size, page.Size);
    }
}
