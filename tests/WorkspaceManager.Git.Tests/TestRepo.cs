using System.Diagnostics;

namespace WorkspaceManager.Git.Tests;

/// <summary>
/// A throw-away git repository in the temp folder. Git runs isolated from the machine's configuration
/// (no global / system config), so tests behave the same on every machine and in CI.
/// </summary>
internal sealed class TestRepo : IDisposable
{
    private static readonly string EmptyConfig = CreateEmptyConfig();

    public TestRepo(string? name = null, string root = "")
    {
        Root = root.Length > 0 ? root : Path.Combine(Path.GetTempPath(), "wm-tests", Guid.NewGuid().ToString("N"));
        Path_ = System.IO.Path.Combine(Root, name ?? "repo");
        Directory.CreateDirectory(Path_);
    }

    public string Root { get; }

    public string Path_ { get; }

    public static GitRunnerOptions IsolatedOptions()
    {
        var o = new GitRunnerOptions();
        foreach (var (k, v) in IsolatedEnvironment())
        {
            o.Environment[k] = v;
        }

        return o;
    }

    public TestRepo Init(string branch = "main")
    {
        Git("init", "-q", "-b", branch);
        return this;
    }

    public TestRepo Write(string relativePath, string content)
    {
        var full = System.IO.Path.Combine(Path_, relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return this;
    }

    public TestRepo Commit(string message, params string[] files)
    {
        Write(files.Length > 0 ? files[0] : $"{Guid.NewGuid():N}.txt", message);
        Git("add", "-A");
        Git("commit", "-q", "-m", message);
        return this;
    }

    public string Git(params string[] args) => RunGit(Path_, args);

    public static string RunGit(string dir, params string[] args)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = dir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }

        foreach (var (k, v) in IsolatedEnvironment())
        {
            psi.Environment[k] = v;
        }

        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0)
        {
            throw new InvalidOperationException($"git {string.Join(' ', args)} failed ({p.ExitCode}): {stderr}");
        }

        return stdout;
    }

    public void Dispose()
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(f, FileAttributes.Normal);
            }

            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static Dictionary<string, string> IsolatedEnvironment() => new()
    {
        ["GIT_CONFIG_NOSYSTEM"] = "1",
        ["GIT_CONFIG_GLOBAL"] = EmptyConfig,
        ["GIT_AUTHOR_NAME"] = "Test",
        ["GIT_AUTHOR_EMAIL"] = "test@example.com",
        ["GIT_COMMITTER_NAME"] = "Test",
        ["GIT_COMMITTER_EMAIL"] = "test@example.com",
    };

    private static string CreateEmptyConfig()
    {
        var p = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "wm-tests", "empty.gitconfig");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(p)!);
        File.WriteAllText(p, "[core]\n\tautocrlf = false\n[init]\n\tdefaultBranch = main\n");
        return p;
    }
}
