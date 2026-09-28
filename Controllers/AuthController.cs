using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniflowApi.Common;
using UniflowApi.Data;
using UniflowApi.Models;
using UniflowApi.Services;

namespace UniflowApi.Controllers;

[ApiController]
[Route("api/v1/auth")]
public class AuthController : ControllerBase
{
    private readonly StoredProcExecutor _db;
    private readonly ITokenService _tokens;
    private readonly ICurrentUser _me;

    public AuthController(StoredProcExecutor db, ITokenService tokens, ICurrentUser me)
    {
        _db = db;
        _tokens = tokens;
        _me = me;
    }

    [NonAction]
    public void SetTokenCookie(string token, int expiryMinutes)
    {
        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            IsEssential = true,
            Expires = DateTime.UtcNow.AddMinutes(expiryMinutes)
        };

        Response.Cookies.Append("water_token", token, cookieOptions);
    }

    [HttpPost("admin/login")]
    public async Task<IActionResult> AdminLogin([FromBody] AdminLoginRequest request)
    {
        var admin = await _db.QuerySingleOrDefaultAsync<AdminLoginRow>(
            "usp_GetAdminForLogin", new { Username = request.Username });

        if (admin is null || !BCrypt.Net.BCrypt.Verify(request.Password, admin.PasswordHash))
            throw ApiException.Unauthorized("Invalid username or password");

        await _db.ExecuteAsync("usp_TouchAdminLastLogin", new { AdminID = admin.AdminID });

        var token = _tokens.GenerateToken(admin.AdminID, admin.SchemeID, "admin");

        SetTokenCookie(token, 1440);

        var result = new AdminLoginResult(admin.AdminID, admin.SchemeID, admin.Username, admin.FullName, admin.Email,
            1440);
        return Ok(ApiResponse<AdminLoginResult>.Ok(result, "Login successful"));
    }

    [HttpGet("admin/profile")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> GetAdminProfile()
    {
        var profile =
            await _db.QuerySingleOrDefaultAsync<AdminProfileRow>("usp_GetAdminProfile", new { AdminID = _me.UserId });
        if (profile is null) throw ApiException.NotFound("Admin not found");
        return Ok(ApiResponse<AdminProfileRow>.Ok(profile));
    }

    [HttpPut("admin/profile")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> UpdateAdminProfile([FromBody] UpdateAdminProfileRequest request)
    {
        await _db.ExecuteAsync("usp_UpdateAdminProfile", new { AdminID = _me.UserId, request.FullName, request.Email });
        return Ok(ApiResponse.Ok("Profile updated"));
    }

    [HttpPost("admin/change-password")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> ChangeAdminPassword([FromBody] ChangePasswordRequest request)
    {
        var current =
            await _db.QuerySingleOrDefaultAsync<PasswordHashRow>("usp_GetAdminPasswordHash",
                new { AdminID = _me.UserId });
        if (current is null || !BCrypt.Net.BCrypt.Verify(request.CurrentPassword, current.PasswordHash))
            throw ApiException.Unauthorized("Current password is incorrect");

        var newHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        await _db.ExecuteAsync("usp_ChangeAdminPassword", new { AdminID = _me.UserId, NewPasswordHash = newHash });
        return Ok(ApiResponse.Ok("Password changed"));
    }

    [HttpPost("customer/login")]
    public async Task<IActionResult> CustomerLogin([FromBody] CustomerLoginRequest request)
    {
        var customer = await _db.QuerySingleOrDefaultAsync<CustomerLoginRow>(
            "usp_GetCustomerForLogin", new { AccountNo = request.AccountNo });

        if (customer is null || string.IsNullOrEmpty(customer.PasswordHash)
                             || !BCrypt.Net.BCrypt.Verify(request.Password, customer.PasswordHash))
            throw ApiException.Unauthorized("Invalid account number or password");

        await _db.ExecuteAsync("usp_TouchCustomerLastLogin", new { CustomerID = customer.CustomerID });

        var token = _tokens.GenerateToken(customer.CustomerID, customer.SchemeID, "customer");

        SetTokenCookie(token, 1440);

        var result = new CustomerLoginResult(customer.CustomerID, customer.SchemeID, customer.AccountNo,
            customer.FullName, customer.Zone, 1440);
        return Ok(ApiResponse<CustomerLoginResult>.Ok(result, "Login successful"));
    }


    [HttpGet("customer/profile")]
    [Authorize(Policy = "CustomerOnly")]
    public async Task<IActionResult> GetCustomerProfile()
    {
        var profile = await _db.QuerySingleOrDefaultAsync<CustomerRow>(
            "usp_GetCustomerById", new { CustomerID = _me.UserId, SchemeID = _me.SchemeId });
        if (profile is null) throw ApiException.NotFound("Customer not found");
        return Ok(ApiResponse<CustomerRow>.Ok(profile));
    }

    [HttpPut("customer/profile")]
    [Authorize(Policy = "CustomerOnly")]
    public async Task<IActionResult> UpdateCustomerProfile([FromBody] UpdateCustomerProfileRequest request)
    {
        await _db.ExecuteAsync("usp_UpdateCustomerProfile",
            new { CustomerID = _me.UserId, request.FullName, request.Phone, request.Location });
        return Ok(ApiResponse.Ok("Profile updated"));
    }

    [HttpPost("customer/change-password")]
    [Authorize(Policy = "CustomerOnly")]
    public async Task<IActionResult> ChangeCustomerPassword([FromBody] ChangePasswordRequest request)
    {
        var current =
            await _db.QuerySingleOrDefaultAsync<PasswordHashRow>("usp_GetCustomerPasswordHash",
                new { CustomerID = _me.UserId });
        if (current is null || string.IsNullOrEmpty(current.PasswordHash) ||
            !BCrypt.Net.BCrypt.Verify(request.CurrentPassword, current.PasswordHash))
            throw ApiException.Unauthorized("Current password is incorrect");

        var newHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        await _db.ExecuteAsync("usp_SetCustomerPassword", new { CustomerID = _me.UserId, PasswordHash = newHash });
        return Ok(ApiResponse.Ok("Password changed"));
    }

    [HttpGet("customer/dashboard")]
    [Authorize(Policy = "CustomerOnly")]
    public async Task<IActionResult> CustomerDashboard()
    {
        var summary = await _db.QuerySingleOrDefaultAsync<dynamic>(
            "usp_GetCustomerAccountSummary", new { CustomerID = _me.UserId, SchemeID = _me.SchemeId });
        var recentBills = await _db.QueryAsync<BillRow>("usp_ListBillsByCustomer",
            new { CustomerID = _me.UserId, Page = 1, PageSize = 5 });
        var recentPayments = await _db.QueryAsync<PaymentRow>("usp_ListPaymentsByCustomer",
            new { CustomerID = _me.UserId, Page = 1, PageSize = 5 });

        return Ok(ApiResponse<object>.Ok(new { summary, recentBills, recentPayments }));
    }

    [HttpGet("validate")]
    [Authorize]
    public IActionResult Validate()
    {
        var result = new TokenValidationResult(_me.UserId, _me.UserType == "superadmin" ? null : _me.SchemeId,
            _me.UserType, true);
        return Ok(ApiResponse<TokenValidationResult>.Ok(result));
    }

    [HttpPost("logout")]
    [Authorize]
    public IActionResult Logout()
    {
        Response.Cookies.Delete("water_token", new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict
        });
        return Ok(ApiResponse.Ok("Logged out successfully"));
    }
}
