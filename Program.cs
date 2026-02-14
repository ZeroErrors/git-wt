using System.CommandLine;
using System.Diagnostics;
using System.Text;

var branchArg = new Argument<string>("branch-name")
{
    Description = "Name of the branch/worktree to create",
    Arity = ArgumentArity.ZeroOrOne
};

var listOption = new Option<bool>("--list", "-l")
{
    Description = "List all worktrees with branch and upstream info"
};

var pruneOption = new Option<bool>("--prune", "-p")
{
    Description = "Remove worktrees whose upstream branch is gone and delete empty parent directories"
};

var rootCommand = new RootCommand("Creates a worktree for the given branch, automatically tracking an existing remote branch or creating a new local branch.")
{
    branchArg,
    listOption,
    pruneOption
};

rootCommand.SetAction(parseResult =>
{
    if (parseResult.GetValue(listOption))
        return ListWorktrees();

    if (parseResult.GetValue(pruneOption))
        return PruneWorktrees();

    var branchName = parseResult.GetValue(branchArg);
    if (string.IsNullOrEmpty(branchName))
    {
        Console.Error.WriteLine("Error: branch name is required. Use -h for help.");
        return 1;
    }
    return CreateWorktree(branchName);
});

return rootCommand.Parse(args).Invoke();

// ── Create worktree ──────────────────────────────────────────────────

static int CreateWorktree(string branchName)
{
    if (!IsValidBranchName(branchName))
    {
        Console.Error.WriteLine($"Error: Invalid branch name '{branchName}'");
        return 1;
    }

    var bareRepoPath = FindBareRepo(Directory.GetCurrentDirectory());
    if (bareRepoPath == null)
    {
        Console.Error.WriteLine("Error: Could not find .bare directory.");
        Console.Error.WriteLine("This tool must be run from within a bare repo worktree setup.");
        return 1;
    }

    var repoRoot = Path.GetDirectoryName(bareRepoPath)!;
    var worktreePath = Path.Combine(repoRoot, branchName);

    if (Directory.Exists(worktreePath))
    {
        Console.Error.WriteLine($"Error: Directory already exists: {worktreePath}");
        return 1;
    }

    // Fetch latest from remote
    Console.WriteLine("Fetching from origin...");
    RunGitLive(bareRepoPath, "fetch", "--progress", "origin");

    // Check if local branch already exists
    var (localExit, _, _) = RunGit(bareRepoPath, "rev-parse", "--verify", $"refs/heads/{branchName}");
    var localBranchExists = localExit == 0;

    // Check if branch exists on remote (using local refs)
    var (remoteExit, _, _) = RunGit(bareRepoPath, "rev-parse", "--verify", $"refs/remotes/origin/{branchName}");
    var remoteBranchExists = remoteExit == 0;

    // Temporarily enable extensions.relativeWorktrees and worktree.useRelativePaths
    // during worktree creation, then disable them afterwards.
    var (relExtExit, relExtOutput, _) = RunGit(bareRepoPath, "config", "--get", "extensions.relativeWorktrees");
    var relExtWasEnabled = relExtExit == 0 && relExtOutput.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);

    var (relPathExit, relPathOutput, _) = RunGit(bareRepoPath, "config", "--get", "worktree.useRelativePaths");
    var relPathWasEnabled = relPathExit == 0 && relPathOutput.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);

    if (!relExtWasEnabled)
        RunGit(bareRepoPath, "config", "extensions.relativeWorktrees", "true");
    if (!relPathWasEnabled)
        RunGit(bareRepoPath, "config", "worktree.useRelativePaths", "true");

    // Create the worktree
    int exitCode;
    try
    {
        if (localBranchExists)
        {
            Console.WriteLine($"Using existing branch '{branchName}'...");
            exitCode = RunGitLive(bareRepoPath, "worktree", "add", worktreePath, branchName);
        }
        else if (remoteBranchExists)
        {
            Console.WriteLine($"Tracking remote branch 'origin/{branchName}'...");
            exitCode = RunGitLive(bareRepoPath, "worktree", "add", "--track", "-b", branchName, worktreePath, $"origin/{branchName}");
        }
        else
        {
            var baseBranch = GetDefaultBranch(bareRepoPath);
            Console.WriteLine($"Creating new branch '{branchName}' from '{baseBranch}'...");
            exitCode = RunGitLive(bareRepoPath, "worktree", "add", "--no-track", "-b", branchName, worktreePath, baseBranch);
        }
    }
    finally
    {
        if (!relExtWasEnabled)
            RunGit(bareRepoPath, "config", "--unset", "extensions.relativeWorktrees");
        if (!relPathWasEnabled)
            RunGit(bareRepoPath, "config", "--unset", "worktree.useRelativePaths");
    }

    if (exitCode == 0)
        Console.WriteLine($"Created worktree at: {worktreePath}");

    return exitCode;
}

