# Uninstall existing version (ignore errors if not installed)
dotnet tool uninstall -g git-wt 2>$null

# Pack and install
dotnet pack
dotnet tool install -g --add-source ./nupkg git-wt
