using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniflowApi.Common;
using UniflowApi.Data;
using UniflowApi.Models;
using UniflowApi.Services;

namespace UniflowApi.Controllers;

[ApiController]
[Route("api/v1/bills")]
public class BillsController : ControllerBase
{
    private readonly StoredProcExecutor _db;
    private readonly ICurrentUser _me;

    public BillsController(StoredProcExecutor db, ICurrentUser me)
    {
        _db = db;
        _me = me;
    }

    [HttpPost("generate")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> GenerateMonthlyBills([FromBody] GenerateMonthlyBillsRequest request)
    {
        await _db.ExecuteAsync("usp_GenerateMonthlyBills", new
        {
            SchemeID = _me.SchemeId,
            PeriodStart = request.PeriodStart.ToDateTime(TimeOnly.MinValue),
            PeriodEnd = request.PeriodEnd.ToDateTime(TimeOnly.MinValue),
            DueDate = request.DueDate.ToDateTime(TimeOnly.MinValue)
        });

        return Ok(ApiResponse.Ok("Monthly bills generated"));
    }

    [HttpPost("mark-overdue")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> MarkOverdueBills()
    {
        await _db.ExecuteAsync("usp_MarkOverdueBills", new { SchemeID = _me.SchemeId });
        return Ok(ApiResponse.Ok("Overdue bills updated"));
    }

    [HttpGet]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> ListForScheme([FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var bills = await _db.QueryAsync<BillRow>("usp_ListBillsByScheme",
            new { SchemeID = _me.SchemeId, Status = status, Page = page, PageSize = pageSize });

        return Ok(ApiResponse<IEnumerable<BillRow>>.Ok(bills));
    }

    [HttpGet("customer/{customerId:int}")]
    [Authorize]
    public async Task<IActionResult> ListForCustomer(int customerId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        if (_me.IsCustomer && _me.UserId != customerId)
            throw ApiException.Forbidden();

        var bills = await _db.QueryAsync<BillRow>("usp_ListBillsByCustomer",
            new { CustomerID = customerId, Page = page, PageSize = pageSize });

        return Ok(ApiResponse<IEnumerable<BillRow>>.Ok(bills));
    }

    [HttpGet("me")]
    [Authorize(Policy = "CustomerOnly")]
    public async Task<IActionResult> ListMine([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var bills = await _db.QueryAsync<BillRow>("usp_ListBillsByCustomer", new { CustomerID = _me.UserId, Page = page, PageSize = pageSize });
        return Ok(ApiResponse<IEnumerable<BillRow>>.Ok(bills));
    }

    [HttpGet("stats")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Stats()
    {
        var stats = await _db.QuerySingleOrDefaultAsync<dynamic>("usp_GetBillStats", new { SchemeID = _me.SchemeId });
        return Ok(ApiResponse<dynamic>.Ok(stats));
    }

    [HttpGet("overdue")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Overdue([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var overdue = await _db.QueryAsync<dynamic>("usp_ListOverdueBills", new { SchemeID = _me.SchemeId, Page = page, PageSize = pageSize });
        return Ok(ApiResponse<IEnumerable<dynamic>>.Ok(overdue));
    }

    [HttpGet("summary")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Summary([FromQuery] DateOnly periodStart, [FromQuery] DateOnly periodEnd)
    {
        var summary = await _db.QuerySingleOrDefaultAsync<dynamic>("usp_GetBillingSummary",
            new { SchemeID = _me.SchemeId, PeriodStart = periodStart, PeriodEnd = periodEnd });
        return Ok(ApiResponse<dynamic>.Ok(summary));
    }

    [HttpGet("{billId:int}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> GetById(int billId)
    {
        var bill = await _db.QuerySingleOrDefaultAsync<BillRow>("usp_GetBillById", new { BillID = billId, SchemeID = _me.SchemeId });
        if (bill is null) throw ApiException.NotFound("Bill not found");
        return Ok(ApiResponse<BillRow>.Ok(bill));
    }

    [HttpPut("{billId:int}/status")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> UpdateStatus(int billId, [FromBody] UpdateBillStatusRequest request)
    {
        await _db.ExecuteAsync("usp_UpdateBillStatus", new { BillID = billId, SchemeID = _me.SchemeId, request.Status });
        return Ok(ApiResponse.Ok("Bill status updated"));
    }

    [HttpPut("bulk/status")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> BulkUpdateStatus([FromBody] BulkUpdateBillStatusRequest request)
    {
        await _db.ExecuteAsync("usp_BulkUpdateBillStatus", new
        {
            BillIDsCsv = string.Join(',', request.BillIds), SchemeID = _me.SchemeId, request.Status
        });
        return Ok(ApiResponse.Ok("Bill statuses updated"));
    }

    [HttpDelete("{billId:int}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Delete(int billId)
    {
        await _db.ExecuteAsync("usp_DeleteBill", new { BillID = billId, SchemeID = _me.SchemeId });
        return Ok(ApiResponse.Ok("Bill deleted"));
    }
}
