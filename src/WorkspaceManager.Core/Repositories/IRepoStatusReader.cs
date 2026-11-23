namespace WorkspaceManager.Core.Repositories;

public interface IRepoStatusReader
{
    /// <summary>Reads the status of one repository. Never throws for git errors: they go to <see cref="RepoStatus.Error"/>.</summary>
    Task<RepoStatus> ReadAsync(string repoPath, CancellationToken cancellationToken = default);
}

public interface IWorkspaceScanner
{
    /// <summary>Folders directly under <paramref name="workspaceRoot"/> that are git working trees, sorted by name.</summary>
    IReadOnlyList<string> FindRepositories(string workspaceRoot);
}