// ── List worktrees ───────────────────────────────────────────────────

static int ListWorktrees()
{
    Console.OutputEncoding = Encoding.UTF8;

    var bareRepoPath = FindBareRepo(Directory.GetCurrentDirectory());
    if (bareRepoPath == null)
    {
        Console.Error.WriteLine("Error: Could not find .bare directory.");
        Console.Error.WriteLine("This tool must be run from within a bare repo worktree setup.");
        return 1;
    }

    var repoRoot = Path.GetDirectoryName(bareRepoPath)!;

    // Get worktree list
    var (wtExit, wtOutput, _) = RunGit(bareRepoPath, "worktree", "list", "--porcelain");
    if (wtExit != 0)
    {
        Console.Error.WriteLine("Error: Failed to list worktrees.");
        return 1;
    }

    var worktrees = ParseWorktreeList(wtOutput);
    if (worktrees.Count == 0)
    {
        Console.WriteLine("No worktrees found.");
        return 0;
    }

    // Get upstream info
    var (refExit, refOutput, _) = RunGit(bareRepoPath,
        "for-each-ref", "--format=%(refname:short)\t%(upstream:short)\t%(upstream:track)", "refs/heads/");
    var upstreamMap = refExit == 0 ? ParseUpstreamInfo(refOutput) : new Dictionary<string, (string? Upstream, bool IsGone)>();

    // Build branch info list (filter out bare entry)
    var branches = new List<BranchInfo>();
    foreach (var wt in worktrees)
    {
        if (wt.IsBare) continue;

        if (wt.IsDetached)
        {
            // Use directory name relative to repo root for detached HEADs
            var dirName = Path.GetRelativePath(repoRoot, wt.Path).Replace('\\', '/');
            branches.Add(new BranchInfo(dirName, null, false, true));
        }
        else if (wt.Branch is not null)
        {
            var branch = wt.Branch;
            upstreamMap.TryGetValue(branch, out var info);
            branches.Add(new BranchInfo(branch, info.Upstream, info.IsGone, false));
        }
    }

    if (branches.Count == 0)
    {
        Console.WriteLine("No worktrees found.");
        return 0;
    }

    // Build and print tree
    var root = BuildTree(branches);
    Console.WriteLine(repoRoot.Replace('\\', '/'));
    PrintTree(root, "", true);
    return 0;
}

// ── Prune worktrees ──────────────────────────────────────────────────

