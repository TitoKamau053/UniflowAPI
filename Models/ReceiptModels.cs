namespace UniflowApi.Models;

public class ReceiptRow
{
    public int PaymentID { get; set; }
    public string ReceiptNumber { get; set; } = string.Empty;
    public decimal AmountPaid { get; set; }
    public string TransactionRef { get; set; } = string.Empty;
    public DateTime PaymentDate { get; set; }
}

public class ReceiptDetailRow
{
    public int PaymentID { get; set; }
    public string ReceiptNumber { get; set; } = string.Empty;
    public int CustomerID { get; set; }
    public string AccountNo { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Zone { get; set; }
    public string SchemeName { get; set; } = string.Empty;
    public decimal AmountPaid { get; set; }
    public decimal UnallocatedAmount { get; set; }
    public string TransactionRef { get; set; } = string.Empty;
    public DateTime PaymentDate { get; set; }
    public List<ReceiptAllocationRow> Allocations { get; set; } = new();
}

public class ReceiptAllocationRow
{
    public string TargetType { get; set; } = string.Empty;
    public int TargetID { get; set; }
    public decimal AmountAllocated { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? Reference { get; set; }
}