namespace UniflowApi.Models;

public record ProcessPaymentRequest(int CustomerId, decimal AmountPaid, string TransactionRef, DateTime? PaymentDate);
public record ProcessPaymentResult(int PaymentId);

public class PaymentRow
{
    public int PaymentID { get; set; }
    public int CustomerID { get; set; }
    public decimal AmountPaid { get; set; }
    public decimal UnallocatedAmount { get; set; }
    public string TransactionRef { get; set; } = string.Empty;
    public DateTime PaymentDate { get; set; }
}
