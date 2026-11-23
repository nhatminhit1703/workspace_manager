using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace WorkspaceManager.Git;

/// <summary>Result of one git process.</summary>
public sealed record GitResult(int ExitCode, string StandardOutput, string StandardError, TimeSpan Duration)
{
    public bool Success => ExitCode == 0;
}

public interface IGitRunner
{
    /// <summary>Runs <c>git &lt;arguments&gt;</c> in <paramref name="workingDirectory"/>. Throws only when git cannot be started or on cancel / timeout.</summary>
    Task<GitResult> RunAsync(string workingDirectory, IReadOnlyList<string> arguments, CancellationToken cancellationToken = default);
}

public sealed class GitRunnerOptions
{
    /// <summary>Path of the git executable; "git" = resolved from PATH.</summary>
    public string GitExecutable { get; set; } = "git";

    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>Extra environment variables (tests use them to isolate git from the user's configuration).</summary>
    public IDictionary<string, string> Environment { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
}

/// <summary>
/// Runs the git CLI. Arguments go through <see cref="ProcessStartInfo.ArgumentList"/> (no quoting problems),
/// the environment is fixed so output is machine-stable and git never waits for a prompt.
/// </summary>
public sealed class GitRunner(GitRunnerOptions options, ILogger<GitRunner>? logger = null) : IGitRunner
{
    private readonly ILogger _logger = (ILogger?)logger ?? NullLogger.Instance;

    public GitRunner()
        : this(new GitRunnerOptions())
    {
    }

    public async Task<GitResult> RunAsync(string workingDirectory, IReadOnlyList<string> arguments, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var psi = new ProcessStartInfo(options.GitExecutable)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
        };
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add("core.quotepath=false");
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add("color.ui=false");
        foreach (var a in arguments)
        {
            psi.ArgumentList.Add(a);
        }

        psi.Environment["LC_ALL"] = "C";
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        psi.Environment["GCM_INTERACTIVE"] = "never";
        psi.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        psi.Environment["GIT_PAGER"] = "cat";
        foreach (var (k, v) in options.Environment)
        {
            psi.Environment[k] = v;
        }

        using var timeout = new CancellationTokenSource(options.Timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        var sw = Stopwatch.StartNew();
        using var process = new Process { StartInfo = psi };
        process.Start();
        process.StandardInput.Close();

        var stdout = process.StandardOutput.ReadToEndAsync(linked.Token);
        var stderr = process.StandardError.ReadToEndAsync(linked.Token);
        try
        {
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
            var result = new GitResult(process.ExitCode, await stdout.ConfigureAwait(false), await stderr.ConfigureAwait(false), sw.Elapsed);
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("git {Args} in {Dir} -> {Code} ({Ms} ms)", string.Join(' ', arguments), workingDirectory, result.ExitCode, (int)sw.ElapsedMilliseconds);
            }
            return result;
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            if (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"git {string.Join(' ', arguments)} did not finish within {options.Timeout.TotalSeconds:0} s.");
            }

            throw;
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }
}
