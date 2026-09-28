namespace UniflowApi.Common;

/// Africa's Talking credentials — one platform-level account (ApiKey/Username)
/// shared across all schemes, matching how Nyanjigi itself is configured via
/// env vars. The per-scheme SenderId is NOT here — it's pulled from that
/// scheme's dbo.SystemSettings ('notifications','sender_id') at send time, so
/// different schemes can brand their outgoing SMS differently without a
/// second Africa's Talking account each.
public class SmsSettings
{
    public const string SectionName = "Sms";

    public string ApiKey { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string DefaultSenderId { get; set; } = "UNIFLOW";
    public bool Sandbox { get; set; } = true;
}
