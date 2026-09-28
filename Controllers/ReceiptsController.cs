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

    public ReceiptsController(StoredProcExecutor db, ICurrentUser me, IReceiptPdfService pdf)
    {
        _db = db;
        _me = me;
        _pdf = pdf;
    }

    [HttpGet]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> ListAll([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var receipts = await _db.QueryAsync<dynamic>("usp_ListAllPayments", new { SchemeID = _me.SchemeId, Page = page, PageSize = pageSize });
        return Ok(ApiResponse<IEnumerable<dynamic>>.Ok(receipts));
    }

    [HttpGet("summary")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Summary()
    {
        var summary = await _db.QuerySingleOrDefaultAsync<dynamic>("usp_GetReceiptsSummary", new { SchemeID = _me.SchemeId });
        return Ok(ApiResponse<dynamic>.Ok(summary));
    }

    [HttpGet("customer/{customerId:int}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> ListForCustomer(int customerId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var receipts = await _db.QueryAsync<ReceiptRow>("usp_ListReceiptsByCustomer", new { CustomerID = customerId, Page = page, PageSize = pageSize });
        return Ok(ApiResponse<IEnumerable<ReceiptRow>>.Ok(receipts));
    }

    [HttpGet("me/receipts")]
    [Authorize(Policy = "CustomerOnly")]
    public async Task<IActionResult> MyReceipts([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var receipts = await _db.QueryAsync<ReceiptRow>("usp_ListReceiptsByCustomer", new { CustomerID = _me.UserId, Page = page, PageSize = pageSize });
        return Ok(ApiResponse<IEnumerable<ReceiptRow>>.Ok(receipts));
    }

    [HttpGet("{receiptId:int}")]
    [Authorize]
    public async Task<IActionResult> GetById(int receiptId)
    {
        var receipt = await GetOwnedReceiptOrThrow(receiptId);
        return Ok(ApiResponse<ReceiptDetailRow>.Ok(receipt));
    }

    [HttpGet("{receiptId:int}/pdf")]
    [Authorize]
    public async Task<IActionResult> GetPdf(int receiptId)
    {
        var receipt = await GetOwnedReceiptOrThrow(receiptId);

        var bytes = _pdf.Generate(receipt);

        return File(
            bytes,
            "application/pdf",
            $"{receipt.ReceiptNumber}.pdf"
        );
    }

    private async Task<ReceiptDetailRow> GetOwnedReceiptOrThrow(int receiptId)
    {
        var result = await _db.QueryMultipleAsync<
            ReceiptDetailRow,
            ReceiptAllocationRow>(
            "usp_GetReceiptById",
            new
            {
                PaymentID = receiptId,
                SchemeID = _me.SchemeId
            });

        var receipt = result.First;

        if (receipt is null)
            throw ApiException.NotFound("Receipt not found");

        if (_me.IsCustomer && receipt.CustomerID != _me.UserId)
            throw ApiException.Forbidden();

        receipt.Allocations = result.Second;

        return receipt;
    }
}
