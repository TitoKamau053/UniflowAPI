using System.Text.Json.Serialization;

namespace UniflowApi.Models;

public record CreateCustomerRequest
{
    public string FullName { get; init; } = string.Empty;
    public string NationalID { get; init; } = string.Empty;
    public string AccountNo { get; init; } = string.Empty;
    public string? Phone { get; init; }
    public string? Location { get; init; }
    public string Zone { get; init; } = string.Empty;
    public string? MeterNumber { get; init; }
    public DateOnly? ConnectionDate { get; init; }

    public CreateCustomerRequest() { }

    [JsonConstructor]
    public CreateCustomerRequest(
        string fullName,
        string nationalID,
        string accountNo,
        string? phone,
        string? location,
        string zone,
        string? meterNumber,
        DateOnly? connectionDate)
    {
        FullName = fullName;
        NationalID = nationalID;
        AccountNo = accountNo;
        Phone = phone;
        Location = location;
        Zone = zone;
        MeterNumber = meterNumber;
        ConnectionDate = connectionDate;
    }
}

public record CreateCustomerResult(int CustomerId, string AccountNo);

public class CustomerRow
{
    public int CustomerID { get; set; }
    public int SchemeID { get; set; }
    public string AccountNo { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Location { get; set; }
    public string Zone { get; set; } = string.Empty;
    public string? MeterNumber { get; set; }
    public DateTime ConnectionDate { get; set; }
    public bool IsActive { get; set; }
}

public record UpdateCustomerRequest(string FullName, string? Phone, string? Location, string Zone, string? MeterNumber);
public record AdjustBalanceRequest(decimal Amount, string Reason);
public record AdjustBalanceResult(int AdjustmentId);
public record ResetPasswordResult(string TemporaryPassword);

public class CustomerAdjustmentRow
{
    public int AdjustmentID { get; set; }
    public int CustomerID { get; set; }
    public decimal Amount { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CustomerStatsRow
{
    public int TotalCustomers { get; set; }
    public int ActiveCustomers { get; set; }
    public int InactiveCustomers { get; set; }
    public int TotalZones { get; set; }
}

public class ZoneAnalyticsRow
{
    public string Zone { get; set; } = string.Empty;
    public int CustomerCount { get; set; }
    public decimal OutstandingBalance { get; set; }
    public decimal TotalCollected { get; set; }
}
