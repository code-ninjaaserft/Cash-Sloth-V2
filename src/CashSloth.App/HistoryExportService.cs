using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CashSloth.Contracts;

namespace CashSloth.App;

internal static class HistoryExportService
{
    internal static string ExportRecording(string parentFolder, HistoryRecording recording, IReadOnlyList<SaleHistoryRecord> sales)
    {
        var folder = CreateExportFolder(parentFolder, recording.Name, recording.StartedAtUtc);
        WriteLocalSalesCsv(Path.Combine(folder, "sales.csv"), sales);
        WriteLocalSummaryCsv(Path.Combine(folder, "summary.csv"), recording, sales);
        WriteTimelinePng(
            Path.Combine(folder, "revenue-timeline.png"),
            recording.Name,
            sales.Select(value => (value.CompletedUtc, value.TotalCents)).ToArray());
        return folder;
    }

    internal static string ExportEventReport(
        string parentFolder,
        EventFinalReportResponse report,
        IReadOnlyList<EventSaleResponse> sales)
    {
        var folder = CreateExportFolder(parentFolder, report.EventName, report.EndedAtUtc);
        using (var writer = CreateWriter(Path.Combine(folder, "sales.csv")))
        {
            WriteRow(writer, "sale_id", "completed_utc", "received_utc", "event_nickname", "payment_method", "showcase", "subtotal_chf_cents", "tip_chf_cents", "total_chf_cents", "given_chf_cents", "change_chf_cents", "items");
            foreach (var sale in sales.OrderBy(value => value.CompletedAtUtc))
            {
                WriteRow(writer,
                    sale.ClientSaleId,
                    sale.CompletedAtUtc.ToUniversalTime().ToString("O"),
                    sale.ReceivedAtUtc.ToUniversalTime().ToString("O"),
                    sale.Nickname,
                    sale.PaymentMethod,
                    sale.IsShowcase ? "true" : "false",
                    sale.SubtotalCents.ToString(CultureInfo.InvariantCulture),
                    sale.TipCents.ToString(CultureInfo.InvariantCulture),
                    sale.TotalCents.ToString(CultureInfo.InvariantCulture),
                    sale.GivenCents.ToString(CultureInfo.InvariantCulture),
                    sale.ChangeCents.ToString(CultureInfo.InvariantCulture),
                    string.Join(" | ", sale.Lines.Select(line => $"{line.Quantity}x {line.Name}")));
            }
        }
        using (var writer = CreateWriter(Path.Combine(folder, "summary.csv")))
        {
            WriteRow(writer, "event_id", report.EventId.ToString("N"));
            WriteRow(writer, "event_name", report.EventName);
            WriteRow(writer, "started_utc", report.StartedAtUtc.ToUniversalTime().ToString("O"));
            WriteRow(writer, "ended_utc", report.EndedAtUtc.ToUniversalTime().ToString("O"));
            WriteRow(writer, "complete", report.IsComplete ? "true" : "false");
            WriteRow(writer, "missing_nicknames", string.Join(" | ", report.MissingNicknames));
            WriteRow(writer, "sales", report.Statistics.SaleCount.ToString(CultureInfo.InvariantCulture));
            WriteRow(writer, "subtotal_chf_cents", report.Statistics.SubtotalCents.ToString(CultureInfo.InvariantCulture));
            WriteRow(writer, "tips_chf_cents", report.Statistics.TipCents.ToString(CultureInfo.InvariantCulture));
            WriteRow(writer, "total_chf_cents", report.Statistics.TotalCents.ToString(CultureInfo.InvariantCulture));
        }
        WriteTimelinePng(
            Path.Combine(folder, "revenue-timeline.png"),
            report.EventName,
            sales.Where(value => !value.IsShowcase).Select(value => (value.CompletedAtUtc, value.TotalCents)).ToArray());
        return folder;
    }

    private static void WriteLocalSalesCsv(string path, IReadOnlyList<SaleHistoryRecord> sales)
    {
        using var writer = CreateWriter(path);
        WriteRow(writer, "sale_id", "completed_utc", "event_name", "event_nickname", "operator", "payment_method", "showcase", "subtotal_chf_cents", "tip_chf_cents", "total_chf_cents", "given_chf_cents", "change_chf_cents", "items");
        foreach (var sale in sales.OrderBy(value => value.CompletedUtc))
        {
            WriteRow(writer,
                sale.Id,
                sale.CompletedUtc.ToUniversalTime().ToString("O"),
                sale.EventName,
                sale.EventNickname ?? sale.RegisterName,
                sale.OperatorUsername,
                sale.PaymentMethod,
                sale.IsShowcase ? "true" : "false",
                sale.SubtotalCents.ToString(CultureInfo.InvariantCulture),
                sale.TipCents.ToString(CultureInfo.InvariantCulture),
                sale.TotalCents.ToString(CultureInfo.InvariantCulture),
                sale.GivenCents.ToString(CultureInfo.InvariantCulture),
                sale.ChangeCents.ToString(CultureInfo.InvariantCulture),
                string.Join(" | ", sale.Lines.Select(line => $"{line.Quantity}x {line.Name}")));
        }
    }

