namespace OpenBaseNET.Application.Customers;

/// <summary>A bounded page, ordered by Name ASC and then unique Id ASC by the adapter.</summary>
public sealed class CustomerPage
{
    public const int DefaultSize = 20;
    public const int MaxSize = 100;

    public CustomerPage(int number = 1, int size = DefaultSize)
    {
        if (number < 1)
            throw new InputValidationException("PAGE_NUMBER_OUT_OF_RANGE", "pageNumber", "Page number must be positive.");

        if (size is < 1 or > MaxSize)
            throw new InputValidationException("PAGE_SIZE_OUT_OF_RANGE", "pageSize", $"Page size must be between 1 and {MaxSize}.");

        var offset = ((long)number - 1) * size;
        if (offset > int.MaxValue)
            throw new InputValidationException("PAGE_NUMBER_OUT_OF_RANGE", "pageNumber", "Page offset exceeds the supported range.");

        Number = number;
        Size = size;
        Offset = (int)offset;
    }

    public int Number { get; }
    public int Size { get; }
    public int Offset { get; }
}
