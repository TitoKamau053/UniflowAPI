using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniflowApi.Common;
using UniflowApi.Data;
using UniflowApi.Models;
using UniflowApi.Services;

namespace UniflowApi.Controllers;

/// <summary>
/// Every notification here is admin-initiated. The one automatic exception
/// (account creation) is queued inside usp_CreateCustomer itself — nothing
/// in this controller duplicates that.
/// </summary>
[ApiController]
[Route("api/v1/notifications")]
[Authorize(Policy = "AdminOnly")]
public class NotificationsController : ControllerBase
{
    private readonly StoredProcExecutor _db;
    private readonly ICurrentUser _me;
    private readonly ISmsGateway _sms;

    public NotificationsController(StoredProcExecutor db, ICurrentUser me, ISmsGateway sms)
    {
        _db = db;
        _me = me;
        _sms = sms;
    }

    /// <summary>An admin explicitly decides a customer should be notified about something (a bill, a fine, a payment, an overdue notice). Only queues — does not send.</summary>
    [HttpPost]
    public async Task<IActionResult> Queue([FromBody] QueueNotificationRequest request)
    {
        await _db.ExecuteAsync("usp_QueueNotification", new
        {
            SchemeID = _me.SchemeId,
            CustomerID = request.CustomerId,
            request.NotificationType,
            request.RelatedTable,
            RelatedID = request.RelatedId,
            request.Message
        });

        return Ok(ApiResponse.Ok("Notification queued"));
    }

    /// <summary>Sends immediately (bypasses the queue entirely) — mirrors Nyanjigi's POST /notifications/send for one-off announcements.</summary>
    [HttpPost("send")]
    public async Task<IActionResult> SendDirect([FromBody] SendDirectNotificationRequest request)
    {
        var customer = await _db.QuerySingleOrDefaultAsync<CustomerRow>(
            "usp_GetCustomerById", new { CustomerID = request.CustomerId, SchemeID = _me.SchemeId });
        if (customer is null) throw ApiException.NotFound("Customer not found");
        if (string.IsNullOrWhiteSpace(customer.Phone)) throw ApiException.Validation("Customer has no phone number on file");

        var result = await _sms.SendAsync(customer.Phone, request.Message);

        // Record it in the same table history/pending share, already marked with its outcome.
        var p = new Dapper.DynamicParameters();
        p.Add("SchemeID", _me.SchemeId);
        p.Add("CustomerID", request.CustomerId);
        p.Add("NotificationType", request.NotificationType ?? "custom");
        p.Add("RelatedTable", (string?)null);
        p.Add("RelatedID", (int?)null);
        p.Add("Message", request.Message);
        await _db.ExecuteWithOutputAsync("usp_QueueNotification", p);

        return result.Success
            ? Ok(ApiResponse<SmsSendResult>.Ok(result, "Notification sent"))
            : throw ApiException.Validation(result.Error ?? "SMS send failed");
    }

    /// <summary>Polled by whatever worker dispatches SMS/email — kept behind AdminOnly until a service credential is set up for it.</summary>
    [HttpGet("pending")]
    public async Task<IActionResult> GetPending([FromQuery] int top = 100)
    {
        var pending = await _db.QueryAsync<NotificationRow>("usp_GetPendingNotifications",
            new { SchemeID = _me.SchemeId, Top = top });

        return Ok(ApiResponse<IEnumerable<NotificationRow>>.Ok(pending));
    }

    /// <summary>Drains up to `top` pending notifications for this scheme and actually sends each via the SMS gateway, marking sent/failed as it goes.</summary>
    [HttpPost("dispatch-pending")]
    public async Task<IActionResult> DispatchPending([FromQuery] int top = 50)
    {
        var pending = (await _db.QueryAsync<NotificationRow>("usp_GetPendingNotifications",
            new { SchemeID = _me.SchemeId, Top = top })).ToList();

        var sent = 0;
        var failed = 0;
        foreach (var n in pending)
        {
            var customer = await _db.QuerySingleOrDefaultAsync<CustomerRow>(
                "usp_GetCustomerById", new { CustomerID = n.CustomerID, SchemeID = _me.SchemeId });

            var result = string.IsNullOrWhiteSpace(customer?.Phone)
                ? new SmsSendResult(false, null, null, "No phone number on file")
                : await _sms.SendAsync(customer.Phone, n.Message);

            await _db.ExecuteAsync("usp_MarkNotificationSent", new { NotificationID = n.NotificationID, Success = result.Success });
            if (result.Success) sent++; else failed++;
        }

        return Ok(ApiResponse<object>.Ok(new { attempted = pending.Count, sent, failed }, "Dispatch complete"));
    }

    [HttpPost("{notificationId:long}/mark-sent")]
    public async Task<IActionResult> MarkSent(long notificationId, [FromBody] MarkNotificationSentRequest request)
    {
        await _db.ExecuteAsync("usp_MarkNotificationSent", new { NotificationID = notificationId, request.Success });
        return Ok(ApiResponse.Ok("Notification updated"));
    }

    [HttpGet("history")]
    public async Task<IActionResult> History([FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var history = await _db.QueryAsync<NotificationRow>("usp_ListNotificationHistory",
            new { SchemeID = _me.SchemeId, Status = status, Page = page, PageSize = pageSize });

        return Ok(ApiResponse<IEnumerable<NotificationRow>>.Ok(history));
    }

    /// <summary>Targeted customer list for building a notification audience — reuses the same search the Customers list uses.</summary>
    [HttpGet("customers")]
    public async Task<IActionResult> TargetableCustomers([FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var customers = await _db.QueryAsync<CustomerRow>("usp_ListCustomersByScheme",
            new { SchemeID = _me.SchemeId, Search = search, Page = page, PageSize = pageSize });

        return Ok(ApiResponse<IEnumerable<CustomerRow>>.Ok(customers));
    }
}
