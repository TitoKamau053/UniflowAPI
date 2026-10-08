using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniflowApi.Common;
using UniflowApi.Data;
using UniflowApi.Models;
using UniflowApi.Services;

namespace UniflowApi.Controllers;

[ApiController]
[Route("api/v1/receipts")]
public class ReceiptsController : ControllerBase
{
    private readonly StoredProcExecutor _db;
    private readonly ICurrentUser _me;
    private readonly IReceiptPdfService _pdf;
    private readonly ISmsGateway _sms;

    public ReceiptsController(StoredProcExecutor db, ICurrentUser me, IReceiptPdfService pdf, ISmsGateway sms)
    {
        _db = db;
        _me = me;
        _pdf = pdf;
        _sms = sms;
    }

    //Scheme-wide receipt list (admin + superadmin with ?schemeId=).
    [HttpGet]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> ListAll([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] int? schemeId = null)
    {
        var receipts = await _db.QueryAsync<dynamic>("usp_ListAllPayments",
            new { SchemeID = _me.ResolveSchemeId(schemeId), Page = page, PageSize = pageSize });
        return Ok(ApiResponse<IEnumerable<dynamic>>.Ok(receipts));
    }

    [HttpGet("summary")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Summary([FromQuery] int? schemeId = null)
    {
        var summary = await _db.QuerySingleOrDefaultAsync<dynamic>("usp_GetReceiptsSummary",
            new { SchemeID = _me.ResolveSchemeId(schemeId) });
        return Ok(ApiResponse<dynamic>.Ok(summary));
    }

    //Admin: receipts for one customer in the active scheme.
    [HttpGet("customer/{customerId:int}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> ListForCustomer(int customerId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] int? schemeId = null)
    {
        _ = _me.ResolveSchemeId(schemeId); // enforce scheme context for SuperAdmin
        var receipts = await _db.QueryAsync<ReceiptRow>("usp_ListReceiptsByCustomer",
            new { CustomerID = customerId, Page = page, PageSize = pageSize });
        return Ok(ApiResponse<IEnumerable<ReceiptRow>>.Ok(receipts));
    }

    //Customer portal: my receipts.
    [HttpGet("me")]
    [Authorize(Policy = "CustomerOnly")]
    public async Task<IActionResult> MyReceipts([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var receipts = await _db.QueryAsync<ReceiptRow>("usp_ListReceiptsByCustomer",
            new { CustomerID = _me.UserId, Page = page, PageSize = pageSize });
        return Ok(ApiResponse<IEnumerable<ReceiptRow>>.Ok(receipts));
    }

    //JSON detail + allocations. Customer only own; admin any in scheme.
    [HttpGet("{receiptId:int}")]
    [Authorize]
    public async Task<IActionResult> GetById(int receiptId, [FromQuery] int? schemeId = null)
    {
        var receipt = await GetOwnedReceiptOrThrow(receiptId, schemeId);
        return Ok(ApiResponse<ReceiptDetailRow>.Ok(receipt));
    }

    //PDF download. Customer only own; admin any in scheme.
    [HttpGet("{receiptId:int}/pdf")]
    [Authorize]
    public async Task<IActionResult> GetPdf(int receiptId, [FromQuery] int? schemeId = null)
    {
        var receipt = await GetOwnedReceiptOrThrow(receiptId, schemeId);
        var bytes = _pdf.Generate(receipt);
        return File(bytes, "application/pdf", $"{receipt.ReceiptNumber}.pdf");
    }

    /// Admin/SuperAdmin: SMS the customer a short receipt confirmation
    /// (amount + receipt number). Does not attach PDF over SMS.
    [HttpPost("{receiptId:int}/notify")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> NotifyCustomer(int receiptId, [FromQuery] int? schemeId = null)
    {
        var receipt = await GetOwnedReceiptOrThrow(receiptId, schemeId);

        var customer = await _db.QuerySingleOrDefaultAsync<CustomerRow>(
            "usp_GetCustomerById",
            new { CustomerID = receipt.CustomerID, SchemeID = _me.ResolveSchemeId(schemeId) });

        if (customer is null)
            throw ApiException.NotFound("Customer not found");
        if (string.IsNullOrWhiteSpace(customer.Phone))
            throw ApiException.Validation("Customer has no phone number on file");

        var msg =
            $"Dear {customer.FullName}, receipt {receipt.ReceiptNumber} for KES {receipt.AmountPaid:N2} " +
            $"on {receipt.PaymentDate:dd MMM yyyy} was recorded for account {receipt.AccountNo}. Thank you. — {receipt.SchemeName}";

        await _sms.SendAsync(customer.Phone, msg);
        return Ok(ApiResponse.Ok("Receipt notification SMS queued"));
    }

    private async Task<ReceiptDetailRow> GetOwnedReceiptOrThrow(int receiptId, int? schemeId = null)
    {
        int effectiveSchemeId;
        if (_me.IsCustomer)
            effectiveSchemeId = _me.SchemeId;
        else
            effectiveSchemeId = _me.ResolveSchemeId(schemeId);

        var result = await _db.QueryMultipleAsync<ReceiptDetailRow, ReceiptAllocationRow>(
            "usp_GetReceiptById",
            new { PaymentID = receiptId, SchemeID = effectiveSchemeId });

        var receipt = result.First;
        if (receipt is null)
            throw ApiException.NotFound("Receipt not found");

        if (_me.IsCustomer && receipt.CustomerID != _me.UserId)
            throw ApiException.Forbidden();

        receipt.Allocations = result.Second?.ToList() ?? new List<ReceiptAllocationRow>();
        return receipt;
    }
}