static int PruneWorktrees()
{
    var bareRepoPath = FindBareRepo(Directory.GetCurrentDirectory());
    if (bareRepoPath == null)
    {
        Console.Error.WriteLine("Error: Could not find .bare directory.");
        Console.Error.WriteLine("This tool must be run from within a bare repo worktree setup.");
        return 1;
    }

    var repoRoot = Path.GetDirectoryName(bareRepoPath)!;

    // Fetch to get current remote state
    Console.WriteLine("Fetching from origin...");
    RunGitLive(bareRepoPath, "fetch", "--progress", "--prune", "origin");

    // Get worktree list
    var (wtExit, wtOutput, _) = RunGit(bareRepoPath, "worktree", "list", "--porcelain");
    if (wtExit != 0)
    {
        Console.Error.WriteLine("Error: Failed to list worktrees.");
        return 1;
    }

    var worktrees = ParseWorktreeList(wtOutput);

    // Get upstream info
    var (refExit, refOutput, _) = RunGit(bareRepoPath,
        "for-each-ref", "--format=%(refname:short)\t%(upstream:short)\t%(upstream:track)", "refs/heads/");
    if (refExit != 0)
    {
        Console.Error.WriteLine("Error: Failed to get branch upstream info.");
        return 1;
    }
    var upstreamMap = ParseUpstreamInfo(refOutput);

    // Find gone worktrees
    var goneBranches = new List<(string Path, string Branch)>();
    foreach (var wt in worktrees)
    {
        if (wt.IsBare || wt.IsDetached || wt.Branch is null) continue;
        if (upstreamMap.TryGetValue(wt.Branch, out var info) && info.IsGone)
            goneBranches.Add((wt.Path, wt.Branch));
    }

    if (goneBranches.Count == 0)
    {
        Console.WriteLine("No gone worktrees to prune.");
        return 0;
    }

    int failed = 0;
    foreach (var (wtPath, branch) in goneBranches)
    {
        Console.WriteLine($"Removing worktree '{branch}'...");
        var exit = RunGitLive(bareRepoPath, "worktree", "remove", wtPath);
        if (exit != 0)
        {
            Console.Error.WriteLine($"  Skipped: worktree has dirty or untracked changes.");
            failed++;
            continue;
        }

        // Delete the local branch now that the worktree is gone
        RunGit(bareRepoPath, "branch", "-d", branch);

        // Clean up empty parent directories up to (but not including) repoRoot
        var parent = Path.GetDirectoryName(wtPath);
        while (parent != null
            && !string.Equals(Path.GetFullPath(parent), Path.GetFullPath(repoRoot), StringComparison.OrdinalIgnoreCase)
            && Directory.Exists(parent)
            && Directory.GetFileSystemEntries(parent).Length == 0)
        {
            Console.WriteLine($"  Removing empty directory: {Path.GetRelativePath(repoRoot, parent).Replace('\\', '/')}");
            Directory.Delete(parent);
            parent = Path.GetDirectoryName(parent);
        }
    }

    var removed = goneBranches.Count - failed;
    Console.WriteLine($"Pruned {removed} worktree{(removed == 1 ? "" : "s")}{(failed > 0 ? $", {failed} skipped (dirty)" : "")}.");
    return 0;
}

static List<WorktreeEntry> ParseWorktreeList(string output)
{
    var entries = new List<WorktreeEntry>();
    string? path = null;
    bool isBare = false, isDetached = false;
    string? branch = null;

    foreach (var line in output.Split('\n'))
    {
        var trimmed = line.TrimEnd('\r');
        if (trimmed.StartsWith("worktree "))
        {
            path = trimmed["worktree ".Length..];
            isBare = false;
            isDetached = false;
            branch = null;
        }
        else if (trimmed == "bare")
        {
            isBare = true;
        }
        else if (trimmed == "detached")
        {
            isDetached = true;
        }
        else if (trimmed.StartsWith("branch "))
        {
            // e.g., "branch refs/heads/main" → "main"
            var refName = trimmed["branch ".Length..];
            if (refName.StartsWith("refs/heads/"))
                branch = refName["refs/heads/".Length..];
            else
                branch = refName;
        }
        else if (trimmed == "" && path != null)
        {
            entries.Add(new WorktreeEntry(path, branch, isDetached, isBare));
            path = null;
        }
    }

    // Handle last entry if no trailing blank line
    if (path != null)
        entries.Add(new WorktreeEntry(path, branch, isDetached, isBare));

    return entries;
}

static Dictionary<string, (string? Upstream, bool IsGone)> ParseUpstreamInfo(string output)
{
    var map = new Dictionary<string, (string? Upstream, bool IsGone)>();
    foreach (var line in output.Split('\n'))
    {
        var trimmed = line.TrimEnd('\r');
        if (string.IsNullOrEmpty(trimmed)) continue;

        var parts = trimmed.Split('\t');
        if (parts.Length < 1) continue;

        var branch = parts[0];
        var upstream = parts.Length > 1 && !string.IsNullOrEmpty(parts[1]) ? parts[1] : null;
        var track = parts.Length > 2 ? parts[2] : "";
        var isGone = track.Contains("[gone]");

        map[branch] = (upstream, isGone);
    }
    return map;
}

