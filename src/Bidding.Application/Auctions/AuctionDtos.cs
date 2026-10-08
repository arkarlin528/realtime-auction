using Bidding.Domain.Auctions;

namespace Bidding.Application.Auctions;

public sealed record AuctionSummaryDto(
    int Id,
    string Title,
    string ContainerType,
    string Location,
    string Condition,
    int YearBuilt,
    string Currency,
    decimal StartingPrice,
    decimal? CurrentPrice,
    decimal MinimumNextBid,
    int BidCount,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    AuctionStatus Status,
    bool HasReserve,
    bool ReserveMet,
    int? LeadingBidderId,
    AuctionOutcome? Outcome);

public sealed record BidDto(int Id, decimal Amount, int BidderId, string BidderName, DateTimeOffset PlacedAt);

public sealed record AuctionDetailDto(
    AuctionSummaryDto Summary,
    string Description,
    decimal MinIncrement,
    DateTimeOffset ScheduledEndsAt,
    DateTimeOffset? ClosedAt,
    string? WinnerName,
    IReadOnlyList<BidDto> RecentBids);

public enum AuctionListFilter
{
    Live = 1,
    Scheduled = 2,
    Closed = 3,
}

public sealed record PlaceBidRequest(decimal Amount);

public sealed record BidResultDto(
    int BidId,
    int AuctionId,
    decimal Amount,
    DateTimeOffset PlacedAt,
    decimal MinimumNextBid,
    DateTimeOffset EndsAt,
    bool Extended,
    /// <summary>True when this was a retry with an idempotency key we'd already accepted; nothing new was saved.</summary>
    bool Replayed);

public sealed record CreateAuctionRequest(
    string Title,
    string Description,
    string ContainerType,
    string Location,
    string Condition,
    int YearBuilt,
    decimal StartingPrice,
    decimal MinIncrement,
    decimal? ReservePrice,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt);

public enum MyBidState
{
    Leading = 1,
    Outbid = 2,
    Won = 3,
    Lost = 4,
}

public sealed record MyBidDto(AuctionSummaryDto Auction, decimal MyHighestBid, int MyBidCount, MyBidState State);

// SignalR payloads. Each carries ServerTime so clients can line their countdowns up with the server clock.

public sealed record BidPlacedMessage(
    int AuctionId,
    int BidId,
    decimal Amount,
    int BidderId,
    string BidderName,
    DateTimeOffset PlacedAt,
    int BidCount,
    decimal MinimumNextBid,
    DateTimeOffset EndsAt,
    bool Extended,
    bool ReserveMet,
    DateTimeOffset ServerTime);

public sealed record OutbidMessage(int AuctionId, string Title, decimal NewPrice, decimal MinimumNextBid, DateTimeOffset ServerTime);

public sealed record AuctionClosedMessage(
    int AuctionId,
    string Title,
    AuctionOutcome Outcome,
    decimal? FinalPrice,
    int? WinnerId,
    string? WinnerName,
    DateTimeOffset ServerTime);

internal static class AuctionMapping
{
    public static AuctionSummaryDto ToSummary(this Auction a, DateTimeOffset now) => new(
        a.Id, a.Title, a.ContainerType, a.Location, a.Condition, a.YearBuilt, a.Currency, a.StartingPrice,
        a.CurrentPrice, a.MinimumNextBid, a.BidCount, a.StartsAt, a.EndsAt, a.StatusAt(now),
        a.ReservePrice is not null, a.ReserveMet, a.LeadingBidderId, a.Outcome);
}
