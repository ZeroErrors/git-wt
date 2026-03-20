using Xunit;

public class ParsingTests
{
    [Fact]
    public void ParseWorktreeList_ParsesBasicEntries()
    {
        var output = "worktree /repo/.bare\nbare\n\nworktree /repo/main\nbranch refs/heads/main\n\nworktree /repo/feat/thing\nbranch refs/heads/feat/thing\n\n";

        var entries = Parsing.ParseWorktreeList(output);

        Assert.Equal(3, entries.Count);
        Assert.True(entries[0].IsBare);
        Assert.Equal("main", entries[1].Branch);
        Assert.False(entries[1].IsDetached);
        Assert.Equal("feat/thing", entries[2].Branch);
    }

    [Fact]
    public void ParseWorktreeList_ParsesDetachedHead()
    {
        var output = "worktree /repo/detached\nHEAD abc1234\ndetached\n\n";

        var entries = Parsing.ParseWorktreeList(output);

        Assert.Single(entries);
        Assert.True(entries[0].IsDetached);
        Assert.Null(entries[0].Branch);
    }

    [Fact]
    public void ParseWorktreeList_HandlesNoTrailingBlankLine()
    {
        var output = "worktree /repo/main\nbranch refs/heads/main";

        var entries = Parsing.ParseWorktreeList(output);

        Assert.Single(entries);
        Assert.Equal("main", entries[0].Branch);
    }

    [Fact]
    public void ParseWorktreeList_ReturnsEmptyForEmptyInput()
    {
        var entries = Parsing.ParseWorktreeList("");
        Assert.Empty(entries);
    }

    [Fact]
    public void ParseUpstreamInfo_ParsesTrackingBranches()
    {
        var output = "main\torigin/main\t\nfeat\torigin/feat\t[gone]\n";

        var map = Parsing.ParseUpstreamInfo(output);

        Assert.Equal(2, map.Count);
        Assert.Equal("origin/main", map["main"].Upstream);
        Assert.False(map["main"].IsGone);
        Assert.True(map["feat"].IsGone);
    }

    [Fact]
    public void ParseUpstreamInfo_HandlesMissingUpstream()
    {
        var output = "local\t\t\n";

        var map = Parsing.ParseUpstreamInfo(output);

        Assert.Single(map);
        Assert.Null(map["local"].Upstream);
        Assert.False(map["local"].IsGone);
    }

    [Fact]
    public void ParseUpstreamInfo_ReturnsEmptyForEmptyInput()
    {
        var map = Parsing.ParseUpstreamInfo("");
        Assert.Empty(map);
    }

    [Fact]
    public void BuildTree_CreatesSingleLevelTree()
    {
        var branches = new List<BranchInfo>
        {
            new("main", "origin/main", false, false),
            new("develop", "origin/develop", false, false),
        };

        var root = Parsing.BuildTree(branches);

        Assert.Equal(2, root.Children.Count);
        Assert.Contains("main", root.Children.Keys);
        Assert.Contains("develop", root.Children.Keys);
    }

    [Fact]
    public void BuildTree_CreatesNestedTree()
    {
        var branches = new List<BranchInfo>
        {
            new("feat/auth", "origin/feat/auth", false, false),
            new("feat/ui", "origin/feat/ui", false, false),
            new("main", "origin/main", false, false),
        };

        var root = Parsing.BuildTree(branches);

        Assert.Equal(2, root.Children.Count);
        Assert.Equal(2, root.Children["feat"].Children.Count);
    }

    [Fact]
    public void BuildTree_AttachesBranchInfoToLeaf()
    {
        var info = new BranchInfo("feat/thing", null, true, false);
        var branches = new List<BranchInfo> { info };

        var root = Parsing.BuildTree(branches);

        Assert.Equal(info, root.Children["feat"].Children["thing"].Info);
    }
}
