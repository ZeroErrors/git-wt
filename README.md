# git-wt

A CLI tool for managing [bare repo worktree](https://git-scm.com/docs/git-worktree) setups. Creates worktrees with automatic remote branch tracking, lists them as a hierarchical tree, and prunes stale ones.

## Install

Requires [.NET 10](https://dotnet.microsoft.com/download/dotnet/10.0) or later.

```
dotnet tool install -g git-wt
```

## Usage

```
git wt <branch-name>    Create a worktree for the given branch
git wt --list           List all worktrees as a tree
git wt --prune          Remove worktrees whose upstream is gone
```

### Create a worktree

```
git wt feat/my-feature
```

Automatically detects whether the branch exists locally, on the remote, or needs to be created:
- **Local branch exists** — checks it out in a new worktree
- **Remote branch exists** — creates a local branch tracking the remote
- **Neither** — creates a new branch from the default branch

### List worktrees

```
git wt --list
```

Displays worktrees as a hierarchical tree grouped by branch path segments:

```
D:/github/my-project
├── feat
│   ├── auth-system
│   └── zero
│       ├── assets-v3
│       └── native-libs [gone]
├── main
└── release (no upstream)
```

Annotations: `[gone]` (upstream deleted), `(no upstream)` (no tracking configured), `(detached)` (detached HEAD). Normal tracking is silent.

### Prune gone worktrees

```
git wt --prune
```

Fetches from origin, then removes worktrees whose upstream branch has been deleted. Safely skips worktrees with dirty or untracked changes. Cleans up empty parent directories after removal.

## License

[MIT](LICENSE)
