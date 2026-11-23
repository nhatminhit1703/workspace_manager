using WorkspaceManager.Core.Reports;
using WorkspaceManager.Core.Repositories;

namespace WorkspaceManager.Reports;

public interface IPmReportService
{
    Task<WorkspaceReport> BuildAsync(string workspaceRoot, ReportOptions options, CancellationToken cancellationToken = default);
}

/// <summary>Reads every repository of the workspace in parallel, then applies the report rules.</summary>
public sealed class PmReportService(IWorkspaceScanner scanner, IRepoInsightReader reader, TimeProvider time) : IPmReportService
{
    private const int MaxParallel = 8;

    public async Task<WorkspaceReport> BuildAsync(string workspaceRoot, ReportOptions options, CancellationToken cancellationToken = default)
    {
        var now = time.GetLocalNow();
        var paths = scanner.FindRepositories(workspaceRoot);
        using var gate = new SemaphoreSlim(MaxParallel);
        var insights = await Task.WhenAll(paths.Select(async p =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await reader.ReadAsync(p, options, now, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
            }
        })).ConfigureAwait(false);

        return PmReportBuilder.Build(workspaceRoot, insights, options, now);
    }
}
