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
/// Ensures the repo is configured for relative worktree paths by upgrading to
/// <c>repositoryformatversion = 1</c> and enabling <c>extensions.relativeWorktrees</c>.
/// These settings are permanent — once any worktree has been created with relative paths,
/// git requires them to be present or it will refuse to operate on the repo.
/// </summary>
static class RelativeWorktrees
{
    public static void EnsureEnabled(string gitDir)
    {
        var (verExit, verOutput, _) = Git.Run(gitDir, "config", "--get", "core.repositoryformatversion");
        if (verExit != 0 || verOutput.Trim() != "1")
            Git.Run(gitDir, "config", "core.repositoryformatversion", "1");

        var (extExit, extOutput, _) = Git.Run(gitDir, "config", "--get", "extensions.relativeWorktrees");
        if (extExit != 0 || !extOutput.Trim().Equals("true", StringComparison.OrdinalIgnoreCase))
            Git.Run(gitDir, "config", "extensions.relativeWorktrees", "true");
    }
}
