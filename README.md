# git-wt

[![CI](https://github.com/ZeroErrors/git-wt/actions/workflows/ci.yml/badge.svg)](https://github.com/ZeroErrors/git-wt/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/git-wt)](https://www.nuget.org/packages/git-wt)

A CLI tool for managing [bare repo worktree](https://git-scm.com/docs/git-worktree) setups. Creates worktrees with automatic remote branch tracking, lists them as a hierarchical tree, and prunes stale ones.

## Install

Requires [.NET 10](https://dotnet.microsoft.com/download/dotnet/10.0) or later.

```
dotnet tool install -g git-wt
```

## Usage

```
git wt --setup <url>    Clone a repo into a bare worktree layout
git wt <branch-name>    Create a worktree for the given branch
git wt --list           List all worktrees as a tree
git wt --prune          Remove worktrees whose upstream is gone
```

### Set up a new repository

```
git wt --setup git@github.com:user/my-project.git
```

Clones the repository into a bare worktree layout, ready for use with `git wt`:

```
my-project/
├── .bare/       (bare git repository)
└── main/        (worktree for the default branch)
```

This is a drop-in replacement for `git clone` that produces a worktree-based structure. All remote branches are fetched and the default branch (e.g. `main` or `master`) is checked out as the first worktree. Works with both SSH and HTTPS URLs.

### Create a worktree

```
git wt feat/my-feature
```

Automatically detects whether the branch exists locally, on the remote, or needs to be created:
- **Local branch exists**:checks it out in a new worktree
- **Remote branch exists**:creates a local branch tracking the remote
- **Neither**:creates a new branch from the default branch

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
