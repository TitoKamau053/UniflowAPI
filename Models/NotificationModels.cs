namespace UniflowApi.Models;

public record QueueNotificationRequest(int CustomerId, string NotificationType, string Message, string? RelatedTable, int? RelatedId);
public record MarkNotificationSentRequest(bool Success = true);
public record SendDirectNotificationRequest(int CustomerId, string Message, string? NotificationType);

public class NotificationRow
{
    public long NotificationID { get; set; }
    public int SchemeID { get; set; }
    public int CustomerID { get; set; }
    public string NotificationType { get; set; } = string.Empty;
    public string? RelatedTable { get; set; }
    public int? RelatedID { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? SentAt { get; set; }
}
