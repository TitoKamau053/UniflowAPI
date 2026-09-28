namespace UniflowApi.Models;

public record RecordMeterReadingRequest(int CustomerId, DateOnly ReadingDate, decimal CurrentReading, string? Notes);
public record RecordMeterReadingResult(int ReadingId);

public class MeterReadingRow
{
    public int ReadingID { get; set; }
    public int CustomerID { get; set; }
    public DateTime ReadingDate { get; set; }
    public decimal PreviousReading { get; set; }
    public decimal CurrentReading { get; set; }
    public decimal UnitsConsumed { get; set; }
    public string? RecordedBy { get; set; }
    public string? Notes { get; set; }
}

public class CustomerDueForReadingRow
{
    public int CustomerID { get; set; }
    public string AccountNo { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Zone { get; set; } = string.Empty;
    public string? MeterNumber { get; set; }
}
