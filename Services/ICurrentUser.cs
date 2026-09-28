using System.Security.Claims;

namespace UniflowApi.Services;

public interface ICurrentUser
{
    int SchemeId { get; }
    int UserId { get; }
    string UserType { get; }
    bool IsAdmin { get; }
    bool IsCustomer { get; }
    bool IsEquityBiller { get; }
    bool IsSuperAdmin { get; }
}

public class CurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _accessor;

    public CurrentUser(IHttpContextAccessor accessor) => _accessor = accessor;

    private ClaimsPrincipal User => _accessor.HttpContext?.User
        ?? throw new InvalidOperationException("No HTTP context available.");

    public int SchemeId => int.Parse(User.FindFirst("scheme_id")?.Value
        ?? throw new UnauthorizedAccessException("Token missing scheme_id claim."));

    public int UserId => int.Parse(User.FindFirst("sub")?.Value
        ?? throw new UnauthorizedAccessException("Token missing sub claim."));

    public string UserType => User.FindFirst("type")?.Value
        ?? throw new UnauthorizedAccessException("Token missing type claim.");

    public bool IsAdmin => UserType == "admin";
    public bool IsCustomer => UserType == "customer";
    public bool IsEquityBiller => UserType == "equity_biller";
    public bool IsSuperAdmin => UserType == "superadmin";
}
