using ClosedXML.Excel;
using TelegramBotApi.Models;

namespace TelegramBotApi.Services;

/// <summary>
/// Builds an Excel (.xlsx) report of homework completion for the class monitor to send to teachers.
/// Sheets: Сводка (totals), Задания (per task), Студенты (per student), Таблица сдачи (student × task).
/// All times are shown in the college time zone.
/// </summary>
public class StatsExportService
{
    private static readonly XLColor HeaderFill = XLColor.FromHtml("#1C1C1E");
    private static readonly XLColor DoneFill = XLColor.FromHtml("#D9F2E1");
    private static readonly XLColor OverdueFill = XLColor.FromHtml("#FADBD8");

    private readonly StatsService _stats;

    public StatsExportService(StatsService stats) => _stats = stats;

    public static string FileName(DateTime nowUtc) => $"Статистика_{LocalTime.FromUtc(nowUtc):yyyy-MM-dd}.xlsx";

    public async Task<byte[]> BuildAsync(DateTime nowUtc, CancellationToken ct)
    {
        var matrix = await _stats.GetCompletionMatrixAsync(nowUtc, ct);
        var overview = matrix.Overview;
        // Oldest first reads naturally in a report
        var tasks = overview.Tasks.OrderBy(t => t.Deadline ?? t.SentAt).ToList();
        var students = overview.Students.OrderBy(s => s.Name).ToList();

        using var wb = new XLWorkbook();
        AddSummary(wb, overview, nowUtc);
        AddTasks(wb, tasks, nowUtc);
        AddStudents(wb, students);
        AddMatrix(wb, tasks, students, matrix.Done, nowUtc);

        using var stream = new MemoryStream();
        wb.SaveAs(stream);
        return stream.ToArray();
    }

    private static void AddSummary(XLWorkbook wb, StatsOverview o, DateTime nowUtc)
    {
        var ws = wb.Worksheets.Add("Сводка");
        ws.Cell(1, 1).Value = "Статистика сдачи заданий";
        ws.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(16);
        ws.Cell(2, 1).Value = $"Выгружено {LocalTime.FromUtc(nowUtc):dd.MM.yyyy HH:mm}";
        ws.Cell(2, 1).Style.Font.SetFontColor(XLColor.Gray);

        var rows = new (string Label, XLCellValue Value)[]
        {
            ("Студентов", o.Totals.Students),
            ("Заданий", o.Totals.Tasks),
            ("Сдано, %", o.Totals.CompletionPercent),
            ("Просрочек", o.Totals.OverdueCount),
        };
        for (var i = 0; i < rows.Length; i++)
        {
            ws.Cell(4 + i, 1).Value = rows[i].Label;
            ws.Cell(4 + i, 2).Value = rows[i].Value;
            ws.Cell(4 + i, 2).Style.Font.SetBold();
        }

        ws.Cell(9, 1).Value = "Студентами считаются роли «Студент» и «Админ» (староста). " +
                              "Просрочка — дедлайн прошёл, а отметки «Сдано» нет.";
        ws.Cell(9, 1).Style.Font.SetItalic().Font.SetFontColor(XLColor.Gray);
        ws.Column(1).Width = 22;
        ws.Column(2).Width = 12;
    }

    private static void AddTasks(XLWorkbook wb, IReadOnlyList<TaskStats> tasks, DateTime nowUtc)
    {
        var ws = wb.Worksheets.Add("Задания");
        Header(ws, "Предмет", "Задание", "Опубликовано", "Дедлайн", "Сдали", "Из", "%", "Статус");

        for (var i = 0; i < tasks.Count; i++)
        {
            var t = tasks[i];
            var row = i + 2;
            ws.Cell(row, 1).Value = t.GroupName;
            ws.Cell(row, 2).Value = PlainText(t.Text);
            ws.Cell(row, 3).Value = LocalTime.FromUtc(t.SentAt);
            if (t.Deadline is { } dl) ws.Cell(row, 4).Value = LocalTime.FromUtc(dl);
            ws.Cell(row, 5).Value = t.Completed;
            ws.Cell(row, 6).Value = t.Total;
            ws.Cell(row, 7).Value = t.Total == 0 ? 0 : Math.Round(100.0 * t.Completed / t.Total);

            var overdue = t.Deadline < nowUtc && t.Completed < t.Total;
            ws.Cell(row, 8).Value = t.Completed == t.Total && t.Total > 0 ? "все сдали"
                : overdue ? "просрочено"
                : t.Deadline == null ? "без срока" : "в процессе";
            if (overdue) ws.Cell(row, 8).Style.Font.SetFontColor(XLColor.FromHtml("#C0392B"));
        }

        ws.Range(2, 3, Math.Max(2, tasks.Count + 1), 4).Style.DateFormat.Format = "dd.MM.yyyy HH:mm";
        Finish(ws, wrapColumn: 2, wrapWidth: 50);
    }

