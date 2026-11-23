using System.Globalization;
using WorkspaceManager.Core.Ai;
using WorkspaceManager.Core.Reports;

namespace WorkspaceManager.Reports;

/// <summary>Overview report written by an AI model from the PM report data and the user's notes.</summary>
public sealed class AiReportService(IAiClient ai)
{
    public const int MaxDataChars = 120_000;

    /// <summary>The exact text sent to the model (shown to the user before sending).</summary>
    public static string BuildPrompt(WorkspaceReport report, string? notes)
    {
        ArgumentNullException.ThrowIfNull(report);
        var data = ReportText.Summary(report);
        if (!string.IsNullOrWhiteSpace(notes))
        {
            data += "\n## NOTES FROM ME (manual, treat as facts)\n" + notes.Trim() + "\n";
        }

        if (data.Length > MaxDataChars)
        {
            data = data[..MaxDataChars] + "\n...(truncated)";
        }

        return string.Create(CultureInfo.InvariantCulture, $$"""
            You are the project manager of a software team. Below is raw data about the git repositories of a
            workspace (status, rule-based action items, commits of the last {{report.Options.Days}} day(s)) and notes from
            the owner of the workspace. Write an OVERVIEW REPORT for that person.

            Rules:
            - Write in Vietnamese. Keep repository names, branch names and code identifiers as they are.
            - Output Markdown only: headings (##), bullet lists, tables. No HTML, no code fences around the whole answer.
            - Be concrete: cite repository, branch and numbers from the data. Never invent facts; say when something is unknown.
            - Sections, in this order:
              1. "Tóm tắt": 3 to 6 bullets, the state today and the single most important thing to do.
              2. "Việc cần làm ngay": table (Ưu tiên | Repo | Việc | Lý do), most urgent first.
              3. "Tình trạng repo": table of the repositories worth attention only, then one line for the rest.
              4. "Tiến độ theo nhánh / tính năng": group commits by branch or ticket, what was done, by whom.
              5. "Chất lượng & rủi ro".
              6. "Daily report của tôi": Hôm qua / Hôm nay / Vấn đề, from the owner's commits and notes.
              7. "Hoạt động team": who worked on what.

            RAW DATA (generated {{report.GeneratedAt:yyyy-MM-dd HH:mm}}):
            {{data}}
            """);
    }

    public Task<string> GenerateAsync(WorkspaceReport report, string? notes, string model, CancellationToken cancellationToken = default) =>
        ai.GenerateAsync(BuildPrompt(report, notes), model, cancellationToken);
}
