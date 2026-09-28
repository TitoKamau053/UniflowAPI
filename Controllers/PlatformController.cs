using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniflowApi.Common;
using UniflowApi.Data;
using UniflowApi.Models;
using UniflowApi.Services;

namespace UniflowApi.Controllers;

[ApiController]
[Route("api/v1/platform")]
public class PlatformController : ControllerBase
{
    private readonly StoredProcExecutor _db;
    private readonly ITokenService _tokens;

    public PlatformController(StoredProcExecutor db, ITokenService tokens)
    {
        _db = db;
        _tokens = tokens;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] SuperAdminLoginRequest request)
    {
        var superAdmin = await _db.QuerySingleOrDefaultAsync<SuperAdminLoginRow>(
            "usp_GetSuperAdminForLogin", new { Username = request.Username });

        if (superAdmin is null || !BCrypt.Net.BCrypt.Verify(request.Password, superAdmin.PasswordHash))
            throw ApiException.Unauthorized("Invalid username or password");

        await _db.ExecuteAsync("usp_TouchSuperAdminLastLogin", new { SuperAdminID = superAdmin.SuperAdminID });

        var token = _tokens.GenerateToken(superAdmin.SuperAdminID, null, "superadmin");

        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            IsEssential = true,
            Expires = DateTime.UtcNow.AddMinutes(1440)
        };
        Response.Cookies.Append("water_token", token, cookieOptions);

        var result = new SuperAdminLoginResult(superAdmin.SuperAdminID, superAdmin.Username, 1440);
        return Ok(ApiResponse<SuperAdminLoginResult>.Ok(result, "Login successful"));
    }

    [HttpPost("schemes")]
    [Authorize(Policy = "SuperAdminOnly")]
    public async Task<IActionResult> ProvisionScheme([FromBody] ProvisionSchemeRequest request)
    {
        var passwordHash = BCrypt.Net.BCrypt.HashPassword(request.AdminPassword);

        var p = new Dapper.DynamicParameters();
        p.Add("SchemeName", request.SchemeName);
        p.Add("AdminUsername", request.AdminUsername);
        p.Add("AdminPasswordHash", passwordHash);
        p.Add("AdminEmail", request.AdminEmail);
        p.Add("AdminFullName", request.AdminFullName);
        p.Add("InitialFlatRate", request.InitialFlatRate);
        p.Add("NewSchemeID", dbType: System.Data.DbType.Int32, direction: System.Data.ParameterDirection.Output);
        p.Add("NewAdminID", dbType: System.Data.DbType.Int32, direction: System.Data.ParameterDirection.Output);

        var result = await _db.ExecuteWithOutputAsync("usp_ProvisionScheme", p);

        var response = new ProvisionSchemeResult(result.Get<int>("NewSchemeID"), result.Get<int>("NewAdminID"));
        return Ok(ApiResponse<ProvisionSchemeResult>.Ok(response, "Scheme provisioned successfully"));
    }

    [HttpGet("schemes")]
    [Authorize(Policy = "SuperAdminOnly")]
    public async Task<IActionResult> ListSchemes()
    {
        var schemes = await _db.QueryAsync<SchemeRow>("usp_ListSchemes");
        return Ok(ApiResponse<IEnumerable<SchemeRow>>.Ok(schemes));
    }

    [HttpPost("logout")]
    [Authorize]
    public IActionResult Logout()
    {
        Response.Cookies.Delete("water_token", new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
        });
        return Ok(ApiResponse.Ok("Logout successful"));
    }
}
