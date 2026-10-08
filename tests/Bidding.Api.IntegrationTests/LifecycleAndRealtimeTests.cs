using System.Net;
using System.Net.Http.Json;
using Bidding.Application.Auctions;
using Bidding.Application.Auth;
using Bidding.Domain.Auctions;
using Bidding.Infrastructure.Seeding;
using Microsoft.AspNetCore.SignalR.Client;

namespace Bidding.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class LifecycleTests(ApiFactory api)
{
    [Fact]
    public async Task Auction_closes_with_a_winner_and_shows_up_in_my_bids()
    {
        var auction = await api.NewAuctionAsync(runsFor: TimeSpan.FromMinutes(3));
        var alice = await api.NewBidderAsync("Alice Wong");
        var bob = await api.NewBidderAsync("Bob Tan");
        await api.ClientWithToken(alice.Token).Bid(auction.Id, 1000m);
        await api.ClientWithToken(bob.Token).Bid(auction.Id, 1050m);

        api.Clock.Advance(TimeSpan.FromMinutes(4));
        // The background closer also ticks on the fake clock and may get there first; either way it closes once.
        await api.CloseDueAuctionsAsync();

        var detail = await (await api.CreateClient().GetAsync($"/api/auctions/{auction.Id}")).ReadAs<AuctionDetailDto>();
        Assert.Equal(AuctionStatus.Closed, detail.Summary.Status);
        Assert.Equal(AuctionOutcome.Sold, detail.Summary.Outcome);
        Assert.Equal("Bob T.", detail.WinnerName);

        var bobs = await (await api.ClientWithToken(bob.Token).GetAsync("/api/me/bids")).ReadAs<List<MyBidDto>>();
        var alices = await (await api.ClientWithToken(alice.Token).GetAsync("/api/me/bids")).ReadAs<List<MyBidDto>>();
        Assert.Equal(MyBidState.Won, bobs.Single(b => b.Auction.Id == auction.Id).State);
        Assert.Equal(MyBidState.Lost, alices.Single(b => b.Auction.Id == auction.Id).State);

        var late = await api.ClientWithToken(alice.Token).Bid(auction.Id, 5000m);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, late.StatusCode);
    }

    [Fact]
    public async Task Below_reserve_closes_without_a_sale()
    {
        var auction = await api.NewAuctionAsync(runsFor: TimeSpan.FromMinutes(2), reserve: 9000m);
        var bidder = await api.NewBidderAsync();
        await api.ClientWithToken(bidder.Token).Bid(auction.Id, 1000m);

        api.Clock.Advance(TimeSpan.FromMinutes(3));
        await api.CloseDueAuctionsAsync();

        var detail = await (await api.CreateClient().GetAsync($"/api/auctions/{auction.Id}")).ReadAs<AuctionDetailDto>();
        Assert.Equal(AuctionOutcome.ReserveNotMet, detail.Summary.Outcome);
        Assert.Null(detail.WinnerName);
        Assert.True(detail.Summary.HasReserve);
        Assert.False(detail.Summary.ReserveMet);
    }

    [Fact]
    public async Task Closed_list_contains_finished_auctions_only()
    {
        var auction = await api.NewAuctionAsync(runsFor: TimeSpan.FromMinutes(1));
        api.Clock.Advance(TimeSpan.FromMinutes(2));
        await api.CloseDueAuctionsAsync();

        var closed = await (await api.CreateClient().GetAsync("/api/auctions?filter=Closed")).ReadAs<List<AuctionSummaryDto>>();
        var live = await (await api.CreateClient().GetAsync("/api/auctions?filter=Live")).ReadAs<List<AuctionSummaryDto>>();

        Assert.Contains(closed, a => a.Id == auction.Id && a.Outcome == AuctionOutcome.NoBids);
        Assert.DoesNotContain(live, a => a.Id == auction.Id);
        Assert.All(closed, a => Assert.Equal(AuctionStatus.Closed, a.Status));
    }
}

