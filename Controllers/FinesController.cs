using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniflowApi.Common;
using UniflowApi.Data;
using UniflowApi.Models;
using UniflowApi.Services;

namespace UniflowApi.Controllers;

/// <summary>
/// Fines are ADMIN-ENTERED, never system-calculated. There is no "assess
/// late fines" bulk job — an admin decides the amount and reason for every
/// fine, matching how the client's system is actually operated.
/// </summary>
[ApiController]
[Route("api/v1/fines")]
public class FinesController : ControllerBase
{
    private readonly StoredProcExecutor _db;
    private readonly ICurrentUser _me;

    public FinesController(StoredProcExecutor db, ICurrentUser me)
    {
        _db = db;
        _me = me;
    }

    [HttpPost]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Apply([FromBody] ApplyFineRequest request)
    {
        // Tenant check: the customer (and bill, if given) must belong to the caller's own scheme.
        var customer = await _db.QuerySingleOrDefaultAsync<CustomerRow>(
            "usp_GetCustomerById", new { CustomerID = request.CustomerId, SchemeID = _me.SchemeId });
        if (customer is null)
            throw ApiException.NotFound("Customer not found");

        var p = new DynamicParameters();
        p.Add("CustomerID", request.CustomerId);
        p.Add("BillID", request.BillId);
        p.Add("Amount", request.Amount);
        p.Add("Reason", request.Reason);
        p.Add("AppliedDate", request.AppliedDate);
        p.Add("NewFineID", dbType: System.Data.DbType.Int32, direction: System.Data.ParameterDirection.Output);

        var result = await _db.ExecuteWithOutputAsync("usp_ApplyFine", p);

        var response = new ApplyFineResult(result.Get<int>("NewFineID"));
        return Ok(ApiResponse<ApplyFineResult>.Ok(response, "Fine applied successfully"));
    }

    [HttpGet]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> ListAll([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        // No usp_ListAllFinesByScheme was in the original set — reuse the
        // scheme-scoped bill listing's pattern isn't right here since fines
        // aren't bill-shaped, so this calls straight through to a proc.
        var fines = await _db.QueryAsync<FineRow>("usp_ListFinesByScheme", new { SchemeID = _me.SchemeId, Page = page, PageSize = pageSize });
        return Ok(ApiResponse<IEnumerable<FineRow>>.Ok(fines));
    }

    [HttpGet("{fineId:int}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> GetById(int fineId)
    {
        var fine = await _db.QuerySingleOrDefaultAsync<FineRow>("usp_GetFineById", new { FineID = fineId, SchemeID = _me.SchemeId });
        if (fine is null) throw ApiException.NotFound("Fine not found");
        return Ok(ApiResponse<FineRow>.Ok(fine));
    }

    [HttpPut("{fineId:int}/status")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> UpdateStatus(int fineId, [FromBody] UpdateFineStatusRequest request)
    {
        await _db.ExecuteAsync("usp_UpdateFineStatus", new { FineID = fineId, SchemeID = _me.SchemeId, request.Status });
        return Ok(ApiResponse.Ok("Fine status updated"));
    }

    [HttpGet("customer/{customerId:int}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> ListForCustomer(int customerId)
    {
        var fines = await _db.QueryAsync<FineRow>("usp_ListFinesByCustomer", new { CustomerID = customerId });
        return Ok(ApiResponse<IEnumerable<FineRow>>.Ok(fines));
    }

    /// <summary>The logged-in customer's own fines — mirrors Nyanjigi's GET /fines/me.</summary>
    [HttpGet("me")]
    [Authorize(Policy = "CustomerOnly")]
    public async Task<IActionResult> ListMine()
    {
        var fines = await _db.QueryAsync<FineRow>("usp_ListFinesByCustomer", new { CustomerID = _me.UserId });
        return Ok(ApiResponse<IEnumerable<FineRow>>.Ok(fines));
    }
}
