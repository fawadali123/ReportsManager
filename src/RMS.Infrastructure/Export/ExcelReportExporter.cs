using System.Globalization;
using ClosedXML.Excel;
using RMS.Core.Abstractions;
using RMS.Core.Reporting;

namespace RMS.Infrastructure.Export;

/// <summary>Writes a report as an .xlsx workbook with typed cells, a frozen header row and a filter.</summary>
internal sealed class ExcelReportExporter : IReportExporter
{
    private const int MaxCellText = 32767;      // Excel's limit for one cell
    private const int ExcelMaxDataRows = 1_048_000;
    private const double MaxColumnWidth = 60;

    /// <summary>Excel stores numbers as doubles; beyond 15 digits they lose precision, so those are written as text.</summary>
    private const decimal MaxExactNumber = 999_999_999_999_999m;

    public string Format => "xlsx";
    public string DisplayName => "Excel";
    public string ContentType => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public string FileExtension => ".xlsx";
    public int MaxRows => ExcelMaxDataRows;

    public async Task<ExportSummary> ExportAsync(
        ReportExportContext context, IReportResultReader reader, Stream output, CancellationToken ct = default)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Report");
        var columns = reader.Columns;
        var row = 1;

        sheet.Cell(row, 1).Value = context.Title;
        sheet.Cell(row, 1).Style.Font.Bold = true;
        sheet.Cell(row, 1).Style.Font.FontSize = 14;
        row++;

        if (!string.IsNullOrWhiteSpace(context.Description))
        {
            sheet.Cell(row++, 1).Value = context.Description;
        }

        sheet.Cell(row, 1).Value = $"Generated {context.GeneratedAt:yyyy-MM-dd HH:mm} by {context.GeneratedBy}";
        sheet.Cell(row, 1).Style.Font.Italic = true;
        row++;

        foreach (var parameter in context.Parameters)
        {
            sheet.Cell(row, 1).Value = parameter.Key;
            sheet.Cell(row, 1).Style.Font.Bold = true;
            sheet.Cell(row, 2).Value = parameter.Value;
            row++;
        }

        row++; // blank line before the table
        var headerRow = row;
        for (var c = 0; c < columns.Count; c++)
        {
            sheet.Cell(headerRow, c + 1).Value = columns[c].Name;
        }

        if (columns.Count > 0)
        {
            var header = sheet.Range(headerRow, 1, headerRow, columns.Count);
            header.Style.Font.Bold = true;
            header.Style.Fill.BackgroundColor = XLColor.FromHtml("#DDEBEA");
            header.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
        }

        var rowCount = 0;
        var truncated = false;
        var hasTime = new bool[columns.Count];

        while (await reader.ReadAsync(ct))
        {
            if (rowCount >= context.MaxRows)
            {
                truncated = true;
                break;
            }

            row++;
            rowCount++;
            for (var c = 0; c < columns.Count; c++)
            {
                var value = reader.GetValue(c);
                if (value is DateTime date && date.TimeOfDay != TimeSpan.Zero)
                {
                    hasTime[c] = true;
                }
                SetCell(sheet.Cell(row, c + 1), value);
            }
        }

        if (rowCount > 0)
        {
            for (var c = 0; c < columns.Count; c++)
            {
                if (columns[c].Type == ReportColumnType.Date)
                {
                    sheet.Range(headerRow + 1, c + 1, row, c + 1).Style.DateFormat.Format =
                        hasTime[c] ? "yyyy-mm-dd hh:mm:ss" : "yyyy-mm-dd";
                }
            }
        }

        if (columns.Count > 0)
        {
            sheet.Range(headerRow, 1, row, columns.Count).SetAutoFilter();
            sheet.SheetView.FreezeRows(headerRow);

            // Measuring every row is slow on large exports; the first few hundred give a good width.
            sheet.Columns(1, columns.Count).AdjustToContents(headerRow, Math.Min(row, headerRow + 300));
            for (var c = 1; c <= columns.Count; c++)
            {
                if (sheet.Column(c).Width > MaxColumnWidth)
                {
                    sheet.Column(c).Width = MaxColumnWidth;
                }
            }
        }

        if (truncated)
        {
            sheet.Cell(row + 2, 1).Value = $"Stopped at {rowCount:N0} rows. The report has more; narrow the parameters to export the rest.";
            sheet.Cell(row + 2, 1).Style.Font.Italic = true;
        }

        workbook.SaveAs(output);
        return new ExportSummary(rowCount, truncated);
    }

    private static void SetCell(IXLCell cell, object? value)
    {
        switch (value)
        {
            case null:
                break;

            case string text:
                // Assigning a string never creates a formula, so text starting with '=' stays text.
                cell.Value = text.Length > MaxCellText ? text[..MaxCellText] : text;
                break;

            case DateTime date when date.Year >= 1900:
                cell.Value = date;
                break;

            case DateTime date:
                cell.Value = date.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                break;

            case bool flag:
                cell.Value = flag ? "Yes" : "No";
                break;

            case decimal number when Math.Abs(number) > MaxExactNumber:
                cell.Value = number.ToString(CultureInfo.InvariantCulture);
                break;

            case decimal number:
                cell.Value = (double)number;
                break;

            case double number when double.IsFinite(number):
                cell.Value = number;
                break;

            default:
                cell.Value = Convert.ToString(value, CultureInfo.InvariantCulture);
                break;
        }
    }
}
