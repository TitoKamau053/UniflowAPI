using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniflowApi.Common;
using UniflowApi.Data;
using UniflowApi.Models;
using UniflowApi.Services;

namespace UniflowApi.Controllers;

[ApiController]
[Route("api/v1/meter-readings")]
[Authorize(Policy = "AdminOnly")]
public class MeterReadingsController : ControllerBase
{
    private readonly StoredProcExecutor _db;
    private readonly ICurrentUser _me;

    public MeterReadingsController(StoredProcExecutor db, ICurrentUser me)
    {
        _db = db;
        _me = me;
    }

    /// <summary>usp_RecordMeterReading auto-resolves PreviousReading from the customer's last reading — the caller only ever supplies the current dial value.</summary>
    [HttpPost]
    public async Task<IActionResult> Record([FromBody] RecordMeterReadingRequest request)
    {
        var customer = await _db.QuerySingleOrDefaultAsync<CustomerRow>(
            "usp_GetCustomerById", new { CustomerID = request.CustomerId, SchemeID = _me.SchemeId });
        if (customer is null)
            throw ApiException.NotFound("Customer not found");

        var p = new DynamicParameters();
        p.Add("CustomerID", request.CustomerId);
        p.Add("ReadingDate", request.ReadingDate);
        p.Add("CurrentReading", request.CurrentReading);
        p.Add("RecordedBy", User.FindFirst("sub")?.Value);
        p.Add("Notes", request.Notes);
        p.Add("NewReadingID", dbType: System.Data.DbType.Int32, direction: System.Data.ParameterDirection.Output);

        var result = await _db.ExecuteWithOutputAsync("usp_RecordMeterReading", p);

        var response = new RecordMeterReadingResult(result.Get<int>("NewReadingID"));
        return Ok(ApiResponse<RecordMeterReadingResult>.Ok(response, "Meter reading recorded"));
    }

    [HttpGet("customer/{customerId:int}")]
    public async Task<IActionResult> ListForCustomer(int customerId)
    {
        var readings = await _db.QueryAsync<MeterReadingRow>("usp_ListMeterReadingsByCustomer", new { CustomerID = customerId });
        return Ok(ApiResponse<IEnumerable<MeterReadingRow>>.Ok(readings));
    }

    /// <summary>Customers with no reading yet for the given month — mirrors Nyanjigi's admin "who still needs a reading" list.</summary>
    [HttpGet("due")]
    public async Task<IActionResult> ListDueForReading([FromQuery] DateOnly month)
    {
        var due = await _db.QueryAsync<CustomerDueForReadingRow>("usp_ListCustomersDueForReading",
            new { SchemeID = _me.SchemeId, Month = month });

        return Ok(ApiResponse<IEnumerable<CustomerDueForReadingRow>>.Ok(due));
    }
}
