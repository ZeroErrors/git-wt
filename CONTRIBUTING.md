# Contributing

Thanks for your interest in contributing to git-wt!

## Getting Started

1. Fork the repository
2. Clone your fork and create a branch for your change
3. Make your changes
4. Open a pull request against `main`

## Building

Requires [.NET 10](https://dotnet.microsoft.com/download/dotnet/10.0) or later.

```
dotnet build
dotnet test
```

To install locally as a global tool:

```bash
# Linux / macOS
./install-local.sh

# Windows (PowerShell)
./install-local.ps1
```

## Guidelines

- Keep changes focused, one feature or fix per PR
- Follow the existing code style
- Test your changes against a real bare worktree setup before submitting

## Reporting Issues

Open an issue on GitHub with steps to reproduce the problem and the output you see.
