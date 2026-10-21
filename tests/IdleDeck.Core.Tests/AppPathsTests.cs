namespace IdleDeck.Core.Tests;

public class AppPathsTests
{
    [Fact]
    public void ResolveRoot_uses_override_when_set()
    {
        Assert.Equal(@"C:\x", AppPaths.ResolveRoot(@"C:\x", "def"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void ResolveRoot_falls_back_when_null_or_blank(string? overrideDir)
    {
        Assert.Equal("def", AppPaths.ResolveRoot(overrideDir, "def"));
    }
}