[Collection(ApiCollection.Name)]
public class AuthTests(ApiFactory api)
{
    [Fact]
    public async Task Register_then_me()
    {
        var email = $"new-{Guid.NewGuid():N}@test.local";

        var registered = await (await api.CreateClient().PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(email, "New Person", "long-enough-password"))).ReadAs<LoginResponse>();
        var me = await (await api.ClientWithToken(registered.AccessToken).GetAsync("/api/auth/me")).ReadAs<UserDto>();

        Assert.Equal(email, me.Email);
        Assert.Equal(Bidding.Domain.Users.UserRole.Bidder, me.Role);
    }

    [Fact]
    public async Task Registering_an_existing_email_is_a_conflict()
    {
        var response = await api.CreateClient().PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(DataSeeder.BidderEmail, "Someone", "long-enough-password"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Bot_accounts_cannot_log_in()
    {
        var response = await api.CreateClient().PostAsJsonAsync("/api/auth/login",
            new LoginRequest("bot1@bots.demo.test", ApiFactory.DemoPassword));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}

[Collection(ApiCollection.Name)]
public class RealtimeTests(ApiFactory api)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Watchers_see_bids_and_the_outbid_bidder_is_told_privately()
    {
        var auction = await api.NewAuctionAsync();
        var alice = await api.NewBidderAsync("Alice Wong");
        var bob = await api.NewBidderAsync("Bob Tan");
        var carol = await api.NewBidderAsync("Carol Lim");

        await using var watcher = api.Hub(); // anonymous
        await using var aliceHub = api.Hub(alice.Token);
        await using var carolHub = api.Hub(carol.Token);

        var watcherSawBob = Expect<BidPlacedMessage>(watcher, "BidPlaced", m => m.AuctionId == auction.Id && m.Amount == 1050m);
        var aliceOutbid = Expect<OutbidMessage>(aliceHub, "Outbid", m => m.AuctionId == auction.Id);
        var carolOutbid = Expect<OutbidMessage>(carolHub, "Outbid", m => m.AuctionId == auction.Id);

        foreach (var hub in new[] { watcher, aliceHub, carolHub })
        {
            await hub.StartAsync();
            await hub.InvokeAsync("JoinAuction", auction.Id);
        }

        await api.ClientWithToken(alice.Token).Bid(auction.Id, 1000m);
        await api.ClientWithToken(bob.Token).Bid(auction.Id, 1050m);

        var seen = await watcherSawBob.Task.WaitAsync(Timeout);
        Assert.Equal("Bob T.", seen.BidderName);
        Assert.Equal(1100m, seen.MinimumNextBid);

        var outbid = await aliceOutbid.Task.WaitAsync(Timeout);
        Assert.Equal(1050m, outbid.NewPrice);

        var winner = await Task.WhenAny(carolOutbid.Task, Task.Delay(TimeSpan.FromSeconds(1)));
        Assert.NotSame(carolOutbid.Task, winner); // Carol never bid, so she gets no outbid message
    }

    [Fact]
    public async Task Room_is_told_when_the_auction_closes()
    {
        var auction = await api.NewAuctionAsync(runsFor: TimeSpan.FromMinutes(1));
        await using var watcher = api.Hub();
        var closed = Expect<AuctionClosedMessage>(watcher, "AuctionClosed", m => m.AuctionId == auction.Id);
        await watcher.StartAsync();
        await watcher.InvokeAsync("JoinAuction", auction.Id);

        api.Clock.Advance(TimeSpan.FromMinutes(2));
        await api.CloseDueAuctionsAsync();

        var message = await closed.Task.WaitAsync(Timeout);
        Assert.Equal(AuctionOutcome.NoBids, message.Outcome);
    }

    private static TaskCompletionSource<T> Expect<T>(HubConnection hub, string method, Func<T, bool> match)
    {
        var received = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        hub.On<T>(method, message =>
        {
            if (match(message))
                received.TrySetResult(message);
        });
        return received;
    }
}
