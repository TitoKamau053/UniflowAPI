using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniflowApi.Common;
using UniflowApi.Data;
using UniflowApi.Models;
using UniflowApi.Services;

namespace UniflowApi.Controllers;

/// <summary>
/// Equity Bank's Biller integration. Equity — not us — initiates every call
/// here: it logs in once to get a token, calls /validate-customer before
/// letting a customer pay at any of its channels, then POSTs /callback once
/// the payment clears. This mirrors Nyanjigi's EquityController 1:1 except
/// every lookup is now scoped to the calling biller token's SchemeID, so one
/// deployment can serve many schemes' Equity integrations without any risk
/// of cross-scheme customer resolution.
/// </summary>
[ApiController]
[Route("api/v1/equity")]
public class EquityController : ControllerBase
{
    private readonly StoredProcExecutor _db;
    private readonly ITokenService _tokens;
    private readonly ICurrentUser _me;
    private readonly ILogger<EquityController> _logger;

    public EquityController(StoredProcExecutor db, ITokenService tokens, ICurrentUser me, ILogger<EquityController> logger)
    {
        _db = db;
        _tokens = tokens;
        _me = me;
        _logger = logger;
    }

    /// <summary>STEP 0 — Equity authenticates as a specific scheme's biller.</summary>
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] EquityLoginRequest request)
    {
        var creds = await _db.QuerySingleOrDefaultAsync<EquityBillerCredentialRow>(
            "usp_GetEquityBillerCredentials", new { BillerUsername = request.Username });

        if (creds is null || !BCrypt.Net.BCrypt.Verify(request.Password, creds.BillerPasswordHash))
            throw ApiException.Unauthorized("Invalid credentials");

        // Short-lived token, scoped to exactly this scheme — every subsequent
        // call this biller makes is automatically confined to its own tenant.
        var token = _tokens.GenerateToken(creds.SettingID, creds.SchemeID, "equity_biller");

        return Ok(new EquityLoginResult(token)); // Equity expects a bare {access: "..."} body, not our envelope
    }

    /// <summary>STEP 1 — Equity checks the account exists (and can show outstanding balance) before accepting payment.</summary>
    [HttpPost("validate-customer")]
    [Authorize(Policy = "EquityBillerOnly")]
    public async Task<IActionResult> ValidateCustomer([FromBody] EquityValidateCustomerRequest request)
    {
        var identifier = request.customer_details ?? request.member_number ?? request.billNumber;
        if (string.IsNullOrWhiteSpace(identifier))
            return BadRequest(new { success = false, message = "Customer details are required" });

        var customer = await _db.QuerySingleOrDefaultAsync<EquityCustomerLookupRow>(
            "usp_FindCustomerForEquity", new { SchemeID = _me.SchemeId, Identifier = identifier });

        if (customer is null || !customer.IsActive)
            return NotFound(new { success = false, message = "Customer account not found" });

        var debt = await _db.QuerySingleOrDefaultAsync<decimal>(
            "usp_GetCustomerOutstandingBalance", new { CustomerID = customer.CustomerID });

        return Ok(new
        {
            success = true,
            data = new
            {
                account_number = customer.AccountNo,
                full_name = customer.FullName,
                outstanding_balance = debt
            }
        });
    }

    /// <summary>
    /// STEP 2 — payment notification. Equity requires a fast ack, so this
    /// records + acks synchronously, then allocates via usp_ProcessPayment.
    /// Idempotent by EquityTranId (usp_RecordEquityTransaction's unique
    /// constraint) — Equity is known to retry callbacks.
    /// </summary>
    [HttpPost("callback")]
    [Authorize(Policy = "EquityBillerOnly")]
    public async Task<IActionResult> HandleCallback([FromBody] EquityCallbackRequest request)
    {
        var tranId = request.tranId ?? request.transaction_id;
        var memberNumber = request.billNumber ?? request.member_number;
        var amount = request.amount;
        var channel = request.channel ?? request.payment_method;

        if (string.IsNullOrWhiteSpace(tranId) || string.IsNullOrWhiteSpace(memberNumber) || amount is null)
            return BadRequest(new { responseCode = "400", responseMessage = "Missing required fields" });

        var customer = await _db.QuerySingleOrDefaultAsync<EquityCustomerLookupRow>(
            "usp_FindCustomerForEquity", new { SchemeID = _me.SchemeId, Identifier = memberNumber });

        var recordParams = new DynamicParameters();
        recordParams.Add("SchemeID", _me.SchemeId);
        recordParams.Add("CustomerID", customer?.CustomerID);
        recordParams.Add("EquityTranId", tranId);
        recordParams.Add("MemberNumberProvided", memberNumber);
        recordParams.Add("Amount", amount);
        recordParams.Add("Channel", channel);
        recordParams.Add("RawPayload", JsonSerializer.Serialize(request));
        recordParams.Add("IsDuplicate", dbType: System.Data.DbType.Boolean, direction: System.Data.ParameterDirection.Output);
        recordParams.Add("NewEquityTransactionID", dbType: System.Data.DbType.Int64, direction: System.Data.ParameterDirection.Output);

        var recorded = await _db.ExecuteWithOutputAsync("usp_RecordEquityTransaction", recordParams);
        var isDuplicate = recorded.Get<bool>("IsDuplicate");
        var equityTxId = recorded.Get<long>("NewEquityTransactionID");

        if (isDuplicate)
            return Ok(new { responseCode = "409", responseMessage = "Duplicate transaction" });

        if (customer is null)
            return Ok(new { responseCode = "200", responseMessage = "Received — customer not matched, held for review" });

        // Ack Equity immediately; allocate in the background so the callback
        // response is never blocked on the waterfall proc.
        _ = Task.Run(async () => await ProcessAsync(equityTxId, customer.CustomerID, amount.Value, tranId!));

        return Ok(new { responseCode = "200", responseMessage = "Success" });
    }

    private async Task ProcessAsync(long equityTransactionId, int customerId, decimal amount, string tranId)
    {
        try
        {
            var p = new DynamicParameters();
            p.Add("CustomerID", customerId);
            p.Add("AmountPaid", amount);
            p.Add("TransactionRef", $"EQ-{tranId}");
            p.Add("PaymentDate", (DateTime?)null);
            p.Add("NewPaymentID", dbType: System.Data.DbType.Int32, direction: System.Data.ParameterDirection.Output);

            var result = await _db.ExecuteWithOutputAsync("usp_ProcessPayment", p);
            var paymentId = result.Get<int>("NewPaymentID");

            await _db.ExecuteAsync("usp_MarkEquityTransactionProcessed",
                new { EquityTransactionID = equityTransactionId, PaymentID = paymentId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Equity payment processing failed for tranId {TranId}", tranId);
            await _db.ExecuteAsync("usp_MarkEquityTransactionFailed",
                new { EquityTransactionID = equityTransactionId, FailureReason = ex.Message[..Math.Min(ex.Message.Length, 290)] });
        }
    }
}
