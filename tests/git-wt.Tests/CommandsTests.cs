using Xunit;

public class CommandsTests
{
    [Theory]
    [InlineData("https://github.com/user/my-project.git", "my-project")]
    [InlineData("https://github.com/user/my-project", "my-project")]
    [InlineData("git@github.com:user/my-project.git", "my-project")]
    [InlineData("git@github.com:user/my-project", "my-project")]
    [InlineData("/home/user/repos/my-project", "my-project")]
    [InlineData("C:\\repos\\my-project", "my-project")]
    [InlineData("https://github.com/user/my-project.git/", "my-project")]
    [InlineData("my-project.git", "my-project")]
    [InlineData("my-project", "my-project")]
    public void DeriveRepoName_ExtractsName(string url, string expected)
    {
        Assert.Equal(expected, Commands.DeriveRepoName(url));
    }

    [Theory]
    [InlineData("")]
    [InlineData("/")]
    [InlineData(".git")]
    public void DeriveRepoName_ReturnsNullForInvalidInput(string url)
    {
        Assert.Null(Commands.DeriveRepoName(url));
    }

    [Theory]
    [InlineData("/repo", "/repo/main", false)]
    [InlineData("/repo", "/repo/feat/auth", false)]
    [InlineData("/repo", "/repo", false)]
    [InlineData("/repo", "/private/tmp/ht-stage", true)]
    [InlineData("/repo/nested", "/repo/other", true)]
    public void IsOutsideRepo_DetectsEscapingPaths(string repoRoot, string path, bool expected)
    {
        Assert.Equal(expected, Commands.IsOutsideRepo(repoRoot, path));
    }

    [Theory]
    [InlineData("/home/user/repos/proj", "/home/user", "~/repos/proj")]
    [InlineData("/home/user", "/home/user", "~")]
    [InlineData("/private/tmp/ht-stage", "/home/user", "/private/tmp/ht-stage")]
    [InlineData("/home/username/x", "/home/user", "/home/username/x")]
    public void AbbreviateHome_ReplacesHomePrefix(string path, string home, string expected)
    {
        Assert.Equal(expected, Commands.AbbreviateHome(path, home));
    }
}
