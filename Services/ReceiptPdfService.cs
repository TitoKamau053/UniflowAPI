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
    private static readonly string AccentRed = "#ed1c24";
    private static readonly string TextDark = "#111827";
    private static readonly string TextMuted = "#6b7280";
    private static readonly string Border = "#e5e7eb";
    private static readonly string Surface = "#f9fafb";
    private static readonly string White = "#ffffff";

    private readonly IWebHostEnvironment _environment;

    public ReceiptPdfService(IWebHostEnvironment environment)
    {
        _environment = environment;
    }

    public byte[] Generate(ReceiptDetailRow receipt)
    {
        var logoBytes = LoadLogo();
        var eatPaymentDate = ToEastAfricaTime(receipt.PaymentDate);
        var allocations = receipt.Allocations ?? new List<ReceiptAllocationRow>();
        var allocatedTotal = allocations.Sum(a => a.AmountAllocated);
        var unallocated = receipt.UnallocatedAmount > 0
            ? receipt.UnallocatedAmount
            : Math.Max(0, receipt.AmountPaid - allocatedTotal);

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A5);
                page.MarginTop(24);
                page.MarginBottom(20);
                page.MarginHorizontal(28);
                page.DefaultTextStyle(x => x
                    .FontSize(9)
                    .FontColor(TextDark)
                    .FontFamily(Fonts.Arial));

                page.Header().Element(c => ComposeHeader(c, receipt, logoBytes));
                page.Content().Element(c => ComposeBody(c, receipt, eatPaymentDate, allocations, unallocated));
                page.Footer().Element(ComposeFooter);
            });
        });

        return document.GeneratePdf();
    }

    private static void ComposeHeader(IContainer container, ReceiptDetailRow receipt, byte[]? logoBytes)
    {
        container.Column(col =>
        {
            col.Item().Height(4).Background(AccentRed);

            col.Item().PaddingTop(14).PaddingBottom(4).Row(row =>
            {
                row.RelativeItem().Column(left =>
                {
                    if (logoBytes is { Length: > 0 })
                    {
                        left.Item().Height(42).Width(42).Image(logoBytes).FitArea();
                    }
                    else
                    {
                        left.Item().Height(36).Width(36).Background(AccentRed).AlignCenter().AlignMiddle()
                            .Text("U").FontColor(White).Bold().FontSize(16);
                    }
                });

                row.RelativeItem().AlignRight().Column(right =>
                {
                    right.Item().AlignRight().Text("PAYMENT RECEIPT")
                        .FontSize(11).Bold().FontColor(AccentRed).LetterSpacing(0.5f);
                    right.Item().PaddingTop(2).AlignRight().Text(receipt.ReceiptNumber)
                        .FontSize(10).SemiBold().FontColor(TextDark);
                    right.Item().AlignRight().Text("Official payment confirmation")
                        .FontSize(7).FontColor(TextMuted);
                });
            });

            col.Item().PaddingTop(10).AlignCenter().Text(receipt.SchemeName)
                .FontSize(14).Bold().FontColor(TextDark);

            col.Item().PaddingTop(8).LineHorizontal(1).LineColor(Border);
        });
    }

    private static void ComposeBody(
        IContainer container,
        ReceiptDetailRow receipt,
        DateTime eatPaymentDate,
        List<ReceiptAllocationRow> allocations,
        decimal unallocated)
    {
        container.PaddingTop(12).Column(col =>
        {
            col.Item().Background(Surface).Border(1).BorderColor(Border).Padding(10).Column(card =>
            {
                card.Item().Row(r =>
                {
                    r.RelativeItem().Element(c => MetaBlock(c, "Receipt No.", receipt.ReceiptNumber));
                    r.RelativeItem().Element(c => MetaBlock(c, "Payment date", eatPaymentDate.ToString("dd MMM yyyy, HH:mm")));
                });
                card.Item().PaddingTop(8).Row(r =>
                {
                    r.RelativeItem().Element(c => MetaBlock(c, "Customer", receipt.FullName));
                    r.RelativeItem().Element(c => MetaBlock(c, "Account No.", receipt.AccountNo));
                });
                card.Item().PaddingTop(8).Row(r =>
                {
                    r.RelativeItem().Element(c => MetaBlock(c, "Transaction Ref.",
                        string.IsNullOrWhiteSpace(receipt.TransactionRef) ? "—" : receipt.TransactionRef));
                    r.RelativeItem().Element(c => MetaBlock(c, "Zone",
                        string.IsNullOrWhiteSpace(receipt.Zone) ? "—" : receipt.Zone!));
                });
                if (!string.IsNullOrWhiteSpace(receipt.Phone))
                {
                    card.Item().PaddingTop(8).Element(c => MetaBlock(c, "Phone", receipt.Phone!));
                }
            });

            // —— Line items table ——
            col.Item().PaddingTop(14).Text("PAYMENT DETAILS")
                .FontSize(8).Bold().FontColor(TextMuted).LetterSpacing(0.6f);

            col.Item().PaddingTop(6).Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(3.2f); // description
                    columns.RelativeColumn(2.0f); // reference
                    columns.ConstantColumn(72);   // amount
                });

                table.Header(header =>
                {
                    header.Cell().Element(c => CellHeader(c)).Text("Description");
                    header.Cell().Element(c => CellHeader(c)).Text("Reference");
                    header.Cell().Element(c => CellHeader(c)).AlignRight().Text("Amount");
                });

                if (allocations.Count == 0)
                {
                    table.Cell().ColumnSpan(2).Element(c => CellBody(c))
                        .Text("Payment received (no allocation lines)").FontColor(TextMuted).Italic();
                    table.Cell().Element(c => CellBody(c)).AlignRight()
                        .Text($"KSh {receipt.AmountPaid:N2}").SemiBold();
                }
                else
                {
                    var alt = false;
                    foreach (var a in allocations)
                    {
                        var bg = alt ? Surface : White;
                        alt = !alt;

                        var desc = string.IsNullOrWhiteSpace(a.Description)
                            ? (string.IsNullOrWhiteSpace(a.TargetType) ? "Allocation" : a.TargetType)
                            : a.Description;
                        var reference = string.IsNullOrWhiteSpace(a.Reference) ? "—" : a.Reference;

                        table.Cell().Element(c => CellBody(c, bg)).Text(desc);
                        table.Cell().Element(c => CellBody(c, bg)).Text(reference).FontColor(TextMuted).FontSize(8);
                        table.Cell().Element(c => CellBody(c, bg)).AlignRight()
                            .Text($"KSh {a.AmountAllocated:N2}").SemiBold();
                    }
                }

                if (unallocated > 0.009m)
                {
                    table.Cell().Element(c => CellBody(c, Surface))
                        .Text("Unallocated credit").FontColor(TextMuted).Italic();
                    table.Cell().Element(c => CellBody(c, Surface)).Text("—").FontColor(TextMuted);
                    table.Cell().Element(c => CellBody(c, Surface)).AlignRight()
                        .Text($"KSh {unallocated:N2}").FontColor(TextMuted);
                }
            });

            col.Item().PaddingTop(10).LineHorizontal(1).LineColor(Border);

            col.Item().PaddingTop(8).Row(row =>
            {
                row.RelativeItem().AlignMiddle().Column(t =>
                {
                    t.Item().Text("Amount paid").FontSize(8).FontColor(TextMuted);
                    t.Item().Text("Inclusive of all allocations above").FontSize(7).FontColor(TextMuted);
                });
                row.ConstantItem(130).AlignRight().AlignMiddle().Column(t =>
                {
                    t.Item().AlignRight().Text("TOTAL")
                        .FontSize(8).Bold().FontColor(TextMuted).LetterSpacing(0.5f);
                    t.Item().AlignRight().Text($"KSh {receipt.AmountPaid:N2}")
                        .FontSize(16).Bold().FontColor(AccentRed);
                });
            });

            col.Item().PaddingTop(12).Background(AccentRed).Height(3);

            col.Item().PaddingTop(10).AlignCenter()
                .Text("Thank you for your payment.")
                .FontSize(9).SemiBold().FontColor(TextDark);
        });
    }

    private static void ComposeFooter(IContainer container)
    {
        container.Column(col =>
        {
            col.Item().LineHorizontal(1).LineColor(Border);
            col.Item().PaddingTop(6).AlignCenter()
                .Text("Powered by Uniflow  ·  UNIFLOW SOFTWARE SLT.")
                .FontSize(7).FontColor(TextMuted);
            col.Item().AlignCenter()
                .Text("This receipt was generated electronically and is valid without a signature.")
                .FontSize(6.5f).FontColor(TextMuted);
            col.Item().PaddingTop(2).AlignCenter()
                .Text("Keep this document for your records.")
                .FontSize(6.5f).FontColor(TextMuted);
        });
    }

    private static void MetaBlock(IContainer container, string label, string value)
    {
        container.Column(c =>
        {
            c.Item().Text(label).FontSize(7).FontColor(TextMuted).LetterSpacing(0.3f);
            c.Item().PaddingTop(1).Text(value).FontSize(9).SemiBold().FontColor(TextDark);
        });
    }

    private static IContainer CellHeader(IContainer container) =>
        container
            .BorderBottom(1).BorderColor(Border)
            .Background(Surface)
            .PaddingVertical(5)
            .PaddingHorizontal(4)
            .DefaultTextStyle(x => x.FontSize(7.5f).Bold().FontColor(TextMuted));

    private static IContainer CellBody(IContainer container, string? background = null)
    {
        var c = container.BorderBottom(0.5f).BorderColor(Border).PaddingVertical(5).PaddingHorizontal(4);
        if (!string.IsNullOrEmpty(background))
            c = c.Background(background);
        return c;
    }

    private byte[]? LoadLogo()
    {
        var logoPath = Path.Combine(
            _environment.ContentRootPath,
            "Assets",
            "Branding",
            "uniflow-logo.png");

        return File.Exists(logoPath) ? File.ReadAllBytes(logoPath) : null;
    }

    private static DateTime ToEastAfricaTime(DateTime paymentDate)
    {
        try
        {
            var eatZone = TimeZoneInfo.FindSystemTimeZoneById("Africa/Nairobi");
            var utc = paymentDate.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(paymentDate, DateTimeKind.Utc)
                : paymentDate.ToUniversalTime();
            return TimeZoneInfo.ConvertTimeFromUtc(utc, eatZone);
        }
        catch (TimeZoneNotFoundException)
        {
            // Fallback if TZ id unavailable on host
            return paymentDate.AddHours(3);
        }
    }
}
