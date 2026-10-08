using Bidding.Domain.Common;

namespace Bidding.Domain.Auctions;

public enum AuctionStatus
{
    Scheduled = 1,
    Live = 2,
    /// <summary>Past its end time; the closer job hasn't settled it yet. No more bids are accepted.</summary>
    Ending = 3,
    Closed = 4,
}

public enum AuctionOutcome
{
    Sold = 1,
    ReserveNotMet = 2,
    NoBids = 3,
}

/// <summary>Result of an accepted bid, used to notify the room and the bidder who was just outbid.</summary>
public sealed record BidPlaced(Bid Bid, int? PreviousLeaderId, bool Extended);

/// <summary>
/// Aggregate root for one auction of one container. Every rule about bids lives here, and the
/// <see cref="Version"/> concurrency token makes sure two bids that both "saw" the same price can't
/// both be saved. See docs/adr/0001-optimistic-concurrency-for-bids.md.
/// </summary>
public sealed class Auction : Entity
{
    /// <summary>A bid in the last 30 seconds pushes the end time out, so nobody can win by sniping.</summary>
    public static readonly TimeSpan SnipeWindow = TimeSpan.FromSeconds(30);

    private Auction() { }

    public string Title { get; private set; } = default!;
    public string Description { get; private set; } = default!;
    public string ContainerType { get; private set; } = default!;
    public string Location { get; private set; } = default!;
    public string Condition { get; private set; } = default!;
    public int YearBuilt { get; private set; }
    public string Currency { get; private set; } = "USD";

    public decimal StartingPrice { get; private set; }
    public decimal MinIncrement { get; private set; }
    /// <summary>Hidden minimum. Below it the auction closes as "reserve not met".</summary>
    public decimal? ReservePrice { get; private set; }

    public DateTimeOffset StartsAt { get; private set; }
    public DateTimeOffset EndsAt { get; private set; }
    public DateTimeOffset ScheduledEndsAt { get; private set; }

    public decimal? CurrentPrice { get; private set; }
    public int? LeadingBidderId { get; private set; }
    public int BidCount { get; private set; }

    public DateTimeOffset? ClosedAt { get; private set; }
    public AuctionOutcome? Outcome { get; private set; }
    public int? WinnerId { get; private set; }

    /// <summary>Mapped to PostgreSQL's xmin system column: changes on every update, free of charge.</summary>
    public uint Version { get; private set; }

    public decimal MinimumNextBid => CurrentPrice is { } price ? price + MinIncrement : StartingPrice;

    public bool ReserveMet => ReservePrice is null || (CurrentPrice ?? 0) >= ReservePrice;

    public static Auction Create(string title, string description, string containerType, string location,
        string condition, int yearBuilt, decimal startingPrice, decimal minIncrement, decimal? reservePrice,
        DateTimeOffset startsAt, DateTimeOffset endsAt, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new DomainException("Title is required.");
        if (startingPrice <= 0)
            throw new DomainException("Starting price must be greater than zero.");
        if (minIncrement <= 0)
            throw new DomainException("Minimum increment must be greater than zero.");
        if (reservePrice is { } reserve && reserve < startingPrice)
            throw new DomainException("Reserve price can't be lower than the starting price.");
        if (endsAt <= startsAt)
            throw new DomainException("The auction must end after it starts.");
        if (endsAt <= now)
            throw new DomainException("The auction must end in the future.");

        return new Auction
        {
            Title = title.Trim(),
            Description = description.Trim(),
            ContainerType = containerType.Trim().ToUpperInvariant(),
            Location = location.Trim(),
            Condition = condition.Trim(),
            YearBuilt = yearBuilt,
            StartingPrice = startingPrice,
            MinIncrement = minIncrement,
            ReservePrice = reservePrice,
            StartsAt = startsAt,
            EndsAt = endsAt,
            ScheduledEndsAt = endsAt,
        };
    }

    public AuctionStatus StatusAt(DateTimeOffset now) =>
        ClosedAt is not null ? AuctionStatus.Closed
        : now < StartsAt ? AuctionStatus.Scheduled
        : now < EndsAt ? AuctionStatus.Live
        : AuctionStatus.Ending;

    public BidPlaced PlaceBid(int bidderId, decimal amount, DateTimeOffset now, string? idempotencyKey = null)
    {
        var status = StatusAt(now);
        if (status != AuctionStatus.Live)
            throw new DomainException(status switch
            {
                AuctionStatus.Scheduled => "This auction hasn't started yet.",
                _ => "This auction has ended.",
            });
        if (bidderId == LeadingBidderId)
            throw new DomainException("You're already the highest bidder.");
        if (amount < MinimumNextBid)
            throw new DomainException($"Bid at least {MinimumNextBid:0.##} {Currency}.");

        var previousLeader = LeadingBidderId;
        CurrentPrice = amount;
        LeadingBidderId = bidderId;
        BidCount++;

        var extended = EndsAt - now < SnipeWindow;
        if (extended)
            EndsAt = now + SnipeWindow;

        return new BidPlaced(new Bid(this, bidderId, amount, now, idempotencyKey), previousLeader, extended);
    }

    /// <summary>Settles the auction once its end time has passed. Safe to call again: it's a no-op when already closed.</summary>
    public bool Close(DateTimeOffset now)
    {
        if (ClosedAt is not null)
            return false;
        if (now < EndsAt)
            throw new DomainException("This auction hasn't ended yet.");

        ClosedAt = now;
        if (LeadingBidderId is null)
        {
            Outcome = AuctionOutcome.NoBids;
        }
        else if (!ReserveMet)
        {
            Outcome = AuctionOutcome.ReserveNotMet;
        }
        else
        {
            Outcome = AuctionOutcome.Sold;
            WinnerId = LeadingBidderId;
        }

        return true;
    }
}
