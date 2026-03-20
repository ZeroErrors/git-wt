using System.Text;

/// <summary>
/// Implements the four top-level commands: create, setup, list, and prune.
/// </summary>
static class Commands
{
    /// <summary>
    /// Creates a worktree for the given branch. Tracks a remote branch if one exists,
    /// otherwise creates a new local branch from the default branch.
    /// </summary>
    public static int Create(string branchName)
    {
        if (!Git.IsValidBranchName(branchName))
        {
            Console.Error.WriteLine($"Error: Invalid branch name '{branchName}'");
            return 1;
        }

        if (!TryFindBareRepo(out var bareRepoPath, out var repoRoot))
            return 1;

        var worktreePath = Path.Combine(repoRoot, branchName);
        if (Directory.Exists(worktreePath))
        {
            Console.Error.WriteLine($"Error: Directory already exists: {worktreePath}");
            return 1;
        }

        Console.WriteLine("Fetching from origin...");
        if (Git.RunLive(bareRepoPath, "fetch", "--progress", "origin") != 0)
            Console.Error.WriteLine("Warning: Fetch failed, using cached branch data.");

        var localBranchExists = Git.Run(bareRepoPath, "rev-parse", "--verify", $"refs/heads/{branchName}").ExitCode == 0;
        var remoteBranchExists = Git.Run(bareRepoPath, "rev-parse", "--verify", $"refs/remotes/origin/{branchName}").ExitCode == 0;

        int exitCode;
        using (new RelativeWorktreeScope(bareRepoPath))
        {
            if (localBranchExists)
            {
                Console.WriteLine($"Using existing branch '{branchName}'...");
                exitCode = Git.RunLive(bareRepoPath, "worktree", "add", worktreePath, branchName);
            }
            else if (remoteBranchExists)
            {
                Console.WriteLine($"Tracking remote branch 'origin/{branchName}'...");
                exitCode = Git.RunLive(bareRepoPath, "worktree", "add", "--track", "-b", branchName, worktreePath, $"origin/{branchName}");
            }
            else
            {
                var baseBranch = Git.GetDefaultBranch(bareRepoPath);
                if (baseBranch is null)
                {
                    Console.Error.WriteLine("Error: Could not determine default branch.");
                    Console.Error.WriteLine("Set origin/HEAD with: git remote set-head origin --auto");
                    return 1;
                }
                Console.WriteLine($"Creating new branch '{branchName}' from '{baseBranch}'...");
                exitCode = Git.RunLive(bareRepoPath, "worktree", "add", "--no-track", "-b", branchName, worktreePath, baseBranch);
            }
        }

        if (exitCode == 0)
            Console.WriteLine($"Created worktree at: {worktreePath}");

        return exitCode;
    }

