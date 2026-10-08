using Bidding.Application.Common;
using Bidding.Domain.Auctions;
using Bidding.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bidding.Application.Auctions;

/// <summary>Creating auctions and settling them when time runs out.</summary>
public sealed class AuctionLifecycle(
    IAppDbContext db,
    IAuctionNotifier notifier,
    TimeProvider clock,
    ILogger<AuctionLifecycle> logger)
{
    public async Task<AuctionSummaryDto> CreateAsync(CreateAuctionRequest r, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var auction = Auction.Create(r.Title, r.Description, r.ContainerType, r.Location, r.Condition, r.YearBuilt,
            r.StartingPrice, r.MinIncrement, r.ReservePrice, r.StartsAt, r.EndsAt, now);

        db.Auctions.Add(auction);
        await db.SaveChangesAsync(ct);
        await notifier.AuctionCreatedAsync(auction.Id, ct);
        return auction.ToSummary(now);
    }

    /// <summary>
    /// Closes every auction whose end time has passed. Called every second by a background job.
    /// A bid that sneaks in at the last moment extends EndsAt and bumps the row version, so the
    /// close either sees the extension or loses the version check and simply runs again next tick.
    /// </summary>
    public async Task<int> CloseDueAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var dueIds = await db.Auctions.AsNoTracking()
            .Where(a => a.ClosedAt == null && a.EndsAt <= now)
            .Select(a => a.Id)
            .ToListAsync(ct);

        var closed = 0;
        foreach (var id in dueIds)
        {
            try
            {
                if (await CloseOneAsync(id, ct))
                    closed++;
            }
            catch (DbUpdateConcurrencyException)
            {
                logger.LogDebug("Auction {AuctionId} changed while closing; will retry next tick", id);
            }
            finally
            {
                db.ClearTracking();
            }
        }

        return closed;
    }

    private async Task<bool> CloseOneAsync(int id, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var auction = await db.Auctions.FirstAsync(a => a.Id == id, ct);
        if (auction.StatusAt(now) != AuctionStatus.Ending || !auction.Close(now))
            return false;

        await db.SaveChangesAsync(ct);

        string? winnerName = null;
        if (auction.WinnerId is { } winnerId)
            winnerName = User.PublicName(await db.Users.Where(u => u.Id == winnerId).Select(u => u.DisplayName).FirstAsync(ct));

        await notifier.AuctionClosedAsync(new AuctionClosedMessage(auction.Id, auction.Title, auction.Outcome!.Value,
            auction.CurrentPrice, auction.WinnerId, winnerName, now), ct);
        logger.LogInformation("Auction {AuctionId} closed: {Outcome} at {Price}", id, auction.Outcome, auction.CurrentPrice);
        return true;
    }
}
