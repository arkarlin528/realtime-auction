using Bidding.Domain.Common;

namespace Bidding.Domain.Auctions;

/// <summary>An accepted bid. Rejected bids are never stored, so the bid history is always strictly increasing.</summary>
public sealed class Bid : Entity
{
    private Bid() { }

    internal Bid(Auction auction, int bidderId, decimal amount, DateTimeOffset placedAt, string? idempotencyKey)
    {
        Auction = auction;
        AuctionId = auction.Id;
        BidderId = bidderId;
        Amount = amount;
        PlacedAt = placedAt;
        IdempotencyKey = idempotencyKey;
    }

    public int AuctionId { get; private set; }
    public Auction Auction { get; private set; } = default!;
    public int BidderId { get; private set; }
    public decimal Amount { get; private set; }
    public DateTimeOffset PlacedAt { get; private set; }

    /// <summary>Client-generated key; the same (bidder, key) is accepted at most once, so retries can't double-bid.</summary>
    public string? IdempotencyKey { get; private set; }
}
