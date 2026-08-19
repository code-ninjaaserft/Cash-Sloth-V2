namespace CashSloth.Server.Api;

public sealed class ApiProblemException(
    int statusCode,
    string code,
    string message,
    IReadOnlyDictionary<string, string[]>? fieldErrors = null) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
    public IReadOnlyDictionary<string, string[]>? FieldErrors { get; } = fieldErrors;
}
