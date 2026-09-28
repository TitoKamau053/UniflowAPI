using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniflowApi.Common;
using UniflowApi.Data;
using UniflowApi.Models;
using UniflowApi.Services;

namespace UniflowApi.Controllers;

[ApiController]
[Route("api/v1/customers")]
[Authorize(Policy = "AdminOnly")]
public class CustomersController : ControllerBase
{
    private readonly StoredProcExecutor _db;
    private readonly ICurrentUser _me;

    public CustomersController(StoredProcExecutor db, ICurrentUser me)
    {
        _db = db;
        _me = me;
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateCustomerRequest request)
    {
        var nationalId = request.NationalID.Trim();
        var accountNo = request.AccountNo.Trim();

        if (string.IsNullOrWhiteSpace(request.FullName))
            throw ApiException.Validation("Full name is required");

        if (string.IsNullOrWhiteSpace(nationalId))
            throw ApiException.Validation("National ID is required");

        if (string.IsNullOrWhiteSpace(accountNo))
            throw ApiException.Validation("Account number is required");

        if (string.IsNullOrWhiteSpace(request.Zone))
            throw ApiException.Validation("Zone is required");

        var passwordHash =
            BCrypt.Net.BCrypt.HashPassword(nationalId);

        var p = new DynamicParameters();

        p.Add("SchemeID", _me.SchemeId);
        p.Add("AccountNo", accountNo);
        p.Add("FullName", request.FullName.Trim());
        p.Add("NationalID", nationalId);
        p.Add("PasswordHash", passwordHash);
        p.Add("Phone", request.Phone);
        p.Add("Location", request.Location);
        p.Add("Zone", request.Zone);
        p.Add("MeterNumber", request.MeterNumber);
        p.Add("ConnectionDate",request.ConnectionDate?.ToDateTime(TimeOnly.MinValue),dbType: System.Data.DbType.Date);
        p.Add("NewCustomerID",dbType: System.Data.DbType.Int32,direction: System.Data.ParameterDirection.Output);

        var result = await _db.ExecuteWithOutputAsync("usp_CreateCustomer", p);
        var customerId = result.Get<int>("NewCustomerID");

        return Ok(
            ApiResponse<CreateCustomerResult>.Ok(
                new CreateCustomerResult(customerId, accountNo),
                "Customer created successfully"));
    }

    [HttpGet("{customerId:int}")]
    public async Task<IActionResult> GetById(int customerId)
    {
        var customer = await _db.QuerySingleOrDefaultAsync<CustomerRow>(
            "usp_GetCustomerById", new { CustomerID = customerId, SchemeID = _me.SchemeId });

        if (customer is null)
            throw ApiException.NotFound("Customer not found");

        return Ok(ApiResponse<CustomerRow>.Ok(customer));
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var customers = await _db.QueryAsync<CustomerRow>("usp_ListCustomersByScheme",
            new { SchemeID = _me.SchemeId, Search = search, Page = page, PageSize = pageSize });

        return Ok(ApiResponse<IEnumerable<CustomerRow>>.Ok(customers));
    }

    [HttpGet("{customerId:int}/account-summary")]
    public async Task<IActionResult> AccountSummary(int customerId)
    {
        var summary = await _db.QuerySingleOrDefaultAsync<dynamic>(
            "usp_GetCustomerAccountSummary", new { CustomerID = customerId, SchemeID = _me.SchemeId });

        if (summary is null)
            throw ApiException.NotFound("Customer not found");

        return Ok(ApiResponse<dynamic>.Ok(summary));
    }

    [HttpPut("{customerId:int}")]
    public async Task<IActionResult> Update(int customerId, [FromBody] UpdateCustomerRequest request)
    {
        await _db.ExecuteAsync("usp_UpdateCustomer", new
        {
            CustomerID = customerId, SchemeID = _me.SchemeId,
            request.FullName, request.Phone, request.Location, request.Zone, request.MeterNumber
        });
        return Ok(ApiResponse.Ok("Customer updated"));
    }

    [HttpPost("{customerId:int}/toggle-status")]
    public async Task<IActionResult> ToggleStatus(int customerId)
    {
        await _db.ExecuteAsync("usp_ToggleCustomerStatus", new { CustomerID = customerId, SchemeID = _me.SchemeId });
        return Ok(ApiResponse.Ok("Customer status toggled"));
    }

    [HttpPost("{customerId:int}/reset-password")]
    public async Task<IActionResult> ResetPassword(int customerId)
    {
        var temporaryPassword = Guid.NewGuid().ToString("N")[..10];
        var hash = BCrypt.Net.BCrypt.HashPassword(temporaryPassword);

        await _db.ExecuteAsync("usp_ResetCustomerPassword", new
        {
            CustomerID = customerId, SchemeID = _me.SchemeId,
            NewPasswordHash = hash, PlaintextForSms = temporaryPassword
        });

        return Ok(ApiResponse<ResetPasswordResult>.Ok(new ResetPasswordResult(temporaryPassword), "Password reset — SMS queued"));
    }

    [HttpPost("{customerId:int}/adjust-balance")]
    public async Task<IActionResult> AdjustBalance(int customerId, [FromBody] AdjustBalanceRequest request)
    {
        var p = new DynamicParameters();
        p.Add("CustomerID", customerId);
        p.Add("SchemeID", _me.SchemeId);
        p.Add("Amount", request.Amount);
        p.Add("Reason", request.Reason);
        p.Add("NewAdjustmentID", dbType: System.Data.DbType.Int32, direction: System.Data.ParameterDirection.Output);

        var result = await _db.ExecuteWithOutputAsync("usp_AdjustCustomerBalance", p);
        return Ok(ApiResponse<AdjustBalanceResult>.Ok(new AdjustBalanceResult(result.Get<int>("NewAdjustmentID")), "Balance adjusted"));
    }

    [HttpGet("stats")]
    public async Task<IActionResult> Stats()
    {
        var stats = await _db.QuerySingleOrDefaultAsync<CustomerStatsRow>("usp_GetCustomerStats", new { SchemeID = _me.SchemeId });
        return Ok(ApiResponse<CustomerStatsRow?>.Ok(stats));
    }

    [HttpGet("analytics/zones")]
    public async Task<IActionResult> ZoneAnalytics()
    {
        var zones = await _db.QueryAsync<ZoneAnalyticsRow>("usp_GetZoneAnalytics", new { SchemeID = _me.SchemeId });
        return Ok(ApiResponse<IEnumerable<ZoneAnalyticsRow>>.Ok(zones));
    }
}