    private static void WriteLocalSummaryCsv(string path, HistoryRecording recording, IReadOnlyList<SaleHistoryRecord> sales)
    {
        using var writer = CreateWriter(path);
        WriteRow(writer, "recording_id", recording.Id);
        WriteRow(writer, "name", recording.Name);
        WriteRow(writer, "started_utc", recording.StartedAtUtc.ToUniversalTime().ToString("O"));
        WriteRow(writer, "ended_utc", recording.EndedAtUtc?.ToUniversalTime().ToString("O") ?? string.Empty);
        WriteRow(writer, "sales", sales.Count.ToString(CultureInfo.InvariantCulture));
        WriteRow(writer, "subtotal_chf_cents", sales.Sum(value => value.SubtotalCents).ToString(CultureInfo.InvariantCulture));
        WriteRow(writer, "tips_chf_cents", sales.Sum(value => value.TipCents).ToString(CultureInfo.InvariantCulture));
        WriteRow(writer, "total_chf_cents", sales.Sum(value => value.TotalCents).ToString(CultureInfo.InvariantCulture));
    }

    private static StreamWriter CreateWriter(string path) =>
        new(path, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

    private static void WriteRow(TextWriter writer, params string[] values) =>
        writer.WriteLine(string.Join(',', values.Select(EscapeCsv)));

    private static string EscapeCsv(string? value)
    {
        var text = value ?? string.Empty;
        if (text.Length > 0 && text[0] is '=' or '+' or '-' or '@')
        {
            text = "'" + text;
        }
        return text.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? $"\"{text.Replace("\"", "\"\"")}\"" : text;
    }

    private static string CreateExportFolder(string parentFolder, string name, DateTimeOffset timestamp)
    {
        var safeName = string.Concat(name.Select(value => Path.GetInvalidFileNameChars().Contains(value) ? '_' : value)).Trim();
        if (string.IsNullOrWhiteSpace(safeName)) safeName = "CashSloth";
        var folder = Path.Combine(parentFolder, $"{timestamp.LocalDateTime:yyyyMMdd-HHmmss}-{safeName}");
        var suffix = 1;
        var candidate = folder;
        while (Directory.Exists(candidate)) candidate = $"{folder}-{suffix++}";
        Directory.CreateDirectory(candidate);
        return candidate;
    }

    private static void WriteTimelinePng(
        string path,
        string title,
        IReadOnlyList<(DateTimeOffset CompletedAtUtc, long TotalCents)> sales)
    {
        const int width = 1200;
        const int height = 700;
        const double left = 100;
        const double top = 90;
        const double right = 45;
        const double bottom = 95;
        var grouped = sales.GroupBy(value => new DateTimeOffset(
                value.CompletedAtUtc.Year,
                value.CompletedAtUtc.Month,
                value.CompletedAtUtc.Day,
                value.CompletedAtUtc.Hour,
                0,
                0,
                value.CompletedAtUtc.Offset))
            .OrderBy(value => value.Key)
            .Select(value => (Time: value.Key, Total: value.Sum(item => item.TotalCents)))
            .ToArray();
        if (grouped.Length == 0) grouped = [(DateTimeOffset.UtcNow, 0L)];
        var max = Math.Max(1, grouped.Max(value => value.Total));
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
            DrawText(drawing, title, 28, Brushes.Black, new Point(left, 28));
            DrawText(drawing, "Revenue over time (CHF)", 16, Brushes.DimGray, new Point(left, 62));
            var chartWidth = width - left - right;
            var chartHeight = height - top - bottom;
            drawing.DrawLine(new Pen(Brushes.Gray, 1), new Point(left, top), new Point(left, top + chartHeight));
            drawing.DrawLine(new Pen(Brushes.Gray, 1), new Point(left, top + chartHeight), new Point(left + chartWidth, top + chartHeight));
            var barWidth = Math.Max(3, chartWidth / grouped.Length * 0.72);
            for (var index = 0; index < grouped.Length; index++)
            {
                var x = left + chartWidth * (index + 0.5) / grouped.Length;
                var barHeight = chartHeight * grouped[index].Total / max;
                drawing.DrawRectangle(new SolidColorBrush(Color.FromRgb(55, 125, 80)), null, new Rect(x - barWidth / 2, top + chartHeight - barHeight, barWidth, barHeight));
                if (grouped.Length <= 16 || index % Math.Max(1, grouped.Length / 12) == 0)
                {
                    DrawText(drawing, grouped[index].Time.LocalDateTime.ToString("dd.MM HH:mm"), 11, Brushes.DimGray, new Point(Math.Max(left, x - 36), top + chartHeight + 12));
                }
            }
            DrawText(drawing, $"CHF {max / 100m:0.00}", 12, Brushes.DimGray, new Point(10, top - 8));
            DrawText(drawing, "CHF 0.00", 12, Brushes.DimGray, new Point(20, top + chartHeight - 8));
            DrawText(drawing, $"Total: CHF {sales.Sum(value => value.TotalCents) / 100m:0.00} · Sales: {sales.Count}", 16, Brushes.Black, new Point(left, height - 42));
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static void DrawText(DrawingContext drawing, string text, double size, Brush brush, Point point)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            size,
            brush,
            1.0);
        drawing.DrawText(formatted, point);
    }
}
