using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using UniflowApi.Models;

namespace UniflowApi.Services;

public interface IReceiptPdfService
{
    byte[] Generate(ReceiptDetailRow receipt);
}

public class ReceiptPdfService : IReceiptPdfService
{
    private readonly IWebHostEnvironment _environment;

    public ReceiptPdfService(IWebHostEnvironment environment)
    {
        _environment = environment;
    }

    public byte[] Generate(ReceiptDetailRow receipt)
    {
        var logoPath = Path.Combine(
            _environment.ContentRootPath,
            "Assets",
            "Branding",
            "uniflow-logo.png"
        );

        byte[]? logoBytes = null;

        if (File.Exists(logoPath))
        {
            logoBytes = File.ReadAllBytes(logoPath);
        }

        var eatZone = TimeZoneInfo.FindSystemTimeZoneById("Africa/Nairobi");
        var utcDate = receipt.PaymentDate.Kind == DateTimeKind.Unspecified 
            ? DateTime.SpecifyKind(receipt.PaymentDate, DateTimeKind.Utc) 
            : receipt.PaymentDate.ToUniversalTime();
        var eatPaymentDate = TimeZoneInfo.ConvertTimeFromUtc(utcDate, eatZone);

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A5);
                page.Margin(30);
                page.DefaultTextStyle(x => x.FontSize(10));

                // page.Background()
                //     .AlignCenter()
                //     .AlignMiddle()
                //     .Rotate(-45)
                //     .Text(receipt.SchemeName)
                //     .FontSize(55)
                //     .FontColor(Colors.Blue.Lighten5)
                //     .SemiBold();

                page.Header().Column(col =>
                {
                    col.Spacing(5);
                    if (logoBytes != null)
                    {
                        col.Item().AlignCenter().Height(65).Image(logoBytes).FitArea();
                    }
                    col.Item().AlignCenter().Text(receipt.SchemeName).FontSize(15).Bold();
                    col.Item().AlignCenter().Text("PAYMENT RECEIPT").FontSize(11).FontColor(Colors.Grey.Darken1);
                    col.Item().PaddingTop(5).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
                });

                page.Content()
                    .PaddingVertical(15)
                    .Column(col =>
                    {
                        col.Spacing(7);

                        AddRow(col, "Receipt No.", receipt.ReceiptNumber);
                        AddRow(col, "Date", eatPaymentDate.ToString("dd MMM yyyy, HH:mm"));
                        AddRow(col, "Customer", receipt.FullName);
                        AddRow(col, "Account No.", receipt.AccountNo);
                        AddRow(col, "Transaction Ref.", receipt.TransactionRef);

                        col.Item().PaddingTop(10).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
                        col.Item().PaddingTop(8).Text("PAYMENT DETAILS").Bold().FontSize(10);
                        foreach (var allocation in receipt.Allocations)
                        {
                            col.Item().PaddingTop(5).Row(row =>
                                {
                                    row.RelativeItem().Column(detail =>
                                        {
                                            detail.Item().Text(allocation.Description).SemiBold();
                                            if (!string.IsNullOrWhiteSpace(allocation.Reference))
                                            {
                                                detail.Item().Text(allocation.Reference).FontSize(8).FontColor(Colors.Grey.Darken1);
                                            }
                                        });
                                    row.ConstantItem(120).AlignRight().Text($"KSh {allocation.AmountAllocated:N2}");
                                });
                        }

                        col.Item().PaddingTop(10).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
                        col.Item().PaddingTop(8).Row(row =>
                            {
                                row.RelativeItem().Text("TOTAL").FontSize(14).Bold();
                                row.ConstantItem(120).AlignRight().Text($"KSh {receipt.AmountPaid:N2}").FontSize(14).Bold();
                            });
                    });

                page.Footer().Column(col =>
                {
                    col.Item().AlignCenter().Text("Thank you for your payment.").FontSize(9);
                    col.Item().AlignCenter().Text("Powered by Uniflow").FontSize(8).FontColor(Colors.Grey.Medium);
                    col.Item().PaddingTop(3).AlignCenter().Text("This receipt was generated automatically and is valid without a signature.")
                        .FontSize(7).FontColor(Colors.Grey.Medium).AlignCenter();
                });
            });
        });

        return document.GeneratePdf();
    }

    private static void AddRow(
        ColumnDescriptor column,
        string label,
        string value)
    {
        column.Item().Row(row =>
        {
            row.RelativeItem().Text(label).SemiBold();
            row.RelativeItem().Text(value).AlignRight();
        });
    }
}