using System.Security.Claims;
using UniflowApi.Common;

namespace UniflowApi.Services;

public interface ICurrentUser
{
    int? SchemeIdOrNull { get; }
    int SchemeId { get; }
    int UserId { get; }
    string UserType { get; }
    bool IsAdmin { get; }
    bool IsCustomer { get; }
    bool IsEquityBiller { get; }
    bool IsSuperAdmin { get; }
    int ResolveSchemeId(int? requestSchemeId = null);
}

public class CurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _accessor;

    public CurrentUser(IHttpContextAccessor accessor) => _accessor = accessor;

    private ClaimsPrincipal User => _accessor.HttpContext?.User
        ?? throw new InvalidOperationException("No HTTP context available.");

    private string? FindClaim(params string[] names)
    {
        foreach (var name in names)
        {
            var v = User.FindFirst(name)?.Value;
            if (!string.IsNullOrEmpty(v)) return v;
        }
        return null;
    }

    private int? SchemeIdFromQuery()
    {
        var req = _accessor.HttpContext?.Request;
        if (req is null) return null;

        var raw = req.Query["schemeId"].FirstOrDefault()
               ?? req.Query["SchemeID"].FirstOrDefault()
               ?? req.Query["SchemeId"].FirstOrDefault();

        return int.TryParse(raw, out var id) && id > 0 ? id : null;
    }

    public int? SchemeIdOrNull
    {
        get
        {
            var raw = FindClaim("scheme_id", "schemeId");
            return int.TryParse(raw, out var id) ? id : null;
        }
    }

    public int SchemeId
    {
        get
        {
            if (SchemeIdOrNull is int fromToken)
                return fromToken;

            if (SchemeIdFromQuery() is int fromQuery)
                return fromQuery;

            throw ApiException.Validation(
                "schemeId is required when acting as platform SuperAdmin. Pass ?schemeId=N on the request.");
        }
    }

    public int UserId
    {
        get
        {
            var raw = FindClaim("sub", ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(raw) || !int.TryParse(raw, out var id))
                throw ApiException.Unauthorized("Token missing sub claim.");
            return id;
        }
    }

    public string UserType
    {
        get
        {
            var raw = FindClaim("type", "user_type", "userType");
            if (string.IsNullOrEmpty(raw))
                throw ApiException.Unauthorized("Token missing type claim.");
            return raw.Trim().ToLowerInvariant();
        }
    }

    public bool IsAdmin => UserType == "admin";
    public bool IsCustomer => UserType == "customer";
    public bool IsEquityBiller => UserType == "equity_biller";
    public bool IsSuperAdmin => UserType == "superadmin";

    public int ResolveSchemeId(int? requestSchemeId = null)
    {
        if (SchemeIdOrNull is int fromToken)
            return fromToken;

        if (requestSchemeId is int explicitId && explicitId > 0)
            return explicitId;

        if (SchemeIdFromQuery() is int fromQuery)
            return fromQuery;

        throw ApiException.Validation(
            "schemeId is required when acting as platform SuperAdmin. Pass ?schemeId=N on the request.");
    }
}
