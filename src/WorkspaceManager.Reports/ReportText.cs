using System.Globalization;
using System.Net;
using System.Text;
using WorkspaceManager.Core.Reports;

namespace WorkspaceManager.Reports;

/// <summary>Text and HTML renderings of a report (daily report to paste, HTML file to share, input for the AI report).</summary>
public static class ReportText
{
    /// <summary>My commits grouped by day, newest day first: ready to paste into a daily report.</summary>
    public static string DailyReport(WorkspaceReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var sb = new StringBuilder();
        foreach (var day in report.MyCommits.GroupBy(c => c.Date.LocalDateTime.Date).OrderByDescending(g => g.Key))
        {
            sb.Append(CultureInfo.InvariantCulture, $"{day.Key:yyyy-MM-dd}").AppendLine();
            foreach (var repo in day.GroupBy(c => c.Repo).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
            {
                sb.Append("  ").AppendLine(repo.Key);
                foreach (var c in repo.OrderBy(c => c.Date))
                {
                    sb.Append("    - ").AppendLine(c.Subject);
                }
            }
        }

        return sb.Length == 0 ? "No commit of mine in this period." : sb.ToString().TrimEnd();
    }

    /// <summary>Plain-text summary: KPIs, action items and activity (used as the data of the AI report).</summary>
    public static string Summary(WorkspaceReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var sb = new StringBuilder();
        sb.AppendLine("## KPIs");
        foreach (var k in report.Kpis)
        {
            sb.Append(CultureInfo.InvariantCulture, $"{k.Name}: {k.Value}").AppendLine();
        }

        sb.AppendLine().AppendLine("## ACTION ITEMS");
        foreach (var a in report.Actions)
        {
            sb.Append(CultureInfo.InvariantCulture, $"[{a.Level}] {a.Repo}: {a.Issue} -> {a.SuggestedAction}").AppendLine();
        }

        sb.AppendLine().AppendLine("## REPOSITORIES");
        foreach (var r in report.Repos)
        {
            var b = r.Status.Branch;
            sb.Append(CultureInfo.InvariantCulture,
                $"{r.Status.Name} | branch {b.Head ?? "(detached)"} | {r.Status.Changes.Total} uncommitted | ahead {b.Ahead?.ToString(CultureInfo.InvariantCulture) ?? "-"} behind {b.Behind?.ToString(CultureInfo.InvariantCulture) ?? "-"} | stashes {r.Stashes.Count}").AppendLine();
            if (r.Tracking is { OnRemote: true } t)
            {
                sb.Append(CultureInfo.InvariantCulture, $"  focus branch {t.Branch}: on origin, local={t.OnLocal}, checked out={t.IsCurrent}").AppendLine();
            }
        }

        sb.AppendLine().Append(CultureInfo.InvariantCulture, $"## COMMITS (last {report.Options.Days} days, newest first)").AppendLine();
        foreach (var c in report.Commits.Take(400))
        {
            sb.Append(CultureInfo.InvariantCulture, $"{c.Date:yyyy-MM-dd} | {c.Repo} | {c.Author} | {c.Subject}").AppendLine();
        }

        return sb.ToString();
    }

    public static string Html(WorkspaceReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        static string E(string? s) => WebUtility.HtmlEncode(s ?? string.Empty);
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>Workspace PM Report</title><style>")
          .AppendLine("body{font-family:Segoe UI,Arial,sans-serif;font-size:13px;color:#1f2328;background:#f6f8fa;margin:0;padding:16px 20px}")
          .AppendLine("h1{font-size:20px;margin:0}h2{font-size:15px;margin:22px 0 8px;border-bottom:1px solid #d0d7de;padding-bottom:4px}.sub{color:#59636e}")
          .AppendLine(".kpi{display:inline-block;width:140px;margin:4px;padding:8px 10px;background:#fff;border:1px solid #d0d7de;border-radius:6px}.kpi b{display:block;font-size:22px}")
          .AppendLine(".Warning b{color:#bc4c00}.Bad b{color:#cf222e}table{border-collapse:collapse;width:100%;background:#fff;border:1px solid #d0d7de}")
          .AppendLine("th,td{padding:5px 8px;border-bottom:1px solid #eaeef2;text-align:left;vertical-align:top}th{background:#f0f3f6}")
          .AppendLine(".lv{font-weight:700;font-size:11px;padding:1px 6px;border-radius:10px;color:#fff}.High{background:#cf222e}.Medium{background:#bc4c00}.Low{background:#6e7781}.Info{background:#0969da}")
          .AppendLine("pre{background:#fff;border:1px solid #d0d7de;padding:10px;white-space:pre-wrap}</style></head><body>");
        sb.Append(CultureInfo.InvariantCulture, $"<h1>Workspace PM Report</h1><div class=\"sub\">Generated {report.GeneratedAt:yyyy-MM-dd HH:mm} &middot; {E(report.WorkspaceRoot)} &middot; last {report.Options.Days} day(s)</div>").AppendLine();
        sb.AppendLine("<div>");
        foreach (var k in report.Kpis)
        {
            sb.Append(CultureInfo.InvariantCulture, $"<div class=\"kpi {k.Tone}\"><b>{k.Value}</b>{E(k.Name)}</div>");
        }

        sb.AppendLine("</div>").Append(CultureInfo.InvariantCulture, $"<h2>Action items ({report.Actions.Count})</h2>")
          .AppendLine("<table><tr><th>Priority</th><th>Repo</th><th>Issue</th><th>Suggested action</th></tr>");
        foreach (var a in report.Actions)
        {
            sb.Append(CultureInfo.InvariantCulture, $"<tr><td><span class=\"lv {a.Level}\">{a.Level}</span></td><td>{E(a.Repo)}</td><td>{E(a.Issue)}</td><td>{E(a.SuggestedAction)}</td></tr>").AppendLine();
        }

        sb.AppendLine("</table><h2>My work (daily report)</h2>").Append("<pre>").Append(E(DailyReport(report))).AppendLine("</pre>");
        sb.AppendLine("<h2>Team activity</h2><table><tr><th>Author</th><th>Commits</th><th>Repos</th><th>Last commit</th></tr>");
        foreach (var g in report.Commits.GroupBy(c => c.Author).OrderByDescending(g => g.Count()))
        {
            sb.Append(CultureInfo.InvariantCulture, $"<tr><td>{E(g.Key)}</td><td>{g.Count()}</td><td>{E(string.Join(", ", g.Select(c => c.Repo).Distinct()))}</td><td>{g.Max(c => c.Date):yyyy-MM-dd HH:mm}</td></tr>").AppendLine();
        }

        sb.AppendLine("</table></body></html>");
        return sb.ToString();
    }
}
