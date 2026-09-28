using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniflowApi.Common;
using UniflowApi.Data;
using UniflowApi.Models;
using UniflowApi.Services;

namespace UniflowApi.Controllers;

[ApiController]
[Route("api/v1/payments")]
public class PaymentsController : ControllerBase
{
    private readonly StoredProcExecutor _db;
    private readonly ICurrentUser _me;

    public PaymentsController(StoredProcExecutor db, ICurrentUser me)
    {
        _db = db;
        _me = me;
    }

    [HttpPost("process")]
    [Authorize(Policy = "AdminOnly")] 
    public async Task<IActionResult> Process([FromBody] ProcessPaymentRequest request)
    {
        var customer = await _db.QuerySingleOrDefaultAsync<CustomerRow>(
            "usp_GetCustomerById", new { CustomerID = request.CustomerId, SchemeID = _me.SchemeId });
        if (customer is null)
            throw ApiException.NotFound("Customer not found");

        var p = new DynamicParameters();
        p.Add("CustomerID", request.CustomerId);
        p.Add("AmountPaid", request.AmountPaid);
        p.Add("TransactionRef", request.TransactionRef);
        p.Add("PaymentDate", request.PaymentDate);
        p.Add("NewPaymentID", dbType: System.Data.DbType.Int32, direction: System.Data.ParameterDirection.Output);

        var result = await _db.ExecuteWithOutputAsync("usp_ProcessPayment", p);

        var response = new ProcessPaymentResult(result.Get<int>("NewPaymentID"));
        return Ok(ApiResponse<ProcessPaymentResult>.Ok(response, "Payment processed successfully"));
    }

    [HttpGet("customer/{customerId:int}")]
    [Authorize]
    public async Task<IActionResult> ListForCustomer(int customerId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        if (_me.IsCustomer && _me.UserId != customerId)
            throw ApiException.Forbidden();

        var payments = await _db.QueryAsync<PaymentRow>("usp_ListPaymentsByCustomer",
            new { CustomerID = customerId, Page = page, PageSize = pageSize });

        return Ok(ApiResponse<IEnumerable<PaymentRow>>.Ok(payments));
    }

    [HttpGet("history")]
    [Authorize(Policy = "CustomerOnly")]
    public async Task<IActionResult> MyHistory([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var payments = await _db.QueryAsync<PaymentRow>("usp_ListPaymentsByCustomer", new { CustomerID = _me.UserId, Page = page, PageSize = pageSize });
        return Ok(ApiResponse<IEnumerable<PaymentRow>>.Ok(payments));
    }

    [HttpGet("methods")]
    [AllowAnonymous]
    public IActionResult Methods() => Ok(ApiResponse<string[]>.Ok(new[] { "equity_branch", "equity_agent", "equity_mpesa", "equity_equitel", "equity_ussd", "equity_app", "manual" }));

    [HttpGet("all")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> All([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var payments = await _db.QueryAsync<dynamic>("usp_ListAllPayments", new { SchemeID = _me.SchemeId, Page = page, PageSize = pageSize });
        return Ok(ApiResponse<IEnumerable<dynamic>>.Ok(payments));
    }

    [HttpGet("stats")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Stats()
    {
        var stats = await _db.QuerySingleOrDefaultAsync<dynamic>("usp_GetPaymentStats", new { SchemeID = _me.SchemeId });
        return Ok(ApiResponse<dynamic>.Ok(stats));
    }

    [HttpGet("status/{transactionId}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> StatusByTransactionRef(string transactionId)
    {
        var payment = await _db.QuerySingleOrDefaultAsync<PaymentRow>("usp_GetPaymentByTransactionRef",
            new { TransactionRef = transactionId, SchemeID = _me.SchemeId });
        if (payment is null) throw ApiException.NotFound("Transaction not found");
        return Ok(ApiResponse<PaymentRow>.Ok(payment));
    }

    [HttpGet("{paymentId:int}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> GetById(int paymentId)
    {
        var payment = await _db.QuerySingleOrDefaultAsync<PaymentRow>("usp_GetPaymentById", new { PaymentID = paymentId, SchemeID = _me.SchemeId });
        if (payment is null) throw ApiException.NotFound("Payment not found");
        return Ok(ApiResponse<PaymentRow>.Ok(payment));
    }
}
