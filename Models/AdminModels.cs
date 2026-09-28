namespace UniflowApi.Models;

public record SetEquityCredentialsRequest(string BillerUsername, string BillerPassword, string? EquityBillerNumber);
public record BatchMeterReadingItem(int CustomerId, DateOnly ReadingDate, decimal CurrentReading);
public record BulkSmsRequest(int[] CustomerIds, string Message);
public record PaymentConfirmationRequest(int PaymentId);
