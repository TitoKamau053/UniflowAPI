namespace UniflowApi.Models;

public record EquityLoginRequest(string Username, string Password);
public record EquityLoginResult(string Access);

public class EquityBillerCredentialRow
{
    public int SettingID { get; set; }
    public int SchemeID { get; set; }
    public string BillerUsername { get; set; } = string.Empty;
    public string BillerPasswordHash { get; set; } = string.Empty;
    public string? EquityBillerNumber { get; set; }
}

// Equity's validate-customer request shape is loose — it may send any one of these keys.
public record EquityValidateCustomerRequest(string? customer_details, string? member_number, string? billNumber);

public class EquityCustomerLookupRow
{
    public int CustomerID { get; set; }
    public string AccountNo { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public bool IsActive { get; set; }
}

// Equity's payment callback shape (per their biller spec, as implemented in Nyanjigi)
public record EquityCallbackRequest(
    string? tranId, string? transaction_id,
    string? billNumber, string? member_number,
    decimal? amount,
    string? channel, string? payment_method,
    string? tranDate, string? timestamp);
