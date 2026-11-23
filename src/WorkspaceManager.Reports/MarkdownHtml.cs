using System.Net;
using Markdig;

namespace WorkspaceManager.Reports;

/// <summary>Markdown -> standalone HTML page (tables, lists), for opening an AI report in the browser.</summary>
public static class MarkdownHtml
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().DisableHtml().Build();

    public static string Page(string markdown, string title, string subtitle)
    {
        var body = Markdown.ToHtml(markdown ?? string.Empty, Pipeline);
        return $$"""
            <!DOCTYPE html><html lang="vi"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
            <title>{{WebUtility.HtmlEncode(title)}}</title><style>
            body{font-family:Segoe UI,Arial,sans-serif;margin:0;background:#f6f7f9;color:#1f2328;font-size:14px}
            .wrap{max-width:1100px;margin:0 auto;padding:20px 16px 40px}
            header{background:#1f3a5f;color:#fff;padding:14px 18px;border-radius:8px;margin-bottom:14px}
            header h1{margin:0;font-size:19px}header p{margin:4px 0 0;opacity:.85;font-size:12px}
            main{background:#fff;border:1px solid #d8dee4;border-radius:8px;padding:4px 22px 18px}
            h2{font-size:17px;border-bottom:2px solid #e3e8ee;padding-bottom:6px;margin-top:22px;color:#1f3a5f}
            table{border-collapse:collapse;width:100%;margin:8px 0 12px;font-size:13px}
            th,td{border:1px solid #d8dee4;padding:6px 8px;text-align:left;vertical-align:top}th{background:#eef2f6}
            code{background:#eef1f4;padding:1px 4px;border-radius:3px;font-size:12px}
            </style></head><body><div class="wrap">
            <header><h1>{{WebUtility.HtmlEncode(title)}}</h1><p>{{WebUtility.HtmlEncode(subtitle)}}</p></header>
            <main>{{body}}</main></div></body></html>
            """;
    }
}
