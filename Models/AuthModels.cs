namespace UniflowApi.Models;

public record AdminLoginRequest(string Username, string Password);
public record CustomerLoginRequest(string AccountNo, string Password);

public record AdminLoginResult(int AdminId, int SchemeId, string Username, string FullName, string? Email, int ExpiresInMinutes);
public record CustomerLoginResult(int CustomerId, int SchemeId, string AccountNo, string FullName, string Zone, int ExpiresInMinutes);

// Row shapes returned by the read-only login lookup procs (Data/Auth Additions script)
public class AdminLoginRow
{
    public int AdminID { get; set; }
    public int SchemeID { get; set; }
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string FullName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}

public class CustomerLoginRow
{
    public int CustomerID { get; set; }
    public int SchemeID { get; set; }
    public string AccountNo { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Zone { get; set; } = string.Empty;
    public string? PasswordHash { get; set; }
    public bool IsActive { get; set; }
}

// --- extended auth: profile & password ---
public record UpdateAdminProfileRequest(string FullName, string? Email);
public record UpdateCustomerProfileRequest(string FullName, string? Phone, string? Location);
public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public record TokenValidationResult(int UserId, int? SchemeId, string UserType, bool Valid);

public class PasswordHashRow
{
    public string PasswordHash { get; set; } = string.Empty;
}

public class AdminProfileRow
{
    public int AdminID { get; set; }
    public int SchemeID { get; set; }
    public string Username { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string FullName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime? LastLogin { get; set; }
    public DateTime CreatedAt { get; set; }
}