    /// <summary>
    /// Clones a remote repository into a bare worktree layout:
    /// <c>&lt;name&gt;/.bare</c> (bare repo) + <c>&lt;name&gt;/&lt;default-branch&gt;</c> (worktree).
    /// </summary>
    public static int Setup(string remoteUrl)
    {
        var name = DeriveRepoName(remoteUrl);
        if (name is null)
        {
            Console.Error.WriteLine("Error: Could not derive directory name from URL.");
            return 1;
        }

        var targetDir = Path.Combine(Directory.GetCurrentDirectory(), name);
        if (Directory.Exists(targetDir))
        {
            Console.Error.WriteLine($"Error: Directory already exists: {targetDir}");
            return 1;
        }

        var bareDir = Path.Combine(targetDir, ".bare");

        Console.WriteLine($"Cloning into '{name}/.bare'...");
        if (Git.RunPlainLive("clone", "--bare", remoteUrl, bareDir) != 0)
        {
            Console.Error.WriteLine("Error: Clone failed.");
            TryDeleteDirectory(targetDir);
            return 1;
        }

        // Bare clones only fetch the default branch. Fix the refspec so all branches are visible.
        Git.Run(bareDir, "config", "remote.origin.fetch", "+refs/heads/*:refs/remotes/origin/*");

        Console.WriteLine("Fetching all branches...");
        if (Git.RunLive(bareDir, "fetch", "--progress", "origin") != 0)
            Console.Error.WriteLine("Warning: Fetch failed. Continuing with branches from initial clone.");

        var defaultRef = Git.GetDefaultBranch(bareDir);
        if (defaultRef is null)
        {
            Console.Error.WriteLine("Error: Could not determine default branch.");
            Console.Error.WriteLine("Set origin/HEAD with: git remote set-head origin --auto");
            TryDeleteDirectory(targetDir);
            return 1;
        }

        var defaultBranch = defaultRef.StartsWith("origin/")
            ? defaultRef["origin/".Length..]
            : defaultRef;

        var worktreePath = Path.Combine(targetDir, defaultBranch);
        Console.WriteLine($"Creating worktree for '{defaultBranch}'...");

        int wtExit;
        using (new RelativeWorktreeScope(bareDir))
        {
            wtExit = Git.RunLive(bareDir, "worktree", "add", "--track", "-b", defaultBranch, worktreePath, $"origin/{defaultBranch}");
        }

        if (wtExit != 0)
        {
            Console.Error.WriteLine("Error: Failed to create worktree.");
            TryDeleteDirectory(targetDir);
            return 1;
        }

        Console.WriteLine($"Setup complete: {targetDir}");
        return 0;
    }

    /// <summary>
    /// Lists all worktrees as a hierarchical tree with branch and upstream status.
    /// </summary>
    public static int List()
    {
        Console.OutputEncoding = Encoding.UTF8;

        if (!TryFindBareRepo(out var bareRepoPath, out var repoRoot))
            return 1;

        var (wtExit, wtOutput, _) = Git.Run(bareRepoPath, "worktree", "list", "--porcelain");
        if (wtExit != 0)
        {
            Console.Error.WriteLine("Error: Failed to list worktrees.");
            return 1;
        }

        var worktrees = Parsing.ParseWorktreeList(wtOutput);
        if (worktrees.Count == 0)
        {
            Console.WriteLine("No worktrees found.");
            return 0;
        }

        var (refExit, refOutput, _) = Git.Run(bareRepoPath,
            "for-each-ref", "--format=%(refname:short)\t%(upstream:short)\t%(upstream:track)", "refs/heads/");
        var upstreamMap = refExit == 0
            ? Parsing.ParseUpstreamInfo(refOutput)
            : new Dictionary<string, UpstreamInfo>();

        var branches = new List<BranchInfo>();
        foreach (var wt in worktrees)
        {
            if (wt.IsBare) continue;

            if (wt.IsDetached)
            {
                var dirName = Path.GetRelativePath(repoRoot, wt.Path).Replace('\\', '/');
                branches.Add(new BranchInfo(dirName, null, false, true));
            }
            else if (wt.Branch is not null)
            {
                upstreamMap.TryGetValue(wt.Branch, out var info);
                branches.Add(new BranchInfo(wt.Branch, info?.Upstream, info?.IsGone ?? false, false));
            }
        }

        if (branches.Count == 0)
        {
            Console.WriteLine("No worktrees found.");
            return 0;
        }

        var root = Parsing.BuildTree(branches);
        Console.WriteLine(repoRoot.Replace('\\', '/'));
        Parsing.PrintTree(root, "");
        return 0;
    }