static TreeNode BuildTree(List<BranchInfo> branches)
{
    var root = new TreeNode();
    foreach (var branch in branches)
    {
        var segments = branch.FullBranch.Split('/');
        var current = root;
        for (int i = 0; i < segments.Length; i++)
        {
            var seg = segments[i];
            if (!current.Children.TryGetValue(seg, out var child))
            {
                child = new TreeNode();
                current.Children[seg] = child;
            }
            current = child;

            if (i == segments.Length - 1)
                current.Info = branch;
        }
    }
    return root;
}

static void PrintTree(TreeNode node, string prefix, bool isRoot)
{
    var entries = node.Children.ToList();
    for (int i = 0; i < entries.Count; i++)
    {
        var (name, child) = entries[i];
        var isLast = i == entries.Count - 1;

        var connector = isRoot ? (isLast ? "└── " : "├── ") : (isLast ? "└── " : "├── ");
        var label = name;

        // Append status annotation
        if (child.Info is not null)
        {
            if (child.Info.IsDetached)
                label += " (detached)";
            else if (child.Info.IsGone)
                label += " [gone]";
            else if (child.Info.Upstream is null)
                label += " (no upstream)";
        }

        Console.Write(prefix);
        Console.Write(connector);
        Console.WriteLine(label);

        var extension = isLast ? "    " : "│   ";
        PrintTree(child, prefix + extension, false);
    }
}

// ── Shared helpers ───────────────────────────────────────────────────

static bool IsValidBranchName(string name)
{
    using var p = Process.Start(new ProcessStartInfo("git", ["check-ref-format", "--branch", name])
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true
    })!;
    p.WaitForExit();
    return p.ExitCode == 0;
}

static string? FindBareRepo(string path)
{
    for (var d = path; d != null; d = Path.GetDirectoryName(d))
    {
        var bare = Path.Combine(d, ".bare");
        if (Directory.Exists(bare)) return bare;
    }
    return null;
}

static string GetDefaultBranch(string gitDir)
{
    var (exit, output, _) = RunGit(gitDir, "symbolic-ref", "refs/remotes/origin/HEAD", "--short");
    if (exit == 0 && !string.IsNullOrWhiteSpace(output))
        return output.Trim();

    if (RunGit(gitDir, "rev-parse", "--verify", "origin/main").ExitCode == 0)
        return "origin/main";
    if (RunGit(gitDir, "rev-parse", "--verify", "origin/master").ExitCode == 0)
        return "origin/master";

    return "HEAD";
}

static (int ExitCode, string Output, string Error) RunGit(string gitDir, params string[] args)
{
    var psi = new ProcessStartInfo("git")
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true
    };
    psi.ArgumentList.Add($"--git-dir={gitDir}");
    foreach (var arg in args) psi.ArgumentList.Add(arg);

    using var p = Process.Start(psi)!;
    var output = p.StandardOutput.ReadToEnd();
    var error = p.StandardError.ReadToEnd();
    p.WaitForExit();
    return (p.ExitCode, output, error);
}

static int RunGitLive(string gitDir, params string[] args)
{
    var psi = new ProcessStartInfo("git") { UseShellExecute = false };
    psi.ArgumentList.Add($"--git-dir={gitDir}");
    foreach (var arg in args) psi.ArgumentList.Add(arg);

    using var p = Process.Start(psi)!;
    p.WaitForExit();
    return p.ExitCode;
}

// ── Types ────────────────────────────────────────────────────────────

record WorktreeEntry(string Path, string? Branch, bool IsDetached, bool IsBare);

record BranchInfo(string FullBranch, string? Upstream, bool IsGone, bool IsDetached);

class TreeNode
{
    public SortedDictionary<string, TreeNode> Children { get; } = new(StringComparer.OrdinalIgnoreCase);
    public BranchInfo? Info { get; set; }
}
