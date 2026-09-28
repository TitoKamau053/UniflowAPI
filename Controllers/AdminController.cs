using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniflowApi.Common;
using UniflowApi.Data;
using UniflowApi.Models;
using UniflowApi.Services;

namespace UniflowApi.Controllers;

//Scheme-scoped admin operations. Every action here is confined to the caller's own SchemeID.
[ApiController]
[Route("api/v1/admin")]
[Authorize(Policy = "AdminOnly")]
public class AdminController : ControllerBase
{
    private readonly StoredProcExecutor _db;
    private readonly ICurrentUser _me;
    private readonly ISmsGateway _sms;

    public AdminController(StoredProcExecutor db, ICurrentUser me, ISmsGateway sms)
    {
        _db = db;
        _me = me;
        _sms = sms;
    }

    /// Sets or rotates the caller's OWN scheme's Equity biller credentials.
    /// Equity itself never calls this — it's admin-facing only.
    [HttpPut("equity-credentials")]
    public async Task<IActionResult> SetEquityCredentials([FromBody] SetEquityCredentialsRequest request)
    {
        var passwordHash = BCrypt.Net.BCrypt.HashPassword(request.BillerPassword);

        await _db.ExecuteAsync("usp_UpsertEquityBillerCredentials", new
        {
            SchemeID = _me.SchemeId,
            request.BillerUsername,
            BillerPasswordHash = passwordHash,
            request.EquityBillerNumber
        });

        return Ok(ApiResponse.Ok("Equity biller credentials saved"));
    }

    //Dashboard & analytics

    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard()
    {
        var dashboard = await _db.QuerySingleOrDefaultAsync<dynamic>("usp_GetAdminDashboard", new { SchemeID = _me.SchemeId });
        return Ok(ApiResponse<dynamic>.Ok(dashboard));
    }

    [HttpGet("dashboard-comprehensive")]
    public async Task<IActionResult> DashboardComprehensive()
    {
        var dashboard = await _db.QuerySingleOrDefaultAsync<dynamic>("usp_GetAdminDashboardComprehensive", new { SchemeID = _me.SchemeId });
        return Ok(ApiResponse<dynamic>.Ok(dashboard));
    }

    [HttpGet("revenue-analytics")]
    public async Task<IActionResult> RevenueAnalytics([FromQuery] int months = 6)
    {
        var trend = await _db.QueryAsync<dynamic>("usp_GetRevenueAnalytics", new { SchemeID = _me.SchemeId, Months = months });
        return Ok(ApiResponse<IEnumerable<dynamic>>.Ok(trend));
    }

    [HttpGet("financial-summary")]
    public async Task<IActionResult> FinancialSummary()
    {
        var summary = await _db.QuerySingleOrDefaultAsync<dynamic>("usp_GetFinancialSummary", new { SchemeID = _me.SchemeId });
        return Ok(ApiResponse<dynamic>.Ok(summary));
    }

