using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace FoiaProcessor.Agents.Infrastructure;

/// <summary>
/// Renders a plain-text document body as a paginated PDF.
/// Used by <see cref="Agents.PackagingReleaseAgent"/> to convert each
/// approved (redacted) document to PDF before zipping the release package.
/// </summary>
public static class PdfRenderer
{
    static PdfRenderer()
    {
        // QuestPDF Community license: free for organizations under $1M USD revenue.
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public static byte[] Render(string title, string body)
    {
        var safeTitle = string.IsNullOrWhiteSpace(title) ? "Document" : title;
        var safeBody = body ?? string.Empty;

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.Letter);
                page.Margin(1, Unit.Inch);
                page.DefaultTextStyle(t => t.FontSize(10).FontFamily(Fonts.Consolas));

                page.Header().PaddingBottom(8).BorderBottom(1).BorderColor(Colors.Grey.Lighten2)
                    .Text(safeTitle).SemiBold().FontSize(12).FontColor(Colors.Grey.Darken3);

                page.Content().PaddingVertical(10).Text(safeBody);

                page.Footer().AlignRight().Text(t =>
                {
                    t.DefaultTextStyle(s => s.FontSize(9).FontColor(Colors.Grey.Darken1));
                    t.Span("Page ");
                    t.CurrentPageNumber();
                    t.Span(" of ");
                    t.TotalPages();
                });
            });
        });

        return document.GeneratePdf();
    }
}
