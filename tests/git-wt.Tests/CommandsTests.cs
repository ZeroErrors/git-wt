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
}
