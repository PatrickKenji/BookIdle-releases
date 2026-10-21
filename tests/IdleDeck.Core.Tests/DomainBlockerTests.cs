using IdleDeck.Core.Net;

namespace IdleDeck.Core.Tests;

public class DomainBlockerTests
{
    private readonly DomainBlocker _blocker = new(["DoubleClick.net", "pokeidle.io"]);

    [Fact]
    public void Blocks_exact_and_subdomain()
    {
        Assert.True(_blocker.IsBlocked(new Uri("https://doubleclick.net/x")));
        Assert.True(_blocker.IsBlocked(new Uri("https://ad.g.doubleclick.net/x")));
    }

    [Fact]
    public void Does_not_block_lookalike()
    {
        Assert.False(_blocker.IsBlocked(new Uri("https://notdoubleclick.net/")));
    }

    [Fact]
    public void Never_blocks_pokeidle()
    {
        Assert.False(_blocker.IsBlocked(new Uri("https://pokeidle.io/")));
        Assert.False(_blocker.IsBlocked(new Uri("https://api.pokeidle.io/")));
    }

    [Fact]
    public void Domains_are_normalized_and_exclude_pokeidle() =>
        Assert.Equal(["doubleclick.net"], _blocker.Domains);
}
