using System.Text;
using RMS.Core.Abstractions;
using RMS.Core.Reporting;

namespace RMS.Infrastructure.Export;

/// <summary>
/// Writes a report as comma-separated text. Rows are streamed straight from the database,
/// so this is the format to use for very large extracts.
/// </summary>
internal sealed class CsvReportExporter : IReportExporter
{
    public string Format => "csv";
    public string DisplayName => "CSV";
    public string ContentType => "text/csv";
    public string FileExtension => ".csv";
    public int MaxRows => int.MaxValue;

    public async Task<ExportSummary> ExportAsync(
        ReportExportContext context, IReportResultReader reader, Stream output, CancellationToken ct = default)
    {
        // The byte order mark makes Excel open the file as UTF-8, which matters for Arabic text.
        await using var writer = new StreamWriter(output, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), 64 * 1024, leaveOpen: true);
        var columns = reader.Columns;
        var line = new StringBuilder();

        for (var c = 0; c < columns.Count; c++)
        {
            AppendField(line, columns[c].Name, c, isText: true);
        }
        await writer.WriteLineAsync(line, ct);

        var rowCount = 0;
        var truncated = false;

        while (await reader.ReadAsync(ct))
        {
            if (rowCount >= context.MaxRows)
            {
                truncated = true;
                break;
            }

            line.Clear();
            for (var c = 0; c < columns.Count; c++)
            {
                var value = reader.GetValue(c);
                AppendField(line, ParameterValueConverter.ToDisplay(value), c, isText: value is string);
            }
            await writer.WriteLineAsync(line, ct);
            rowCount++;
        }

        await writer.FlushAsync(ct);
        return new ExportSummary(rowCount, truncated);
    }

    private static void AppendField(StringBuilder line, string text, int index, bool isText)
    {
        if (index > 0)
        {
            line.Append(',');
        }

        // Spreadsheets run text cells that start with these characters as formulas. A leading apostrophe disarms them.
        if (isText && text.Length > 0 && text[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
        {
            text = "'" + text;
        }

        if (text.AsSpan().IndexOfAny(",\"\r\n") >= 0)
        {
            line.Append('"').Append(text.Replace("\"", "\"\"")).Append('"');
        }
        else
        {
            line.Append(text);
        }
    }
}
