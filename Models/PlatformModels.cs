namespace UniflowApi.Models;

public record SuperAdminLoginRequest(string Username, string Password);
public record SuperAdminLoginResult(int SuperAdminId, string Username, int ExpiresInMinutes);

public class SuperAdminLoginRow
{
    public int SuperAdminID { get; set; }
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}

public record ProvisionSchemeRequest(
    string SchemeName,
    string AdminUsername,
    string AdminPassword,
    string? AdminEmail,
    string AdminFullName,
    decimal InitialFlatRate);

public record ProvisionSchemeResult(int SchemeId, int AdminId);

public class SchemeRow
{
    public int SchemeID { get; set; }
    public string SchemeName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}
