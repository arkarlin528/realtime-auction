using Bidding.Application.Common;
using Bidding.Domain.Auctions;
using Bidding.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Bidding.Application.Auctions;

public sealed class AuctionQueries(IAppDbContext db, ICurrentUser currentUser, TimeProvider clock)
{
    private const int ListLimit = 60;

    public async Task<List<AuctionSummaryDto>> ListAsync(AuctionListFilter filter, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var query = db.Auctions.AsNoTracking();

        query = filter switch
        {
            AuctionListFilter.Scheduled => query.Where(a => a.ClosedAt == null && a.StartsAt > now).OrderBy(a => a.StartsAt),
            AuctionListFilter.Closed => query.Where(a => a.ClosedAt != null).OrderByDescending(a => a.ClosedAt),
            _ => query.Where(a => a.ClosedAt == null && a.StartsAt <= now).OrderBy(a => a.EndsAt),
        };

        var auctions = await query.Take(ListLimit).ToListAsync(ct);
        return auctions.Select(a => a.ToSummary(now)).ToList();
    }

    public async Task<AuctionDetailDto> GetAsync(int id, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var auction = await db.Auctions.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct)
            ?? throw new NotFoundException($"Auction {id} was not found.");

        var bids = await db.Bids.AsNoTracking()
            .Where(b => b.AuctionId == id)
            .OrderByDescending(b => b.Amount)
            .Take(25)
            .Join(db.Users, b => b.BidderId, u => u.Id, (b, u) => new { b.Id, b.Amount, b.BidderId, u.DisplayName, b.PlacedAt })
            .ToListAsync(ct);

        string? winnerName = null;
        if (auction.WinnerId is { } winnerId)
            winnerName = User.PublicName(await db.Users.Where(u => u.Id == winnerId).Select(u => u.DisplayName).FirstAsync(ct));

        return new AuctionDetailDto(
            auction.ToSummary(now), auction.Description, auction.MinIncrement, auction.ScheduledEndsAt,
            auction.ClosedAt, winnerName,
            bids.Select(b => new BidDto(b.Id, b.Amount, b.BidderId, User.PublicName(b.DisplayName), b.PlacedAt)).ToList());
    }

    /// <summary>Every auction the signed-in user has bid on, with where they stand.</summary>
    public async Task<List<MyBidDto>> MyBidsAsync(CancellationToken ct)
    {
        var userId = currentUser.UserId ?? throw new NotFoundException("Not signed in.");
        var now = clock.GetUtcNow();

        var mine = await db.Bids.AsNoTracking()
            .Where(b => b.BidderId == userId)
            .GroupBy(b => b.AuctionId)
            .Select(g => new { AuctionId = g.Key, Highest = g.Max(b => b.Amount), Count = g.Count() })
            .ToListAsync(ct);

        var ids = mine.Select(m => m.AuctionId).ToList();
        var auctions = await db.Auctions.AsNoTracking().Where(a => ids.Contains(a.Id)).ToDictionaryAsync(a => a.Id, ct);

        return mine
            .Select(m =>
            {
                var auction = auctions[m.AuctionId];
                var state = auction.ClosedAt is not null
                    ? auction.WinnerId == userId ? MyBidState.Won : MyBidState.Lost
                    : auction.LeadingBidderId == userId ? MyBidState.Leading : MyBidState.Outbid;
                return new MyBidDto(auction.ToSummary(now), m.Highest, m.Count, state);
            })
            .OrderBy(m => m.State)
            .ThenBy(m => m.Auction.EndsAt)
            .ToList();
    }
}
