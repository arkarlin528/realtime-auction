using Bidding.Application.Auctions;
using Bidding.Application.Common;
using Bidding.Domain.Auctions;
using Bidding.Domain.Common;
using Bidding.Infrastructure.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Bidding.Api.Background;

public sealed class DemoActivityOptions
{
    public const string Section = "DemoActivity";

    public bool Enabled { get; set; }
    public int IntervalSeconds { get; set; } = 4;
    /// <summary>Keep at least this many auctions live or about to start.</summary>
    public int MinOpenAuctions { get; set; } = 10;
}

/// <summary>
/// Keeps the public demo lively: simulated bidders bid on live auctions (more eagerly near the end,
/// which shows off anti-sniping), and new auctions are scheduled when the supply runs low.
/// Everything goes through <see cref="BiddingService"/> and <see cref="AuctionLifecycle"/>, so the
/// bots obey exactly the same rules, concurrency checks and notifications as real users.
/// </summary>
internal sealed class DemoActivity(
    IServiceScopeFactory scopes,
    IOptions<DemoActivityOptions> options,
    TimeProvider clock,
    ILogger<DemoActivity> logger) : BackgroundService
{
    private readonly Random _random = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
            return;

        logger.LogInformation("Demo bidders active every {Seconds}s", settings.IntervalSeconds);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(settings.IntervalSeconds), clock);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await TopUpAuctionsAsync(scope.ServiceProvider, settings, stoppingToken);
                await BotBidAsync(scope.ServiceProvider, stoppingToken);
            }
            catch (DomainException ex)
            {
                logger.LogDebug("Bot bid rejected: {Reason}", ex.Message); // e.g. a human bid first
            }
            catch (ConflictException)
            {
                // The auction was busy; try again next tick.
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Demo activity step failed");
            }
        }
    }

    private async Task BotBidAsync(IServiceProvider services, CancellationToken ct)
    {
        var db = services.GetRequiredService<IAppDbContext>();
        var now = clock.GetUtcNow();

        var live = await db.Auctions.AsNoTracking()
            .Where(a => a.ClosedAt == null && a.StartsAt <= now && a.EndsAt > now)
            .ToListAsync(ct);
        if (live.Count == 0)
            return;

        // Prefer auctions that are about to end, like real bidders do.
        var auction = live.OrderBy(a => a.EndsAt).ElementAt((int)(Math.Pow(_random.NextDouble(), 2) * live.Count));
        var secondsLeft = (auction.EndsAt - now).TotalSeconds;
        var overtime = auction.EndsAt - auction.ScheduledEndsAt;
        var pricey = auction.CurrentPrice > auction.StartingPrice * 2.2m;

        var chance = secondsLeft < 40 ? 0.7 : 0.35;
        if (pricey) chance *= 0.25;
        if (overtime > TimeSpan.FromMinutes(2)) chance = 0; // let bidding wars end eventually
        if (_random.NextDouble() > chance)
            return;

        var botIds = await db.Users.AsNoTracking().Where(u => u.IsBot).Select(u => u.Id).ToListAsync(ct);
        var candidates = botIds.Where(id => id != auction.LeadingBidderId).ToList();
        if (candidates.Count == 0)
            return;

        var amount = auction.MinimumNextBid + auction.MinIncrement * _random.Next(0, 3);
        await services.GetRequiredService<BiddingService>()
            .PlaceBidAsync(auction.Id, candidates[_random.Next(candidates.Count)], amount, null, ct);
    }

    private async Task TopUpAuctionsAsync(IServiceProvider services, DemoActivityOptions settings, CancellationToken ct)
    {
        var db = services.GetRequiredService<IAppDbContext>();
        var now = clock.GetUtcNow();
        var open = await db.Auctions.CountAsync(a => a.ClosedAt == null && a.EndsAt > now, ct);
        if (open >= settings.MinOpenAuctions)
            return;

        var template = DataSeeder.NewAuction(_random, now, now.AddMinutes(1), now);
        var start = now.AddMinutes(1 + _random.Next(0, 3));
        await services.GetRequiredService<AuctionLifecycle>().CreateAsync(new CreateAuctionRequest(
            template.Title, template.Description, template.ContainerType, template.Location, template.Condition,
            template.YearBuilt, template.StartingPrice, template.MinIncrement, template.ReservePrice,
            start, start.AddMinutes(6 + _random.Next(0, 10))), ct);
    }
}
