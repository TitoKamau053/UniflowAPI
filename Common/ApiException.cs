namespace UniflowApi.Common;

/// Thrown by controllers/services for expected, user-facing failures (bad
/// input, not found, unauthorized, business-rule violations raised by a
/// stored procedure via THROW). The global exception middleware in
/// Program.cs turns this into the standard ApiResponse envelope.
public class ApiException : Exception
{
    public int StatusCode { get; }
    public object? Errors { get; }

    /// Set only when this exception was raised from a database error (see
    /// StoredProcExecutor). This is the SQL Server error number as registered
    /// in dbo.ErrorCodes — the same number a DBA sees querying that table
    /// directly, so a support ticket referencing "error 50011" means the same
    /// thing on both sides.
        public int? DbErrorCode { get; }

    public ApiException(string message, int statusCode = 400, object? errors = null, int? dbErrorCode = null) : base(message)
    {
        StatusCode = statusCode;
        Errors = errors;
        DbErrorCode = dbErrorCode;
    }

    public static ApiException NotFound(string message = "Resource not found") => new(message, 404);
    public static ApiException Unauthorized(string message = "Unauthorized access") => new(message, 401);
    public static ApiException Forbidden(string message = "Access Denied") => new(message, 403);
    public static ApiException Validation(string message, object? errors = null) => new(message, 400, errors);
}
