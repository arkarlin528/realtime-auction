using Bidding.Application.Common;
using Bidding.Domain.Auctions;
using Bidding.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bidding.Application.Auctions;

/// <summary>
/// Places bids safely under concurrency. Two bidders who both saw "$1,000" can both submit $1,050;
/// only one save can win the version check. The loser reloads and is re-validated against the new
/// price, so it either goes through (if still high enough) or gets a clear "bid at least …" error.
/// Nothing is locked while the user thinks. See docs/adr/0001-optimistic-concurrency-for-bids.md.
/// </summary>
public sealed class BiddingService(
    IAppDbContext db,
    IAuctionNotifier notifier,
    TimeProvider clock,
    ILogger<BiddingService> logger)
{
    public const int MaxAttempts = 5;

    public async Task<BidResultDto> PlaceBidAsync(int auctionId, int bidderId, decimal amount, string? idempotencyKey,
        CancellationToken ct)
    {
        if (idempotencyKey is not null && await FindReplayAsync(bidderId, idempotencyKey, ct) is { } replay)
            return replay;

        for (var attempt = 1; ; attempt++)
        {
            var auction = await db.Auctions.FirstOrDefaultAsync(a => a.Id == auctionId, ct)
                ?? throw new NotFoundException($"Auction {auctionId} was not found.");

            var now = clock.GetUtcNow();
            var placed = auction.PlaceBid(bidderId, amount, now, idempotencyKey); // throws DomainException -> 422
            db.Bids.Add(placed.Bid);

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException) when (attempt < MaxAttempts)
            {
                // Another bid was saved between our read and our write. Start again from fresh data.
                logger.LogDebug("Bid race on auction {AuctionId}, attempt {Attempt}", auctionId, attempt);
                db.ClearTracking();
                continue;
            }
            catch (DbUpdateConcurrencyException)
            {
                db.ClearTracking();
                throw new ConflictException("The auction is very busy right now. Please try again.");
            }
            catch (DbUpdateException ex) when (idempotencyKey is not null && db.IsUniqueViolation(ex))
            {
                // The same request arrived twice at the same moment and the other copy won.
                db.ClearTracking();
                return await FindReplayAsync(bidderId, idempotencyKey, ct)
                    ?? throw new ConflictException("Duplicate bid request.");
            }

            await NotifyAsync(auction, placed, now, ct);
            return new BidResultDto(placed.Bid.Id, auction.Id, placed.Bid.Amount, placed.Bid.PlacedAt,
                auction.MinimumNextBid, auction.EndsAt, placed.Extended, Replayed: false);
        }
    }

    private async Task<BidResultDto?> FindReplayAsync(int bidderId, string idempotencyKey, CancellationToken ct)
    {
        var bid = await db.Bids.AsNoTracking()
            .Include(b => b.Auction)
            .FirstOrDefaultAsync(b => b.BidderId == bidderId && b.IdempotencyKey == idempotencyKey, ct);
        return bid is null
            ? null
            : new BidResultDto(bid.Id, bid.AuctionId, bid.Amount, bid.PlacedAt, bid.Auction.MinimumNextBid,
                bid.Auction.EndsAt, Extended: false, Replayed: true);
    }

    private async Task NotifyAsync(Auction auction, BidPlaced placed, DateTimeOffset now, CancellationToken ct)
    {
        var bidderName = await db.Users.Where(u => u.Id == placed.Bid.BidderId)
            .Select(u => u.DisplayName).FirstAsync(ct);

        await notifier.BidPlacedAsync(new BidPlacedMessage(
            auction.Id, placed.Bid.Id, placed.Bid.Amount, placed.Bid.BidderId, User.PublicName(bidderName),
            placed.Bid.PlacedAt, auction.BidCount, auction.MinimumNextBid, auction.EndsAt, placed.Extended,
            auction.ReserveMet, now), ct);

        if (placed.PreviousLeaderId is { } outbid)
        {
            await notifier.OutbidAsync(outbid, new OutbidMessage(
                auction.Id, auction.Title, placed.Bid.Amount, auction.MinimumNextBid, now), ct);
        }
    }
}
