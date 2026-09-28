using System.Text.Json.Serialization;

namespace UniflowApi.Common;

/// Mirrors the Nyanjigi Node API's response envelope exactly, so any existing
/// frontend built against { success, message, data, timestamp } keeps working
/// unchanged against this API.
public class ApiResponse<T>
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("data")]
    public T? Data { get; set; }

    [JsonPropertyName("errors")]
    public object? Errors { get; set; }

    /// Populated only for failures that originated as a registered database
    /// error (dbo.ErrorCodes) — e.g. 50011. Null for ordinary API-level
    /// failures (validation attribute failures, 404s the API itself decided).
        [JsonPropertyName("errorCode")]
    public int? ErrorCode { get; set; }

    [JsonPropertyName("timestamp")]
    public string Timestamp { get; set; } = DateTime.UtcNow.ToString("o");

    public static ApiResponse<T> Ok(T? data, string message = "Success") => new()
    {
        Success = true,
        Message = message,
        Data = data
    };

    public static ApiResponse<T> Fail(string message, object? errors = null, int? errorCode = null) => new()
    {
        Success = false,
        Message = message,
        Errors = errors,
        ErrorCode = errorCode
    };
}

/// Non-generic helper for endpoints with no payload (e.g. 204-style acks)
public static class ApiResponse
{
    public static ApiResponse<object?> Ok(string message = "Success") => ApiResponse<object?>.Ok(null, message);
    public static ApiResponse<object?> Fail(string message, object? errors = null, int? errorCode = null) => ApiResponse<object?>.Fail(message, errors, errorCode);
}