    [HttpGet("outstanding-customers")]
    public async Task<IActionResult> OutstandingCustomers([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var customers = await _db.QueryAsync<dynamic>("usp_ListOutstandingCustomers", new { SchemeID = _me.SchemeId, Page = page, PageSize = pageSize });
        return Ok(ApiResponse<IEnumerable<dynamic>>.Ok(customers));
    }

    ///Returns the full unpaginated set for client-side CSV/JSON export — formatting itself happens on the client.
    [HttpGet("customers/export")]
    public async Task<IActionResult> ExportCustomers()
    {
        var customers = await _db.QueryAsync<dynamic>("usp_ExportCustomers", new { SchemeID = _me.SchemeId });
        return Ok(ApiResponse<IEnumerable<dynamic>>.Ok(customers));
    }

    [HttpGet("activity-log")]
    public async Task<IActionResult> ActivityLog([FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        var log = await _db.QueryAsync<dynamic>("usp_GetActivityLog", new { SchemeID = _me.SchemeId, Page = page, PageSize = pageSize });
        return Ok(ApiResponse<IEnumerable<dynamic>>.Ok(log));
    }

    [HttpGet("system-health")]
    public async Task<IActionResult> SystemHealth()
    {
        var health = await _db.QuerySingleOrDefaultAsync<dynamic>("usp_GetSystemHealth");
        return Ok(ApiResponse<dynamic>.Ok(health));
    }

    // Meter readings (batch)
    [HttpGet("meter-readings/customers")]
    public async Task<IActionResult> CustomersForReading([FromQuery] DateOnly month)
    {
        var due = await _db.QueryAsync<CustomerDueForReadingRow>("usp_ListCustomersDueForReading", new { SchemeID = _me.SchemeId, Month = month });
        return Ok(ApiResponse<IEnumerable<CustomerDueForReadingRow>>.Ok(due));
    }

    [HttpPost("meter-readings")]
    public async Task<IActionResult> SubmitBatchReadings([FromBody] BatchMeterReadingItem[] readings)
    {
        var json = JsonSerializer.Serialize(readings.Select(r => new { customerId = r.CustomerId, readingDate = r.ReadingDate, currentReading = r.CurrentReading }));
        await _db.ExecuteAsync("usp_SubmitBatchMeterReadings", new { SchemeID = _me.SchemeId, ReadingsJson = json, RecordedBy = _me.UserId.ToString() });
        return Ok(ApiResponse.Ok($"{readings.Length} readings submitted"));
    }

    // SMS
    [HttpGet("sms/status")]
    public async Task<IActionResult> SmsStatus()
    {
        var status = await _sms.GetAccountStatusAsync();
        return Ok(ApiResponse<Services.SmsAccountStatus>.Ok(status));
    }

    [HttpPost("sms/send")]
    public async Task<IActionResult> SendSms([FromBody] SendDirectNotificationRequest request)
    {
        var customer = await _db.QuerySingleOrDefaultAsync<CustomerRow>("usp_GetCustomerById", new { CustomerID = request.CustomerId, SchemeID = _me.SchemeId });
        if (customer is null) throw ApiException.NotFound("Customer not found");
        if (string.IsNullOrWhiteSpace(customer.Phone)) throw ApiException.Validation("Customer has no phone number on file");

        var result = await _sms.SendAsync(customer.Phone, request.Message);
        return result.Success ? Ok(ApiResponse<Services.SmsSendResult>.Ok(result, "SMS sent")) : throw ApiException.Validation(result.Error ?? "SMS send failed");
    }

    [HttpPost("sms/bulk-send")]
    public async Task<IActionResult> BulkSendSms([FromBody] BulkSmsRequest request)
    {
        var sent = 0;
        var failed = 0;
        foreach (var customerId in request.CustomerIds)
        {
            var customer = await _db.QuerySingleOrDefaultAsync<CustomerRow>("usp_GetCustomerById", new { CustomerID = customerId, SchemeID = _me.SchemeId });
            if (customer is null || string.IsNullOrWhiteSpace(customer.Phone)) { failed++; continue; }

            var result = await _sms.SendAsync(customer.Phone, request.Message);
            if (result.Success) sent++; else failed++;
        }

        return Ok(ApiResponse<object>.Ok(new { attempted = request.CustomerIds.Length, sent, failed }, "Bulk send complete"));
    }

    ///Same approach as Nyanjigi: no webhook, just a poll against Africa's Talking's recent-messages list (see ISmsGateway.GetDeliveryStatusAsync).
    [HttpGet("sms/delivery-status/{messageId}")]
    public async Task<IActionResult> SmsDeliveryStatus(string messageId)
    {
        var result = await _sms.GetDeliveryStatusAsync(messageId);
        return result.Success ? Ok(ApiResponse<Services.SmsDeliveryStatus>.Ok(result, "Status retrieved")) : throw ApiException.Validation(result.Error ?? "Status lookup failed");
    }

    [HttpPost("notifications/bill-reminders")]
    public async Task<IActionResult> SendBillReminders()
    {
        var overdue = (await _db.QueryAsync<dynamic>("usp_ListOverdueBills", new { SchemeID = _me.SchemeId, Page = 1, PageSize = 500 })).ToList();

        var sent = 0;
        foreach (var bill in overdue)
        {
            var customer = await _db.QuerySingleOrDefaultAsync<CustomerRow>("usp_GetCustomerById", new { CustomerID = (int)bill.CustomerID, SchemeID = _me.SchemeId });
            if (customer is null || string.IsNullOrWhiteSpace(customer.Phone)) continue;

            var message = $"Reminder: Bill {bill.BillNumber} of {bill.Balance:N2} is overdue (due {bill.DueDate:yyyy-MM-dd}). Please settle at your earliest convenience.";
            var result = await _sms.SendAsync(customer.Phone, message);
            if (result.Success) sent++;
        }

        return Ok(ApiResponse<object>.Ok(new { overdueBills = overdue.Count, remindersSent = sent }, "Bill reminders dispatched"));
    }

    [HttpPost("notifications/payment-confirmations")]
    public async Task<IActionResult> SendPaymentConfirmation([FromBody] PaymentConfirmationRequest request)
    {
        var payment = await _db.QuerySingleOrDefaultAsync<PaymentRow>("usp_GetPaymentById", new { PaymentID = request.PaymentId, SchemeID = _me.SchemeId });
        if (payment is null) throw ApiException.NotFound("Payment not found");

        var customer = await _db.QuerySingleOrDefaultAsync<CustomerRow>("usp_GetCustomerById", new { CustomerID = payment.CustomerID, SchemeID = _me.SchemeId });
        if (customer is null || string.IsNullOrWhiteSpace(customer.Phone)) throw ApiException.Validation("Customer has no phone number on file");

        var message = $"Payment confirmed: {payment.AmountPaid:N2} received (ref {payment.TransactionRef}). Thank you.";
        var result = await _sms.SendAsync(customer.Phone, message);
        return result.Success ? Ok(ApiResponse.Ok("Confirmation sent")) : throw ApiException.Validation(result.Error ?? "SMS send failed");
    }

    ///"Multi-channel" today means SMS only — the Message field is free-text so admins can compose whatever the situation calls for.
    [HttpPost("notifications/custom")]
    public async Task<IActionResult> SendCustom([FromBody] SendDirectNotificationRequest request)
    {
        var customer = await _db.QuerySingleOrDefaultAsync<CustomerRow>("usp_GetCustomerById", new { CustomerID = request.CustomerId, SchemeID = _me.SchemeId });
        if (customer is null) throw ApiException.NotFound("Customer not found");
        if (string.IsNullOrWhiteSpace(customer.Phone)) throw ApiException.Validation("Customer has no phone number on file");

        var result = await _sms.SendAsync(customer.Phone, request.Message);
        return result.Success ? Ok(ApiResponse.Ok("Notification sent")) : throw ApiException.Validation(result.Error ?? "SMS send failed");
    }
}
