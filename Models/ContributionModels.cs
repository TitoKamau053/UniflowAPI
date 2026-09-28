namespace UniflowApi.Models;

public record GenerateContributionsRequest(DateOnly ContributionMonth, decimal AmountRequired);
public record MarkContributionPaidRequest(decimal AmountPaid);

public class ContributionRow
{
    public int ContributionID { get; set; }
    public int CustomerID { get; set; }
    public DateTime ContributionMonth { get; set; }
    public decimal AmountRequired { get; set; }
    public decimal AmountPaid { get; set; }
    public decimal Balance { get; set; }
    public string Status { get; set; } = string.Empty;
}
