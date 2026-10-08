using System.Net;
using System.Net.Http.Json;
using Bidding.Application.Auctions;
using Bidding.Domain.Auctions;
using Bidding.Infrastructure.Seeding;
using Microsoft.AspNetCore.Mvc;

namespace Bidding.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class BiddingTests(ApiFactory api)
{
    [Fact]
    public async Task Anyone_can_browse_but_bidding_needs_a_login()
    {
        var auction = await api.NewAuctionAsync();
        var anonymous = api.CreateClient();

        var list = await (await anonymous.GetAsync("/api/auctions")).ReadAs<List<AuctionSummaryDto>>();
        var detail = await anonymous.GetAsync($"/api/auctions/{auction.Id}");
        var bid = await anonymous.Bid(auction.Id, 1000m);

        Assert.Contains(list, a => a.Id == auction.Id);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, bid.StatusCode);
    }

    [Fact]
    public async Task Accepted_bid_moves_the_price_and_shows_in_history()
    {
        var auction = await api.NewAuctionAsync();
        var (_, token) = await api.NewBidderAsync("Mali Kittisak");
        var client = api.ClientWithToken(token);

        var result = await (await client.Bid(auction.Id, 1100m)).ReadAs<BidResultDto>();
        var detail = await (await client.GetAsync($"/api/auctions/{auction.Id}")).ReadAs<AuctionDetailDto>();

        Assert.Equal(1100m, result.Amount);
        Assert.Equal(1150m, result.MinimumNextBid);
        Assert.Equal(1100m, detail.Summary.CurrentPrice);
        Assert.Equal(1, detail.Summary.BidCount);
        var shown = Assert.Single(detail.RecentBids);
        Assert.Equal("Mali K.", shown.BidderName); // surname hidden from other bidders
    }

    [Fact]
    public async Task Rule_violations_return_422_with_a_reason()
    {
        var auction = await api.NewAuctionAsync();
        var (_, token) = await api.NewBidderAsync();
        var client = api.ClientWithToken(token);

        var tooLow = await client.Bid(auction.Id, 999m);
        await client.Bid(auction.Id, 1000m);
        var selfOutbid = await client.Bid(auction.Id, 1500m);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, tooLow.StatusCode);
        Assert.Contains("at least 1000", (await tooLow.Content.ReadFromJsonAsync<ProblemDetails>())!.Title);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, selfOutbid.StatusCode);
    }

    [Fact]
    public async Task Cannot_bid_before_the_auction_starts()
    {
        var auction = await api.NewAuctionAsync(startsIn: TimeSpan.FromMinutes(5));
        var (_, token) = await api.NewBidderAsync();

        var response = await api.ClientWithToken(token).Bid(auction.Id, 1000m);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(10.123)]
    public async Task Invalid_amounts_are_rejected_with_400(decimal amount)
    {
        var auction = await api.NewAuctionAsync();
        var (_, token) = await api.NewBidderAsync();

        var response = await api.ClientWithToken(token).Bid(auction.Id, amount);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Same_idempotency_key_never_bids_twice()
    {
        var auction = await api.NewAuctionAsync();
        var (_, token) = await api.NewBidderAsync();
        var client = api.ClientWithToken(token);
        var key = Guid.NewGuid().ToString();

        var first = await (await client.Bid(auction.Id, 1200m, key)).ReadAs<BidResultDto>();
        var retry = await (await client.Bid(auction.Id, 1200m, key)).ReadAs<BidResultDto>();

        Assert.False(first.Replayed);
        Assert.True(retry.Replayed);
        Assert.Equal(first.BidId, retry.BidId);
        var detail = await (await client.GetAsync($"/api/auctions/{auction.Id}")).ReadAs<AuctionDetailDto>();
        Assert.Equal(1, detail.Summary.BidCount);
    }

    [Fact]
    public async Task Parallel_retries_with_one_key_still_make_one_bid()
    {
        var auction = await api.NewAuctionAsync();
        var (_, token) = await api.NewBidderAsync();
        var client = api.ClientWithToken(token);
        var key = Guid.NewGuid().ToString();

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => client.Bid(auction.Id, 1300m, key)));

        // The first copy wins; the others replay it, or (if they read the auction before the winner
        // saved) are rejected because this bidder is now the leader. Either way: one bid in the database.
        Assert.All(responses, r => Assert.Contains(r.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.UnprocessableEntity }));
        var detail = await (await client.GetAsync($"/api/auctions/{auction.Id}")).ReadAs<AuctionDetailDto>();
        Assert.Equal(1, detail.Summary.BidCount);
    }

    [Fact]
    public async Task Twenty_bidders_with_the_same_amount_at_the_same_moment_produce_one_winner()
    {
        var auction = await api.NewAuctionAsync();
        var bidders = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => api.NewBidderAsync()));

        var responses = await Task.WhenAll(bidders.Select(b => api.ClientWithToken(b.Token).Bid(auction.Id, 1000m)));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.All(responses.Where(r => r.StatusCode != HttpStatusCode.OK), r =>
            Assert.Contains(r.StatusCode, new[] { HttpStatusCode.UnprocessableEntity, HttpStatusCode.Conflict }));

        var detail = await (await api.CreateClient().GetAsync($"/api/auctions/{auction.Id}")).ReadAs<AuctionDetailDto>();
        Assert.Equal(1, detail.Summary.BidCount);
        Assert.Equal(1000m, detail.Summary.CurrentPrice);
    }

    [Fact]
    public async Task Racing_bids_of_different_amounts_leave_a_consistent_history()
    {
        var auction = await api.NewAuctionAsync(increment: 10m);
        var bidders = await Task.WhenAll(Enumerable.Range(0, 30).Select(_ => api.NewBidderAsync()));

        var responses = await Task.WhenAll(bidders.Select((b, i) =>
            api.ClientWithToken(b.Token).Bid(auction.Id, 1000m + i * 10m)));

        var accepted = new List<BidResultDto>();
        foreach (var r in responses.Where(r => r.StatusCode == HttpStatusCode.OK))
            accepted.Add(await r.ReadAs<BidResultDto>());

        var detail = await (await api.CreateClient().GetAsync($"/api/auctions/{auction.Id}")).ReadAs<AuctionDetailDto>();
        Assert.NotEmpty(accepted);
        Assert.Equal(accepted.Count, detail.Summary.BidCount);
        Assert.Equal(accepted.Max(a => a.Amount), detail.Summary.CurrentPrice);

        // In the order they were saved, every accepted bid beat the one before it by at least the increment.
        var inSaveOrder = accepted.OrderBy(a => a.BidId).Select(a => a.Amount).ToList();
        for (var i = 1; i < inSaveOrder.Count; i++)
            Assert.True(inSaveOrder[i] >= inSaveOrder[i - 1] + 10m, $"Bid {inSaveOrder[i]} did not beat {inSaveOrder[i - 1]}");
    }

    [Fact]
    public async Task Bid_in_the_final_seconds_extends_the_auction()
    {
        var auction = await api.NewAuctionAsync(runsFor: TimeSpan.FromMinutes(5));
        var (_, token) = await api.NewBidderAsync();
        api.Clock.Advance(TimeSpan.FromMinutes(5) - TimeSpan.FromSeconds(10));

        var result = await (await api.ClientWithToken(token).Bid(auction.Id, 1000m)).ReadAs<BidResultDto>();

        Assert.True(result.Extended);
        Assert.Equal(api.Clock.GetUtcNow() + Auction.SnipeWindow, result.EndsAt);
    }

    [Fact]
    public async Task Only_admins_can_create_auctions()
    {
        var (_, token) = await api.NewBidderAsync();
        var start = api.Clock.GetUtcNow();

        var response = await api.ClientWithToken(token).PostAsJsonAsync("/api/auctions", new CreateAuctionRequest(
            "x", "x", "20GP", "x", "x", 2020, 100m, 10m, null, start, start.AddMinutes(5)), ApiFactory.Json);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Admin_cannot_create_an_invalid_auction()
    {
        var admin = api.ClientWithToken(await api.TokenFor(DataSeeder.AdminEmail));
        var start = api.Clock.GetUtcNow();

        var response = await admin.PostAsJsonAsync("/api/auctions", new CreateAuctionRequest(
            "", "x", "99XX", "x", "x", 2020, -1m, 10m, null, start, start.AddMinutes(-5)), ApiFactory.Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.Contains("ContainerType", problem!.Errors.Keys);
        Assert.Contains("StartingPrice", problem.Errors.Keys);
    }
}
