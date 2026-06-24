namespace BioAccess.Web.DTOs.Api;

public sealed class ApiResponse<T>
{
    public bool Success { get; init; }
    public T? Data { get; init; }
    public string? Error { get; init; }

    public static ApiResponse<T> Ok(T data) =>
        new() { Success = true, Data = data };

    public static ApiResponse<T> Fail(string error) =>
        new() { Success = false, Error = error };
}

public sealed class ApiResponse
{
    public bool Success { get; init; }
    public string? Error { get; init; }

    public static ApiResponse Ok() =>
        new() { Success = true };

    public static ApiResponse Fail(string error) =>
        new() { Success = false, Error = error };
}
