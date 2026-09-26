namespace OpenBaseNET.Domain;

public sealed class DomainValidationException(string code, string field, string message)
    : Exception(message)
{
    public string Code { get; } = code;
    public string Field { get; } = field;
}
