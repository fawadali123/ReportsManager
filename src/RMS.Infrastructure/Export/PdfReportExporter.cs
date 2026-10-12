using Microsoft.Extensions.Options;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using RMS.Core.Abstractions;
using RMS.Core.Reporting;
using RMS.Core.Services;

namespace RMS.Infrastructure.Export;

/// <summary>Bound from the "Export:Pdf" configuration section.</summary>
public sealed class PdfExportOptions
{
    /// <summary>
    /// QuestPDF licence tier: Community, Professional or Enterprise.
    /// Community is free only for organisations under the revenue limit in QuestPDF's licence terms;
    /// see https://www.questpdf.com/license/ before going live.
    /// </summary>
    public string License { get; set; } = "Community";

    /// <summary>
    /// Optional font, with fallbacks separated by commas (for example "Segoe UI, Tahoma").
    /// Choose fonts that cover Arabic if reports contain Arabic text. Empty uses QuestPDF's default.
    /// </summary>
    public string? FontFamily { get; set; }
}

/// <summary>
/// Writes a report as a paginated PDF table. Kept behind <see cref="IReportExporter"/> so the PDF
/// library can be swapped without touching anything else.
/// </summary>
internal sealed class PdfReportExporter : IReportExporter
{
    private const int MaxCellText = 300;
    private readonly ReportingOptions _reporting;
    private readonly string[] _fonts;
    private readonly LicenseType _license;

    public PdfReportExporter(ReportingOptions reporting, IOptions<PdfExportOptions> options)
    {
        _reporting = reporting;
        _fonts = (options.Value.FontFamily ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        _license = Enum.TryParse<LicenseType>(options.Value.License, ignoreCase: true, out var license)
            ? license
            : LicenseType.Community;
    }

    public string Format => "pdf";
    public string DisplayName => "PDF";
    public string ContentType => "application/pdf";
    public string FileExtension => ".pdf";
    public int MaxRows => _reporting.MaxPdfRows;

    public async Task<ExportSummary> ExportAsync(
        ReportExportContext context, IReportResultReader reader, Stream output, CancellationToken ct = default)
    {
        // QuestPDF loads its native rendering library the first time it is touched. Doing that here rather
        // than in the constructor means a server where that library cannot load loses PDF export only,
        // not the whole Reports screen.
        QuestPDF.Settings.License = _license;

        // Page layout needs all rows up front, which is why PDF has its own, lower row limit.
        var table = await reader.ReadTableAsync(context.MaxRows, ct);
        var columns = table.Columns;
        var weights = ColumnWeights(table);

        var fontSize = columns.Count switch
        {
            <= 6 => 9f,
            <= 10 => 8f,
            <= 16 => 7f,
            _ => 6f,
        };

        Document.Create(document =>
        {
            document.Page(page =>
            {
                page.Size(columns.Count > 5 ? PageSizes.A4.Landscape() : PageSizes.A4);
                page.Margin(24);
                page.DefaultTextStyle(style => WithFont(style).FontSize(fontSize));

                page.Header().PaddingBottom(8).Column(header =>
                {
                    header.Item().Text(context.Title).FontSize(15).SemiBold().FontColor(Colors.Teal.Darken4);

                    if (!string.IsNullOrWhiteSpace(context.Description))
                    {
                        header.Item().Text(context.Description).FontSize(9).FontColor(Colors.Grey.Darken2);
                    }

                    var source = string.Join(" / ", new[] { context.ApplicationName, context.CategoryName }
                        .Where(part => !string.IsNullOrWhiteSpace(part)));
                    header.Item().PaddingTop(2)
                        .Text($"{source}  |  Generated {context.GeneratedAt:yyyy-MM-dd HH:mm} by {context.GeneratedBy}".TrimStart(' ', '|'))
                        .FontSize(8).FontColor(Colors.Grey.Darken1);

                    if (context.Parameters.Count > 0)
                    {
                        header.Item().Text(string.Join("   ", context.Parameters.Select(p => $"{p.Key}: {p.Value}")))
                            .FontSize(8).FontColor(Colors.Grey.Darken1);
                    }
                });

                page.Content().Column(content =>
                {
                    if (columns.Count == 0 || table.Rows.Count == 0)
                    {
                        content.Item().PaddingTop(12).Text("The report returned no rows.").FontColor(Colors.Grey.Darken1);
                        return;
                    }

                    content.Item().Table(grid =>
                    {
                        grid.ColumnsDefinition(definition =>
                        {
                            foreach (var weight in weights)
                            {
                                definition.RelativeColumn(weight);
                            }
                        });

                        grid.Header(headerRow =>
                        {
                            foreach (var column in columns)
                            {
                                headerRow.Cell()
                                    .Background(Colors.Teal.Darken4)
                                    .PaddingVertical(4).PaddingHorizontal(3)
                                    .Text(column.Name).SemiBold().FontColor(Colors.White);
                            }
                        });

                        for (var r = 0; r < table.Rows.Count; r++)
                        {
                            var background = r % 2 == 0 ? Colors.White : Colors.Grey.Lighten4;
                            var row = table.Rows[r];

                            for (var c = 0; c < columns.Count; c++)
                            {
                                var cell = grid.Cell()
                                    .Background(background)
                                    .BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2)
                                    .PaddingVertical(3).PaddingHorizontal(3);

                                if (columns[c].Type == ReportColumnType.Number)
                                {
                                    cell = cell.AlignRight();
                                }

                                cell.Text(CellText(row[c]));
                            }
                        }
                    });

                    if (table.Truncated)
                    {
                        content.Item().PaddingTop(8)
                            .Text($"Stopped at {table.Rows.Count:N0} rows. The report has more; use Excel or CSV for the full result.")
                            .Italic().FontColor(Colors.Grey.Darken1);
                    }
                });

                page.Footer().AlignCenter().Text(footer =>
                {
                    footer.Span("Page ").FontSize(8);
                    footer.CurrentPageNumber().FontSize(8);
                    footer.Span(" of ").FontSize(8);
                    footer.TotalPages().FontSize(8);
                });
            });
        }).GeneratePdf(output);

        return new ExportSummary(table.Rows.Count, table.Truncated);
    }

    private TextStyle WithFont(TextStyle style) => _fonts.Length > 0 ? style.FontFamily(_fonts) : style;

    private static string CellText(object? value)
    {
        var text = ParameterValueConverter.ToDisplay(value);
        return text.Length > MaxCellText ? text[..MaxCellText] + "..." : text;
    }

    /// <summary>Relative column widths from the header and a sample of the data, clamped so no column dominates.</summary>
    private static float[] ColumnWeights(ReportTable table)
    {
        var weights = new float[table.Columns.Count];
        for (var c = 0; c < weights.Length; c++)
        {
            var longest = table.Columns[c].Name.Length;
            foreach (var row in table.Rows.Take(200))
            {
                longest = Math.Max(longest, ParameterValueConverter.ToDisplay(row[c]).Length);
            }
            weights[c] = Math.Clamp(longest, 5, 40);
        }
        return weights;
    }
}
