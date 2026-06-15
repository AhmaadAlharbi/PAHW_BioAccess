namespace BioAccess.Web.DTOs;

public sealed class OperationResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = "";

    public static OperationResult Ok(string message) => new() { Success = true, Message = message };
    public static OperationResult Fail(string message) => new() { Success = false, Message = message };
}
