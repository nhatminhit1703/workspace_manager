using System.Text;
using System.Text.RegularExpressions;
using WorkspaceManager.Core.Reports;
using WorkspaceManager.Core.Repositories;

namespace WorkspaceManager.Reports;

/// <summary>
/// Scans what a branch changes and turns each risk into one bilingual PCL case (VI + EN).
/// Rules are generic; workspace-specific values come from <see cref="BugHuntOptions"/>.
/// </summary>
public static partial class BugHuntEngine
{
    [GeneratedRegex(@"(\.min\.(js|css)$|\.lock$|package-lock\.json$|(^|/)docs?/|\.md$|\.(png|jpe?g|gif|svg|ico|woff2?|ttf|eot|map|pdf)$)", RegexOptions.IgnoreCase)]
    private static partial Regex SkipScan();

    [GeneratedRegex(@"(^|/)(tests?|__tests__|spec)/|Tests?\.(php|cs|java)$|\.(test|spec)\.", RegexOptions.IgnoreCase)]
    private static partial Regex TestFile();

    [GeneratedRegex(@"\.(env|ini|conf|ya?ml|properties|xml|json|sample|example)$|\.env\.", RegexOptions.IgnoreCase)]
    private static partial Regex ConfigFile();

    [GeneratedRegex(@"^\s*(//|#|\*|/\*|<!--)")]
    private static partial Regex CommentLine();

    [GeneratedRegex(@"\b(var_dump|print_r|dd|dump)\s*\(|\bdebugger\s*;|\.printStackTrace\(\)")]
    private static partial Regex DebugStrong();

    [GeneratedRegex(@"console\.(log|debug)\s*\(|System\.out\.println|Console\.WriteLine")]
    private static partial Regex DebugWeak();

    [GeneratedRegex(@"\b(localhost(:\d+)?|127\.0\.0\.1|192\.168\.\d{1,3}\.\d{1,3}|10\.\d{1,3}\.\d{1,3}\.\d{1,3})\b")]
    private static partial Regex LocalHost();

    [GeneratedRegex(@"[""'](\s*(select|update|delete|insert)\b[^""']*)[""']\s*(\.|\+)\s*\$?[a-z_]|DB::raw\([^)]*\$|whereRaw\([^)]*\$", RegexOptions.IgnoreCase)]
    private static partial Regex SqlConcat();

    [GeneratedRegex(@"catch\s*\([^)]*\)\s*\{\s*\}")]
    private static partial Regex EmptyCatch();

    [GeneratedRegex(@"(change_?me|your[_-]?(api[_-]?)?key|placeholder|x{4,}|\bTODO\b|\bFIXME\b)", RegexOptions.IgnoreCase)]
    private static partial Regex Placeholder();

    [GeneratedRegex(@"login|logout|session|password|payment|checkout|order|price|tax|security|auth|token|csrf|xss|inject", RegexOptions.IgnoreCase)]
    private static partial Regex CriticalTopic();

    [GeneratedRegex(@"cart|address|account|register|redirect|rewrite|proxy|404|permission|role", RegexOptions.IgnoreCase)]
    private static partial Regex HighTopic();

