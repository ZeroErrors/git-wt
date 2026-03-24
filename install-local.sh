#!/bin/bash
set -e

# Uninstall existing version (ignore errors if not installed)
dotnet tool uninstall -g git-wt 2>/dev/null || true

# Pack and install
dotnet pack src/git-wt -o ./artifacts
dotnet tool install -g --add-source ./artifacts --version "0.0.0-local" git-wt
