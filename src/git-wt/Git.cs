using System.ComponentModel;
using System.Diagnostics;

/// <summary>
/// Low-level helpers for running git commands as child processes.
/// </summary>
static class Git
{
    /// <summary>
    /// Runs a git command against the given bare repo, capturing stdout and stderr.
    /// </summary>
    public static (int ExitCode, string Output, string Error) Run(string gitDir, params string[] args)
    {
        var psi = new ProcessStartInfo("git")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        psi.ArgumentList.Add($"--git-dir={gitDir}");
        foreach (var arg in args) psi.ArgumentList.Add(arg);

        using var p = StartProcess(psi);
        // Read stderr asynchronously to avoid deadlocks when both buffers fill.
        var errorTask = p.StandardError.ReadToEndAsync();
        var output = p.StandardOutput.ReadToEnd();
        var error = errorTask.GetAwaiter().GetResult();
        p.WaitForExit();
        return (p.ExitCode, output, error);
    }

    /// <summary>
    /// Runs a git command against the given bare repo with output streamed to the console.
    /// </summary>
    public static int RunLive(string gitDir, params string[] args)
    {
        var psi = new ProcessStartInfo("git") { UseShellExecute = false };
        psi.ArgumentList.Add($"--git-dir={gitDir}");
        foreach (var arg in args) psi.ArgumentList.Add(arg);

        using var p = StartProcess(psi);
        p.WaitForExit();
        return p.ExitCode;
    }

    /// <summary>
    /// Runs a git command without --git-dir, streaming output to the console.
    /// Used for commands like <c>git clone</c> that don't operate on an existing repo.
    /// </summary>
    public static int RunPlainLive(params string[] args)
    {
        var psi = new ProcessStartInfo("git") { UseShellExecute = false };
        foreach (var arg in args) psi.ArgumentList.Add(arg);

        using var p = StartProcess(psi);
        p.WaitForExit();
        return p.ExitCode;
    }

    /// <summary>
    /// Validates a branch name using <c>git check-ref-format</c>.
    /// </summary>
    public static bool IsValidBranchName(string name)
    {
        var psi = new ProcessStartInfo("git", ["check-ref-format", "--branch", name])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var p = StartProcess(psi);
        p.WaitForExit();
        return p.ExitCode == 0;
    }

    /// <summary>
    /// Determines the default branch (e.g. main or master) for the given bare repo.
    /// Tries <c>origin/HEAD</c> first, then falls back to well-known branch names.
    /// Returns null if no default branch can be determined.
    /// </summary>
    public static string? GetDefaultBranch(string gitDir)
    {
        var (exit, output, _) = Run(gitDir, "symbolic-ref", "refs/remotes/origin/HEAD", "--short");
        if (exit == 0 && !string.IsNullOrWhiteSpace(output))
            return output.Trim();

        if (Run(gitDir, "rev-parse", "--verify", "origin/main").ExitCode == 0)
            return "origin/main";
        if (Run(gitDir, "rev-parse", "--verify", "origin/master").ExitCode == 0)
            return "origin/master";

        return null;
    }

    /// <summary>
    /// Starts a process, providing a clear error if git is not installed.
    /// </summary>
    static Process StartProcess(ProcessStartInfo psi)
    {
        try
        {
            return Process.Start(psi)
                ?? throw new InvalidOperationException("Process.Start returned null.");
        }
        catch (Win32Exception)
        {
            Console.Error.WriteLine("Error: 'git' was not found. Ensure git is installed and on your PATH.");
            Environment.Exit(1);
            throw; // unreachable, satisfies return type
        }
    }
}

/// <summary>
/// Temporarily enables <c>extensions.relativeWorktrees</c> and <c>worktree.useRelativePaths</c>
/// for the duration of a <c>using</c> block, restoring the previous values on dispose.
/// This ensures worktree gitdir/commondir links use relative paths, making the
/// repo directory relocatable.
/// </summary>
sealed class RelativeWorktreeScope : IDisposable
{
    readonly string _gitDir;
    readonly bool _relExtWasEnabled;
    readonly bool _relPathWasEnabled;

    public RelativeWorktreeScope(string gitDir)
    {
        _gitDir = gitDir;

        var (relExtExit, relExtOutput, _) = Git.Run(gitDir, "config", "--get", "extensions.relativeWorktrees");
        _relExtWasEnabled = relExtExit == 0 && relExtOutput.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);

        var (relPathExit, relPathOutput, _) = Git.Run(gitDir, "config", "--get", "worktree.useRelativePaths");
        _relPathWasEnabled = relPathExit == 0 && relPathOutput.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);

        if (!_relExtWasEnabled)
            Git.Run(gitDir, "config", "extensions.relativeWorktrees", "true");
        if (!_relPathWasEnabled)
            Git.Run(gitDir, "config", "worktree.useRelativePaths", "true");
    }

    public void Dispose()
    {
        if (!_relExtWasEnabled)
            Git.Run(_gitDir, "config", "--unset", "extensions.relativeWorktrees");
        if (!_relPathWasEnabled)
            Git.Run(_gitDir, "config", "--unset", "worktree.useRelativePaths");
    }
}
