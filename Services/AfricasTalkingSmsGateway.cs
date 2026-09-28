using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using UniflowApi.Common;

namespace UniflowApi.Services;

/* Calls Africa's Talking's plain HTTP messaging API directly (no SDK dependency needed for.NET). 
 Sandbox vs live is selected by base URL*/
public class AfricasTalkingSmsGateway : ISmsGateway
{
    private readonly HttpClient _http;
    private readonly SmsSettings _settings;
    private readonly ILogger<AfricasTalkingSmsGateway> _logger;

    private string BaseUrl => _settings.Sandbox
        ? "https://api.sandbox.africastalking.com/version1/messaging"
        : "https://api.africastalking.com/version1/messaging";

    public AfricasTalkingSmsGateway(HttpClient http, IOptions<SmsSettings> settings, ILogger<AfricasTalkingSmsGateway> logger)
    {
        _http = http;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<SmsSendResult> SendAsync(string phoneNumber, string message, string? senderId = null)
    {
        if (string.IsNullOrWhiteSpace(_settings.ApiKey) || string.IsNullOrWhiteSpace(_settings.Username))
            return new SmsSendResult(false, null, null, "SMS service not configured (missing ApiKey/Username)");

        var formattedPhone = FormatPhoneNumber(phoneNumber);
        if (formattedPhone is null)
            return new SmsSendResult(false, null, null, $"Invalid phone number: {phoneNumber}");

        var form = new Dictionary<string, string>
        {
            ["username"] = _settings.Username,
            ["to"] = formattedPhone,
            ["message"] = message,
            ["from"] = senderId ?? _settings.DefaultSenderId
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl) { Content = new FormUrlEncodedContent(form) };
        request.Headers.Add("apiKey", _settings.ApiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        try
        {
            var response = await _http.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Africa's Talking send failed: {Status} {Body}", response.StatusCode, body);
                return new SmsSendResult(false, null, null, $"Gateway returned {(int)response.StatusCode}");
            }

            using var doc = JsonDocument.Parse(body);
            var recipient = doc.RootElement.GetProperty("SMSMessageData").GetProperty("Recipients")[0];
            var status = recipient.GetProperty("status").GetString();
            var messageId = recipient.TryGetProperty("messageId", out var mid) ? mid.GetString() : null;
            var cost = recipient.TryGetProperty("cost", out var c) ? c.GetString() : null;

            return status == "Success"
                ? new SmsSendResult(true, messageId, cost, null)
                : new SmsSendResult(false, messageId, cost, status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Africa's Talking send threw an exception");
            return new SmsSendResult(false, null, null, "SMS gateway request failed");
        }
    }

    public async Task<SmsAccountStatus> GetAccountStatusAsync()
    {
        if (string.IsNullOrWhiteSpace(_settings.ApiKey) || string.IsNullOrWhiteSpace(_settings.Username))
            return new SmsAccountStatus(false, null, "SMS service not configured");

        var url = _settings.Sandbox
            ? $"https://api.sandbox.africastalking.com/version1/user?username={_settings.Username}"
            : $"https://api.africastalking.com/version1/user?username={_settings.Username}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("apiKey", _settings.ApiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        try
        {
            var response = await _http.SendAsync(request);
            if (!response.IsSuccessStatusCode)
                return new SmsAccountStatus(true, null, $"Gateway returned {(int)response.StatusCode}");

            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            var balance = doc.RootElement.GetProperty("UserData").GetProperty("balance").GetString();
            return new SmsAccountStatus(true, balance, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Africa's Talking status check threw an exception");
            return new SmsAccountStatus(true, null, "Status check failed");
        }
    }

    /* Kenyan-first formatting (0712... -> +254712..., already-international left alone),
    matching the phone shapes Nyanjigi's customers are onboarded with.*/
    private static string? FormatPhoneNumber(string phone)
    {
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        return digits.Length switch
        {
            10 when digits.StartsWith('0') => "+254" + digits[1..],
            12 when digits.StartsWith("254") => "+" + digits,
            9 => "+254" + digits,
            _ when phone.StartsWith('+') => phone,
            _ => null
        };
    }

    /* Same approach as Nyanjigi's SMSService.getDeliveryStatus: fetch recent messages (lastReceivedId=0) and
     search them for the matching id. AT's plain REST equivalent of the SDK's sms.fetchMessages() is GET
     /version1/messaging with a lastReceivedId query param. */
    public async Task<SmsDeliveryStatus> GetDeliveryStatusAsync(string messageId)
    {
        if (string.IsNullOrWhiteSpace(_settings.ApiKey) || string.IsNullOrWhiteSpace(_settings.Username))
            return new SmsDeliveryStatus(false, messageId, null, null, "SMS service not properly configured");

        var url = $"{BaseUrl}?username={Uri.EscapeDataString(_settings.Username)}&lastReceivedId=0";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("apiKey", _settings.ApiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        try
        {
            var response = await _http.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                return new SmsDeliveryStatus(false, messageId, null, null, $"Gateway returned {(int)response.StatusCode}");

            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("SMSMessageData", out var data) || !data.TryGetProperty("Messages", out var messages))
                return new SmsDeliveryStatus(false, messageId, null, null, "No messages found");

            foreach (var msg in messages.EnumerateArray())
            {
                if (msg.TryGetProperty("id", out var idProp) && idProp.GetString() == messageId)
                {
                    var status = msg.TryGetProperty("status", out var s) ? s.GetString() : null;
                    var date = msg.TryGetProperty("date", out var d) ? d.GetString() : null;
                    return new SmsDeliveryStatus(true, messageId, status, date, null);
                }
            }

            return new SmsDeliveryStatus(false, messageId, null, null, "Message not found");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Africa's Talking delivery-status query threw an exception");
            return new SmsDeliveryStatus(false, messageId, null, null, "Delivery status query failed");
        }
    }
}