    /// <summary>
    /// Removes worktrees whose upstream branch has been deleted (gone),
    /// then cleans up empty parent directories.
    /// </summary>
    public static int Prune()
    {
        if (!TryFindBareRepo(out var bareRepoPath, out var repoRoot))
            return 1;

        Console.WriteLine("Fetching from origin...");
        if (Git.RunLive(bareRepoPath, "fetch", "--progress", "--prune", "origin") != 0)
            Console.Error.WriteLine("Warning: Fetch failed, using cached branch data.");

        var (wtExit, wtOutput, _) = Git.Run(bareRepoPath, "worktree", "list", "--porcelain");
        if (wtExit != 0)
        {
            Console.Error.WriteLine("Error: Failed to list worktrees.");
            return 1;
        }

        var worktrees = Parsing.ParseWorktreeList(wtOutput);

        var (refExit, refOutput, _) = Git.Run(bareRepoPath,
            "for-each-ref", "--format=%(refname:short)\t%(upstream:short)\t%(upstream:track)", "refs/heads/");
        if (refExit != 0)
        {
            Console.Error.WriteLine("Error: Failed to get branch upstream info.");
            return 1;
        }
        var upstreamMap = Parsing.ParseUpstreamInfo(refOutput);

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
            if (Git.RunLive(bareRepoPath, "worktree", "remove", wtPath) != 0)
            {
                Console.Error.WriteLine($"  Skipped: worktree has dirty or untracked changes.");
                failed++;
                continue;
            }

            var (delExit, _, _) = Git.Run(bareRepoPath, "branch", "-d", branch);
            if (delExit != 0)
                Console.Error.WriteLine($"  Warning: Could not delete branch '{branch}'. Remove manually with: git branch -D {branch}");

            RemoveEmptyParentDirectories(wtPath, repoRoot);
        }

        var removed = goneBranches.Count - failed;
        Console.WriteLine($"Pruned {removed} worktree{(removed == 1 ? "" : "s")}{(failed > 0 ? $", {failed} skipped (dirty)" : "")}.");
        return 0;
    }

    // ── Helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Locates the <c>.bare</c> directory by walking up from the current directory.
    /// Prints an error and returns false if not found.
    /// </summary>
    static bool TryFindBareRepo(out string bareRepoPath, out string repoRoot)
    {
        for (var d = Directory.GetCurrentDirectory(); d != null; d = Path.GetDirectoryName(d))
        {
            var bare = Path.Combine(d, ".bare");
            if (Directory.Exists(bare))
            {
                bareRepoPath = bare;
                repoRoot = d;
                return true;
            }
        }

        Console.Error.WriteLine("Error: Could not find .bare directory.");
        Console.Error.WriteLine("This tool must be run from within a bare repo worktree setup.");
        bareRepoPath = "";
        repoRoot = "";
        return false;
    }

    /// <summary>
    /// Extracts the repository name from a remote URL by stripping the trailing <c>.git</c>
    /// suffix and taking the last path segment.
    /// Works with HTTPS, SSH, and local file paths.
    /// </summary>
    internal static string? DeriveRepoName(string url)
    {
        var name = url.TrimEnd('/');
        if (name.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            name = name[..^4];

        // Split on the last separator: '/' for URLs, '\' for Windows paths, ':' for SSH (git@host:repo).
        var lastSep = Math.Max(name.LastIndexOf('/'), Math.Max(name.LastIndexOf('\\'), name.LastIndexOf(':')));
        name = name[(lastSep + 1)..];

        return string.IsNullOrEmpty(name) ? null : name;
    }

    /// <summary>
    /// Deletes empty parent directories up to (but not including) the repo root.
    /// </summary>
    static void RemoveEmptyParentDirectories(string startPath, string repoRoot)
    {
        var parent = Path.GetDirectoryName(startPath);
        while (parent != null
            && !string.Equals(Path.GetFullPath(parent), Path.GetFullPath(repoRoot), StringComparison.OrdinalIgnoreCase)
            && Directory.Exists(parent)
            && Directory.GetFileSystemEntries(parent).Length == 0)
        {
            Console.WriteLine($"  Removing empty directory: {Path.GetRelativePath(repoRoot, parent).Replace('\\', '/')}");
            try { Directory.Delete(parent); }
            catch (IOException) { break; }
            catch (UnauthorizedAccessException) { break; }
            parent = Path.GetDirectoryName(parent);
        }
    }

    /// <summary>
    /// Attempts to delete a directory and its contents; failures are silently ignored.
    /// </summary>
    static void TryDeleteDirectory(string path)
    {
        if (!Directory.Exists(path)) return;
        try { Directory.Delete(path, true); }
        catch { /* best-effort cleanup */ }
    }
}
