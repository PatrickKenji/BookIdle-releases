using IdleDeck.Core.Market;

namespace IdleDeck.Core.Tests;

public class OfferClaimsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void The_first_account_to_claim_an_offer_keeps_it()
    {
        var claims = new OfferClaims();
        Assert.True(claims.TryClaim(1, "a", Now));
        Assert.False(claims.TryClaim(1, "b", Now.AddSeconds(5)));
        Assert.True(claims.TryClaim(1, "a", Now.AddSeconds(5)));
    }

    [Fact]
    public void Different_offers_do_not_interfere()
    {
        var claims = new OfferClaims();
        Assert.True(claims.TryClaim(1, "a", Now));
        Assert.True(claims.TryClaim(2, "b", Now));
    }

    [Fact]
    public void A_claim_expires()
    {
        var claims = new OfferClaims();
        Assert.True(claims.TryClaim(1, "a", Now));
        Assert.False(claims.TryClaim(1, "b", Now.Add(OfferClaims.Lifetime).AddSeconds(-1)));
        Assert.True(claims.TryClaim(1, "b", Now.Add(OfferClaims.Lifetime)));
    }
}
