namespace UniflowApi.Services;

public record SmsSendResult(bool Success, string? MessageId, string? Cost, string? Error);
public record SmsAccountStatus(bool Configured, string? Balance, string? Error);
public record SmsDeliveryStatus(bool Success, string? MessageId, string? Status, string? DeliveryTime, string? Error);

public interface ISmsGateway
{
    Task<SmsSendResult> SendAsync(string phoneNumber, string message, string? senderId = null);
    Task<SmsAccountStatus> GetAccountStatusAsync();

    /// <summary>
    /// Mirrors Nyanjigi's SMSService.getDeliveryStatus: Africa's Talking has
    /// no per-ID lookup endpoint, so this pulls recent messages via
    /// fetchMessages and searches them for a matching id — a poll, not a
    /// webhook. Same limitation Nyanjigi has: only messages still in AT's
    /// recent-message window are findable this way.
    /// </summary>
    Task<SmsDeliveryStatus> GetDeliveryStatusAsync(string messageId);
}
