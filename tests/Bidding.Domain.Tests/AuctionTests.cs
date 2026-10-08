using Bidding.Domain.Auctions;
using Bidding.Domain.Common;
using Bidding.Domain.Users;

namespace Bidding.Domain.Tests;

public class AuctionTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = Start.AddMinutes(10);
    private const int Alice = 1, Bob = 2, Carol = 3;

    private static Auction NewAuction(decimal? reserve = null) => Auction.Create(
        "40ft High Cube, cargo-worthy", "Grade A", "40hc", "Laem Chabang", "Cargo-worthy", 2019,
        startingPrice: 1000m, minIncrement: 50m, reservePrice: reserve, Start, End, now: Start.AddMinutes(-5));

    [Fact]
    public void First_bid_must_be_at_least_the_starting_price()
    {
        var auction = NewAuction();

        Assert.Throws<DomainException>(() => auction.PlaceBid(Alice, 999m, Start.AddMinutes(1)));
        var placed = auction.PlaceBid(Alice, 1000m, Start.AddMinutes(1));

        Assert.Equal(1000m, auction.CurrentPrice);
        Assert.Equal(Alice, auction.LeadingBidderId);
        Assert.Null(placed.PreviousLeaderId);
        Assert.Equal(1050m, auction.MinimumNextBid);
    }

    [Fact]
    public void Next_bid_must_beat_the_price_by_the_increment()
    {
        var auction = NewAuction();
        auction.PlaceBid(Alice, 1000m, Start.AddMinutes(1));

        var ex = Assert.Throws<DomainException>(() => auction.PlaceBid(Bob, 1049m, Start.AddMinutes(2)));
        Assert.Contains("1050", ex.Message);

        var placed = auction.PlaceBid(Bob, 1050m, Start.AddMinutes(2));
        Assert.Equal(Alice, placed.PreviousLeaderId);
        Assert.Equal(2, auction.BidCount);
    }

    [Fact]
    public void Leader_cannot_outbid_themselves()
    {
        var auction = NewAuction();
        auction.PlaceBid(Alice, 1000m, Start.AddMinutes(1));

        var ex = Assert.Throws<DomainException>(() => auction.PlaceBid(Alice, 2000m, Start.AddMinutes(2)));
        Assert.Contains("already the highest", ex.Message);
    }

    [Fact]
    public void Bids_before_start_or_after_end_are_rejected()
    {
        var auction = NewAuction();

        Assert.Contains("hasn't started", Assert.Throws<DomainException>(() => auction.PlaceBid(Alice, 1000m, Start.AddSeconds(-1))).Message);
        Assert.Contains("has ended", Assert.Throws<DomainException>(() => auction.PlaceBid(Alice, 1000m, End)).Message);
    }

    [Fact]
    public void Bid_in_the_last_30_seconds_extends_the_auction()
    {
        var auction = NewAuction();
        var lateBid = End.AddSeconds(-10);

        var placed = auction.PlaceBid(Alice, 1000m, lateBid);

        Assert.True(placed.Extended);
        Assert.Equal(lateBid + Auction.SnipeWindow, auction.EndsAt);
        Assert.Equal(End, auction.ScheduledEndsAt);
        Assert.Equal(AuctionStatus.Live, auction.StatusAt(End.AddSeconds(5)));
    }

    [Fact]
    public void Early_bid_does_not_extend()
    {
        var auction = NewAuction();

        var placed = auction.PlaceBid(Alice, 1000m, End.AddMinutes(-2));

        Assert.False(placed.Extended);
        Assert.Equal(End, auction.EndsAt);
    }

    [Fact]
    public void Repeated_sniping_keeps_extending()
    {
        var auction = NewAuction();
        var t = End.AddSeconds(-5);
        var bidders = new[] { Alice, Bob, Carol };

        for (var i = 0; i < 6; i++)
        {
            auction.PlaceBid(bidders[i % 3], auction.MinimumNextBid, t);
            t = auction.EndsAt.AddSeconds(-5);
        }

        Assert.True(auction.EndsAt > End.AddMinutes(2));
    }

    [Fact]
    public void Status_follows_the_clock()
    {
        var auction = NewAuction();

        Assert.Equal(AuctionStatus.Scheduled, auction.StatusAt(Start.AddSeconds(-1)));
        Assert.Equal(AuctionStatus.Live, auction.StatusAt(Start));
        Assert.Equal(AuctionStatus.Ending, auction.StatusAt(End));
        auction.Close(End);
        Assert.Equal(AuctionStatus.Closed, auction.StatusAt(End));
    }

    [Fact]
    public void Closing_with_a_bid_above_reserve_sells_to_the_leader()
    {
        var auction = NewAuction(reserve: 1200m);
        auction.PlaceBid(Alice, 1000m, Start.AddMinutes(1));
        auction.PlaceBid(Bob, 1250m, Start.AddMinutes(2));

        Assert.True(auction.Close(End));

        Assert.Equal(AuctionOutcome.Sold, auction.Outcome);
        Assert.Equal(Bob, auction.WinnerId);
    }

    [Fact]
    public void Closing_below_reserve_has_no_winner()
    {
        var auction = NewAuction(reserve: 5000m);
        auction.PlaceBid(Alice, 1000m, Start.AddMinutes(1));

        auction.Close(End);

        Assert.Equal(AuctionOutcome.ReserveNotMet, auction.Outcome);
        Assert.Null(auction.WinnerId);
        Assert.False(auction.ReserveMet);
    }

    [Fact]
    public void Closing_without_bids_and_closing_twice()
    {
        var auction = NewAuction();

        Assert.True(auction.Close(End.AddSeconds(1)));
        Assert.False(auction.Close(End.AddSeconds(2)));
        Assert.Equal(AuctionOutcome.NoBids, auction.Outcome);
    }

    [Fact]
    public void Cannot_close_early()
    {
        var auction = NewAuction();

        Assert.Throws<DomainException>(() => auction.Close(End.AddSeconds(-1)));
    }

    [Theory]
    [InlineData(0, 50, null, "Starting price")]
    [InlineData(1000, 0, null, "increment")]
    [InlineData(1000, 50, 500.0, "Reserve")]
    public void Create_validates_prices(decimal start, decimal increment, double? reserve, string expected)
    {
        var ex = Assert.Throws<DomainException>(() => Auction.Create("t", "d", "20GP", "x", "c", 2020,
            start, increment, (decimal?)reserve, Start, End, Start));
        Assert.Contains(expected, ex.Message);
    }

    [Theory]
    [InlineData("Mali Kittisak", "Mali K.")]
    [InlineData("Tun", "Tun")]
    [InlineData("  Wei   Ling  Tan ", "Wei T.")]
    public void Public_names_hide_the_surname(string name, string expected)
    {
        Assert.Equal(expected, User.PublicName(name));
    }
}
