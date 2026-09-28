namespace UniflowApi.Models;

public record GenerateMonthlyBillsRequest(DateOnly PeriodStart, DateOnly PeriodEnd, DateOnly DueDate);
public record ApplyFineRequest(int CustomerId, int? BillId, decimal Amount, string Reason, DateOnly? AppliedDate);
public record ApplyFineResult(int FineId);
public record UpdateBillStatusRequest(string Status);
public record BulkUpdateBillStatusRequest(int[] BillIds, string Status);
public record UpdateFineStatusRequest(string Status);

public class BillRow
{
    public int BillID { get; set; }
    public int CustomerID { get; set; }
    public string BillNumber { get; set; } = string.Empty;
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public decimal CurrentCharges { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal AmountPaid { get; set; }
    public decimal Balance { get; set; }
    public DateTime DueDate { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class FineRow
{
    public int FineID { get; set; }
    public int CustomerID { get; set; }
    public int? BillID { get; set; }
    public decimal Amount { get; set; }
    public decimal AmountPaid { get; set; }
    public decimal Balance { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime AppliedDate { get; set; }
    public string Status { get; set; } = string.Empty;
}