    public static List<Finding> Run(IReadOnlyList<BranchDiff> diffs, BugHuntOptions options)
    {
        ArgumentNullException.ThrowIfNull(diffs);
        ArgumentNullException.ThrowIfNull(options);
        var extraHost = string.IsNullOrWhiteSpace(options.ExtraHostPattern) ? null : new Regex(options.ExtraHostPattern, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
        var findings = new List<Finding>();
        foreach (var d in diffs)
        {
            var repoFindings = new List<Finding>();
            LineRules(d, extraHost, repoFindings.Add);
            StaleMinified(d, repoFindings.Add);
            DeviceParity(d, options, repoFindings.Add);
            NoTest(d, repoFindings.Add);
            CommitRegressions(d, repoFindings.Add);

            // Cap each rule per repository, keeping the most severe findings.
            findings.AddRange(repoFindings.GroupBy(f => f.Rule, StringComparer.Ordinal)
                .SelectMany(g => g.OrderBy(f => f.Severity).Take(options.MaxPerRule)));
        }

        return [.. findings.OrderBy(f => f.Severity).ThenBy(f => f.Repo, StringComparer.OrdinalIgnoreCase).ThenBy(f => f.Rule, StringComparer.Ordinal)];
    }

    private static void LineRules(BranchDiff d, Regex? extraHost, Action<Finding> add)
    {
        foreach (var a in d.AddedLines)
        {
            if (SkipScan().IsMatch(a.File))
            {
                continue;
            }

            var where = $"{a.File}:{a.Line}";
            var code = a.Text.Trim();
            var snippet = code.Length > 120 ? code[..120] + "..." : code;
            var isConfig = ConfigFile().IsMatch(a.File);
            var isComment = CommentLine().IsMatch(a.Text);

            if (isConfig && Placeholder().IsMatch(a.Text))
            {
                add(Dev(Severity.Medium, "placeholder", d, where, "Placeholder or TODO in a configuration file: " + snippet,
                    "Kiểm tra giá trị cấu hình chưa điền", "Review an unfilled configuration value"));
            }

            if (isComment || TestFile().IsMatch(a.File))
            {
                continue;
            }

            if (DebugStrong().IsMatch(a.Text))
            {
                add(Dev(Severity.Medium, "debug", d, where, "Debug output left in code: " + snippet, "Xoá lệnh debug còn sót", "Remove leftover debug output"));
            }
            else if (DebugWeak().IsMatch(a.Text))
            {
                add(Dev(Severity.Low, "debug", d, where, "Console output left in code: " + snippet, "Xoá log console còn sót", "Remove leftover console output"));
            }

            if (LocalHost().IsMatch(a.Text) || (extraHost?.IsMatch(a.Text) ?? false))
            {
                add(Dev(Severity.High, "hardcoded-host", d, where, "Hard-coded host: " + snippet, "Bỏ host viết cứng, dùng cấu hình", "Replace the hard-coded host with configuration"));
            }

            if (SqlConcat().IsMatch(a.Text))
            {
                add(Dev(Severity.High, "sql-concat", d, where, "SQL built by string concatenation (injection risk): " + snippet,
                    "Dùng tham số thay cho nối chuỗi SQL", "Use parameters instead of SQL string concatenation"));
            }

            if (EmptyCatch().IsMatch(a.Text))
            {
                add(Dev(Severity.Medium, "empty-catch", d, where, "Empty catch block swallows errors: " + snippet,
                    "Xử lý hoặc ghi log lỗi trong catch rỗng", "Handle or log the error in the empty catch"));
            }
        }
    }

    private static void StaleMinified(BranchDiff d, Action<Finding> add)
    {
        foreach (var (file, status) in d.ChangedFiles)
        {
            if (status == 'D' || !(file.EndsWith(".js", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".css", StringComparison.OrdinalIgnoreCase))
                || file.Contains(".min.", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var ext = Path.GetExtension(file);
            var min = file[..^ext.Length] + ".min" + ext;
            if (d.TreeFiles.Contains(min) && !d.ChangedFiles.ContainsKey(min))
            {
                add(Biz(Severity.High, "stale-min", d, min, $"{file} changed but {Path.GetFileName(min)} was not rebuilt",
                    new PclCase($"[{d.Repo}] Màn hình dùng {Path.GetFileName(file)} chạy theo code mới",
                        $"Môi trường test đã deploy branch {d.Branch}. Trình duyệt đã xoá cache.",
                        $"1. Mở màn hình có dùng {Path.GetFileName(file)}.\n2. Thực hiện thao tác mà branch này sửa.",
                        "Màn hình chạy đúng theo thay đổi mới (file .min.* đã được build lại), không còn hành vi cũ."),
                    new PclCase($"[{d.Repo}] The screen using {Path.GetFileName(file)} runs the new code",
                        $"The test environment runs branch {d.Branch}. Browser cache cleared.",
                        $"1. Open the screen that uses {Path.GetFileName(file)}.\n2. Do the action this branch changes.",
                        "The screen behaves as changed (the .min.* file was rebuilt), not the old way.")));
            }
        }
    }

    private static void DeviceParity(BranchDiff d, BugHuntOptions options, Action<Finding> add)
    {
        foreach (var (a, b) in options.DeviceParityPairs)
        {
            foreach (var (file, _) in d.ChangedFiles)
            {
                foreach (var (from, to) in new[] { (a, b), (b, a) })
                {
                    if (!file.Contains(from, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var twin = ReplaceFirst(file, from, to);
                    if (d.TreeFiles.Contains(twin) && !d.ChangedFiles.ContainsKey(twin))
                    {
                        add(Biz(Severity.Medium, "device-parity", d, twin, $"{file} changed but its twin {twin} did not",
                            new PclCase($"[{d.Repo}] Thay đổi có cả trên {to.Trim('/')} giống {from.Trim('/')}",
                                $"Môi trường test đã deploy branch {d.Branch}.",
                                $"1. Kiểm tra chức năng đã sửa trên {from.Trim('/')}.\n2. Kiểm tra cùng chức năng trên {to.Trim('/')}.",
                                $"Hai bên hoạt động giống nhau; nếu {to.Trim('/')} cố ý không đổi thì có xác nhận của dev."),
                            new PclCase($"[{d.Repo}] The change also applies on {to.Trim('/')} like {from.Trim('/')}",
                                $"The test environment runs branch {d.Branch}.",
                                $"1. Check the changed feature on {from.Trim('/')}.\n2. Check the same feature on {to.Trim('/')}.",
                                $"Both behave the same; if {to.Trim('/')} is intentionally unchanged, a developer confirms it.")));
                    }
                }
            }
        }
    }

    private static void NoTest(BranchDiff d, Action<Finding> add)
    {
        var code = d.ChangedFiles.Keys.Where(f => !SkipScan().IsMatch(f) && !ConfigFile().IsMatch(f) && !TestFile().IsMatch(f)).ToList();
        if (code.Count > 0 && !d.ChangedFiles.Keys.Any(f => TestFile().IsMatch(f)) && d.TreeFiles.Any(f => TestFile().IsMatch(f)))
        {
            add(Dev(Severity.Medium, "no-test", d, d.Repo, $"{code.Count} source file(s) changed and no test was added or updated",
                "Bổ sung test cho thay đổi", "Add or update tests for the change"));
        }
    }

    private static void CommitRegressions(BranchDiff d, Action<Finding> add)
    {
        foreach (var c in d.Commits)
        {
            var critical = CriticalTopic().IsMatch(c.Subject);
            if (!critical && !HighTopic().IsMatch(c.Subject))
            {
                continue;
            }

            add(Biz(critical ? Severity.Critical : Severity.High, "commit", d, c.ShortHash, c.Subject,
                new PclCase($"[{d.Repo}] Hồi quy: {c.Subject}",
                    $"Môi trường test đã deploy branch {d.Branch}.",
                    "1. Thực hiện luồng chính liên quan tới thay đổi này.\n2. Thử thêm trường hợp lỗi / dữ liệu biên.",
                    "Luồng chính chạy đúng như mô tả của commit; các chức năng liên quan khác không bị ảnh hưởng."),
                new PclCase($"[{d.Repo}] Regression: {c.Subject}",
                    $"The test environment runs branch {d.Branch}.",
                    "1. Run the main flow this change touches.\n2. Try error cases and edge data.",
                    "The main flow works as the commit describes; related features are not affected.")));
        }
    }

    private static Finding Dev(Severity sev, string rule, BranchDiff d, string where, string what, string vi, string en) =>
        new(sev, rule, d.Repo, where, what, Audience.Dev,
            new PclCase($"[{d.Repo}] {vi}", "Đọc code của branch " + d.Branch, "Mở " + where, "Đã sửa hoặc có lý do giữ nguyên: " + what),
            new PclCase($"[{d.Repo}] {en}", "Review the code of branch " + d.Branch, "Open " + where, "Fixed, or kept with a reason: " + what));

    private static Finding Biz(Severity sev, string rule, BranchDiff d, string where, string what, PclCase vi, PclCase en) =>
        new(sev, rule, d.Repo, where, what, Audience.Business, vi, en);

    private static string ReplaceFirst(string s, string from, string to)
    {
        var i = s.IndexOf(from, StringComparison.OrdinalIgnoreCase);
        return i < 0 ? s : s[..i] + to + s[(i + from.Length)..];
    }

    /// <summary>Two-column TSV (title, content) ready to paste into a spreadsheet.</summary>
    public static string ToTsv(IEnumerable<Finding> findings, bool vietnamese)
    {
        ArgumentNullException.ThrowIfNull(findings);
        var sb = new StringBuilder();
        foreach (var f in findings)
        {
            var c = vietnamese ? f.Vi : f.En;
            var content = vietnamese
                ? $"【Điều kiện】\n{c.Precondition}\n【Thao tác】\n{c.Steps}\n【Kỳ vọng】\n{c.Expected}"
                : $"[Precondition]\n{c.Precondition}\n[Steps]\n{c.Steps}\n[Expected]\n{c.Expected}";
            sb.Append(Cell($"[{f.Severity}] {c.Title}")).Append('\t').Append(Cell(content)).Append('\n');
        }

        return sb.ToString();
    }

    private static string Cell(string s) => "\"" + s.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}

/// <summary>Reads the diffs of every repository that has the branch, then runs the rules.</summary>
public sealed class BugHuntService(IWorkspaceScanner scanner, IBranchDiffReader reader)
{
    public async Task<(IReadOnlyList<BranchDiff> Diffs, IReadOnlyList<Finding> Findings)> RunAsync(string workspaceRoot, string branch, BugHuntOptions options, CancellationToken cancellationToken = default)
    {
        var diffs = new List<BranchDiff>();
        foreach (var path in scanner.FindRepositories(workspaceRoot))
        {
            if (await reader.ReadAsync(path, branch, cancellationToken).ConfigureAwait(false) is { } d)
            {
                diffs.Add(d);
            }
        }

        return (diffs, BugHuntEngine.Run(diffs, options));
    }
}
