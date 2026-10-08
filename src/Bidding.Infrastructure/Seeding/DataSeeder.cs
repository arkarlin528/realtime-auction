using System.Security.Cryptography;
using Bidding.Application.Common;
using Bidding.Domain.Auctions;
using Bidding.Domain.Users;
using Bidding.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bidding.Infrastructure.Seeding;

public sealed class SeedOptions
{
    public const string Section = "Seed";

    /// <summary>Password for the demo accounts. Comes from configuration, never hard-coded.</summary>
    public string? DemoPassword { get; set; }

    public int RandomSeed { get; set; } = 7;

    /// <summary>Tests turn this off to start from an empty auction list.</summary>
    public bool Auctions { get; set; } = true;
}

/// <summary>
/// Fills an empty database: demo users, simulated "bot" bidders, and auctions in every state
/// (scheduled, live with bid history, closed with each outcome), all relative to "now".
/// Bids are placed through <see cref="Auction.PlaceBid"/>, so seeded history obeys the real rules.
/// </summary>
public sealed class DataSeeder(
    AppDbContext db,
    IPasswordHasher hasher,
    TimeProvider clock,
    IOptions<SeedOptions> options,
    ILogger<DataSeeder> logger)
{
    public const string AdminEmail = "admin@demo.test";
    public const string BidderEmail = "bidder@demo.test";
    public const string Bidder2Email = "bidder2@demo.test";

    // All names are invented.
    private static readonly string[] BotNames =
    [
        "Somchai Prasert", "Nguyen Thi Lan", "Aung Ko Ko", "Siti Rahman", "Lim Wei Jie",
        "Kanya Srisuk", "Budi Santoso", "Hla Myint",
    ];

    public static readonly (string Type, string Label)[] ContainerTypes =
    [
        ("20GP", "20ft Standard"), ("40GP", "40ft Standard"), ("40HC", "40ft High Cube"),
        ("20RF", "20ft Reefer"), ("40RF", "40ft Reefer"), ("45HC", "45ft High Cube"),
    ];

    public static readonly string[] Locations =
        ["Laem Chabang", "Bangkok", "Singapore", "Port Klang", "Ho Chi Minh City", "Yangon", "Jakarta"];

    public static readonly string[] Conditions =
        ["New (one-trip)", "Cargo-worthy (CW)", "Wind & watertight (WWT)", "As-is"];

    private readonly SeedOptions _options = options.Value;

    public async Task MigrateAndSeedAsync(CancellationToken ct = default)
    {
        await db.Database.MigrateAsync(ct);
        if (await db.Users.AnyAsync(ct))
            return;

        logger.LogInformation("Empty database, seeding demo data");
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var bots = await SeedUsersAsync(ct);
            if (_options.Auctions)
                await SeedAuctionsAsync(bots, ct);
            await transaction.CommitAsync(ct);
        });
    }

    private async Task<List<User>> SeedUsersAsync(CancellationToken ct)
    {
        var humans = new List<User>();
        if (string.IsNullOrWhiteSpace(_options.DemoPassword))
        {
            logger.LogWarning("Seed:DemoPassword is not set, so no demo logins were created");
        }
        else
        {
            humans.Add(new User(AdminEmail, "Demo Admin", UserRole.Admin));
            humans.Add(new User(BidderEmail, "Mali Kittisak", UserRole.Bidder));
            humans.Add(new User(Bidder2Email, "Tun Aung", UserRole.Bidder));
            foreach (var user in humans)
                user.SetPasswordHash(hasher.Hash(user, _options.DemoPassword));
        }

        // Bots can't log in: their password is random and thrown away, and login skips bots anyway.
        var bots = BotNames.Select((name, i) => new User($"bot{i + 1}@bots.demo.test", name, UserRole.Bidder, isBot: true)).ToList();
        foreach (var bot in bots)
            bot.SetPasswordHash(hasher.Hash(bot, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))));

        db.Users.AddRange(humans);
        db.Users.AddRange(bots);
        await db.SaveChangesAsync(ct);
        return bots;
    }

    private async Task SeedAuctionsAsync(List<User> bots, CancellationToken ct)
    {
        var random = new Random(_options.RandomSeed);
        var now = clock.GetUtcNow();

        // Live: started a while ago, ending over the next half hour.
        for (var i = 0; i < 8; i++)
        {
            var start = now.AddMinutes(-random.Next(5, 90));
            var end = now.AddMinutes(3 + i * 3 + random.Next(0, 3)).AddSeconds(random.Next(0, 59));
            AddWithHistory(NewAuction(random, start, end, start.AddMinutes(-10)), random, bots, start, now, bidCount: random.Next(0, 9));
        }

        // Scheduled: open soon.
        for (var i = 0; i < 4; i++)
        {
            var start = now.AddMinutes(4 + i * 7);
            db.Auctions.Add(NewAuction(random, start, start.AddMinutes(15 + random.Next(0, 15)), now));
        }

        // Closed: yesterday's results, with every outcome represented.
        for (var i = 0; i < 8; i++)
        {
            var start = now.AddHours(-random.Next(6, 48));
            var end = start.AddMinutes(random.Next(20, 90));
            var auction = NewAuction(random, start, end, start.AddMinutes(-10), forceReserve: i % 4 == 0);
            AddWithHistory(auction, random, bots, start, end, bidCount: i % 5 == 4 ? 0 : random.Next(2, 12));
            auction.Close(auction.EndsAt); // EndsAt, not end: a late seeded bid may have extended it
        }

        await db.SaveChangesAsync(ct);
    }

    public static Auction NewAuction(Random random, DateTimeOffset start, DateTimeOffset end, DateTimeOffset createdAt,
        bool forceReserve = false)
    {
        var (type, label) = ContainerTypes[random.Next(ContainerTypes.Length)];
        var location = Locations[random.Next(Locations.Length)];
        var condition = Conditions[random.Next(Conditions.Length)];
        var year = 2008 + random.Next(0, 17);
        var basePrice = type switch
        {
            "20RF" or "40RF" => 4200m,
            "40HC" or "45HC" => 2400m,
            "40GP" => 2000m,
            _ => 1300m,
        };
        var starting = Math.Round(basePrice * (decimal)(0.6 + random.NextDouble() * 0.3) / 50m) * 50m;
        var increment = starting >= 2000 ? 50m : 25m;
        decimal? reserve = forceReserve || random.NextDouble() < 0.3
            ? Math.Round(starting * (forceReserve ? 2.5m : 1.25m) / 50m) * 50m
            : null;

        return Auction.Create(
            $"{label}, {ShortCondition(condition)}, {location}",
            $"{label} container ({type}) built {year}, located at {location} depot. Condition: {condition}. " +
            "Inspection photos and CSC plate available on request. Buyer collects within 14 days.",
            type, location, condition, year, starting, increment, reserve, start, end, createdAt);
    }

    private static string ShortCondition(string condition) => condition switch
    {
        "New (one-trip)" => "one-trip",
        "Cargo-worthy (CW)" => "cargo-worthy",
        "Wind & watertight (WWT)" => "wind & watertight",
        _ => condition.ToLowerInvariant(),
    };

    private void AddWithHistory(Auction auction, Random random, List<User> bots, DateTimeOffset from, DateTimeOffset until,
        int bidCount)
    {
        db.Auctions.Add(auction);
        if (bidCount == 0)
            return;

        var step = (until - from) / (bidCount + 1);
        var at = from;
        int? leader = null;
        for (var i = 0; i < bidCount; i++)
        {
            at += step;
            var bidder = bots.Where(b => b.Id != leader).ElementAt(random.Next(bots.Count - 1));
            var amount = auction.MinimumNextBid + auction.MinIncrement * random.Next(0, 3);
            db.Bids.Add(auction.PlaceBid(bidder.Id, amount, at).Bid);
            leader = bidder.Id;
        }
    }
}