    private static void AddStudents(XLWorkbook wb, IReadOnlyList<StudentStats> students)
    {
        var ws = wb.Worksheets.Add("Студенты");
        Header(ws, "Студент", "Ник", "Роль", "Сдано", "Из", "%", "Просрочено");

        for (var i = 0; i < students.Count; i++)
        {
            var s = students[i];
            var row = i + 2;
            ws.Cell(row, 1).Value = s.Name;
            ws.Cell(row, 2).Value = string.IsNullOrEmpty(s.Username) ? "" : "@" + s.Username;
            ws.Cell(row, 3).Value = s.Role == RoleType.Admin ? "Староста" : "Студент";
            ws.Cell(row, 4).Value = s.Completed;
            ws.Cell(row, 5).Value = s.Total;
            ws.Cell(row, 6).Value = s.Total == 0 ? 0 : Math.Round(100.0 * s.Completed / s.Total);
            ws.Cell(row, 7).Value = s.Overdue;
            if (s.Overdue > 0) ws.Cell(row, 7).Style.Fill.SetBackgroundColor(OverdueFill);
        }
        Finish(ws);
    }

    private static void AddMatrix(XLWorkbook wb, IReadOnlyList<TaskStats> tasks, IReadOnlyList<StudentStats> students,
        IReadOnlyDictionary<(int TaskId, long UserId), DateTime?> done, DateTime nowUtc)
    {
        var ws = wb.Worksheets.Add("Таблица сдачи");
        ws.Cell(1, 1).Value = "Студент";
        ws.Cell(2, 1).Value = "дедлайн →";

        for (var c = 0; c < tasks.Count; c++)
        {
            var t = tasks[c];
            var col = c + 2;
            var title = PlainText(t.Text);
            ws.Cell(1, col).Value = $"{t.GroupName}: {(title.Length > 40 ? title[..40] + "…" : title)}";
            ws.Cell(2, col).Value = t.Deadline is { } dl ? LocalTime.FromUtc(dl).ToString("dd.MM HH:mm") : "без срока";
            ws.Column(col).Width = 16;
        }
        StyleHeader(ws.Range(1, 1, 2, tasks.Count + 1));
        ws.Row(1).Style.Alignment.SetWrapText().Alignment.SetVertical(XLAlignmentVerticalValues.Top);
        ws.Row(1).Height = 48;

        for (var r = 0; r < students.Count; r++)
        {
            var s = students[r];
            var row = r + 3;
            ws.Cell(row, 1).Value = s.Name;
            for (var c = 0; c < tasks.Count; c++)
            {
                var t = tasks[c];
                var cell = ws.Cell(row, c + 2);
                cell.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
                if (done.TryGetValue((t.Id, s.TelegramId), out var at))
                {
                    cell.Value = "✓";
                    cell.Style.Fill.SetBackgroundColor(DoneFill);
                    if (at is { } completed) cell.CreateComment().AddText($"Сдано {LocalTime.FromUtc(completed):dd.MM HH:mm}");
                }
                else if (t.Deadline < nowUtc)
                {
                    cell.Value = "✗";
                    cell.Style.Fill.SetBackgroundColor(OverdueFill);
                }
            }
        }

        ws.Column(1).Width = 26;
        ws.SheetView.Freeze(2, 1);
        var legendRow = students.Count + 5;
        ws.Cell(legendRow, 1).Value = "✓ — сдано · ✗ — дедлайн прошёл, не сдано · пусто — ещё не сдано";
        ws.Cell(legendRow, 1).Style.Font.SetItalic().Font.SetFontColor(XLColor.Gray);
    }

    // ---------- helpers ----------

    private static void Header(IXLWorksheet ws, params string[] titles)
    {
        for (var i = 0; i < titles.Length; i++) ws.Cell(1, i + 1).Value = titles[i];
        StyleHeader(ws.Range(1, 1, 1, titles.Length));
    }

    private static void StyleHeader(IXLRange range)
    {
        range.Style.Font.SetBold().Font.SetFontColor(XLColor.White).Fill.SetBackgroundColor(HeaderFill);
    }

    private static void Finish(IXLWorksheet ws, int? wrapColumn = null, double wrapWidth = 0)
    {
        ws.SheetView.FreezeRows(1);
        ws.RangeUsed()?.SetAutoFilter();
        ws.Columns().AdjustToContents(8.0, 40.0);
        if (wrapColumn is { } col)
        {
            ws.Column(col).Width = wrapWidth;
            ws.Column(col).Style.Alignment.SetWrapText();
        }
    }

    /// <summary>Markdown → plain text for table cells.</summary>
    public static string PlainText(string text) =>
        System.Text.RegularExpressions.Regex.Replace(
            System.Text.RegularExpressions.Regex.Replace(text ?? "", @"[*_`#>~]", ""), @"\s+", " ").Trim();
}
