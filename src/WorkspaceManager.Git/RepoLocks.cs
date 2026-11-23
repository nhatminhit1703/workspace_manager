using System.Collections.Concurrent;
using WorkspaceManager.Core.Repositories;

namespace WorkspaceManager.Git;

public sealed class RepoLocks : IRepoLocks
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    public async Task<IDisposable> AcquireAsync(string repoPath, CancellationToken cancellationToken = default)
    {
        var key = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repoPath));
        var gate = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Releaser(gate);
    }

    private sealed class Releaser(SemaphoreSlim gate) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                gate.Release();
            }
        }
    }
}
