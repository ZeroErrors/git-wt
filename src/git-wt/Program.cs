using System.CommandLine;

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

var removeOption = new Option<string>("--remove", "-r")
{
    Description = "Remove a worktree and its local branch"
};

var setupOption = new Option<string>("--setup", "-s")
{
    Description = "Clone a repository into a bare worktree layout: <name>/.bare + default branch worktree"
};

var rootCommand = new RootCommand("Creates a worktree for the given branch, automatically tracking an existing remote branch or creating a new local branch.")
{
    branchArg,
    listOption,
    pruneOption,
    removeOption,
    setupOption
};

rootCommand.SetAction(parseResult =>
{
    var setupUrl = parseResult.GetValue(setupOption);
    if (!string.IsNullOrEmpty(setupUrl))
        return Commands.Setup(setupUrl);

    if (parseResult.GetValue(listOption))
        return Commands.List();

    if (parseResult.GetValue(pruneOption))
        return Commands.Prune();

    var removeBranch = parseResult.GetValue(removeOption);
    if (!string.IsNullOrEmpty(removeBranch))
        return Commands.Remove(removeBranch);

    var branchName = parseResult.GetValue(branchArg);
    if (string.IsNullOrEmpty(branchName))
    {
        Console.Error.WriteLine("Error: branch name is required. Use -h for help.");
        return 1;
    }
    return Commands.Create(branchName);
});

return rootCommand.Parse(args).Invoke();
