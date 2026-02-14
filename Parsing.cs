/// <summary>
/// Parses git porcelain output and builds tree structures for display.
/// </summary>
static class Parsing
{
    /// <summary>
    /// Parses the porcelain output of <c>git worktree list --porcelain</c> into structured entries.
    /// </summary>
    public static List<WorktreeEntry> ParseWorktreeList(string output)
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
                var refName = trimmed["branch ".Length..];
                branch = refName.StartsWith("refs/heads/")
                    ? refName["refs/heads/".Length..]
                    : refName;
            }
            else if (trimmed == "" && path != null)
            {
                entries.Add(new WorktreeEntry(path, branch, isDetached, isBare));
                path = null;
            }
        }

        // Handle last entry if output has no trailing blank line
        if (path != null)
            entries.Add(new WorktreeEntry(path, branch, isDetached, isBare));

        return entries;
    }

    /// <summary>
    /// Parses <c>git for-each-ref</c> output (tab-separated: branch, upstream, track)
    /// into a map of branch name to upstream info.
    /// </summary>
    public static Dictionary<string, UpstreamInfo> ParseUpstreamInfo(string output)
    {
        var map = new Dictionary<string, UpstreamInfo>();
        foreach (var line in output.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r');
            if (string.IsNullOrEmpty(trimmed)) continue;

            var parts = trimmed.Split('\t');
            var branch = parts[0];
            var upstream = parts.Length > 1 && !string.IsNullOrEmpty(parts[1]) ? parts[1] : null;
            var track = parts.Length > 2 ? parts[2] : "";
            var isGone = track.Contains("[gone]");

            map[branch] = new UpstreamInfo(upstream, isGone);
        }
        return map;
    }

    /// <summary>
    /// Builds a tree of <see cref="TreeNode"/> from a flat list of branches,
    /// splitting on <c>/</c> separators (e.g. <c>feat/foo</c> becomes two levels).
    /// </summary>
    public static TreeNode BuildTree(List<BranchInfo> branches)
    {
        var root = new TreeNode();
        foreach (var branch in branches)
        {
            var segments = branch.Name.Split('/');
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

    /// <summary>
    /// Recursively prints a worktree tree using box-drawing characters.
    /// </summary>
    public static void PrintTree(TreeNode node, string prefix)
    {
        var entries = node.Children.ToList();
        for (int i = 0; i < entries.Count; i++)
        {
            var (name, child) = entries[i];
            var isLast = i == entries.Count - 1;

            var connector = isLast ? "└── " : "├── ";
            var label = name;

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
            PrintTree(child, prefix + extension);
        }
    }
}

// ── Types ────────────────────────────────────────────────────────

/// <summary>
/// A single entry from <c>git worktree list --porcelain</c>.
/// </summary>
record WorktreeEntry(string Path, string? Branch, bool IsDetached, bool IsBare);

/// <summary>
/// Display info for a worktree branch. <see cref="Name"/> is the branch name,
/// or a relative directory path for detached worktrees.
/// </summary>
record BranchInfo(string Name, string? Upstream, bool IsGone, bool IsDetached);

/// <summary>
/// Upstream tracking state for a local branch.
/// </summary>
record UpstreamInfo(string? Upstream, bool IsGone);

/// <summary>
/// A node in the hierarchical worktree display tree, keyed by path segment.
/// </summary>
class TreeNode
{
    public SortedDictionary<string, TreeNode> Children { get; } = new(StringComparer.OrdinalIgnoreCase);
    public BranchInfo? Info { get; set; }
}
