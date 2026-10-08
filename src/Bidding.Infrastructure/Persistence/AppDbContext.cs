using Bidding.Application.Common;
using Bidding.Domain.Auctions;
using Bidding.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Npgsql;

namespace Bidding.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IAppDbContext
{
    public DbSet<Auction> Auctions => Set<Auction>();
    public DbSet<Bid> Bids => Set<Bid>();
    public DbSet<User> Users => Set<User>();

    public void ClearTracking() => ChangeTracker.Clear();

    public bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}

internal sealed class AuctionConfiguration : IEntityTypeConfiguration<Auction>
{
    public void Configure(EntityTypeBuilder<Auction> b)
    {
        b.Property(a => a.Title).HasMaxLength(150);
        b.Property(a => a.Description).HasMaxLength(2000);
        b.Property(a => a.ContainerType).HasMaxLength(10);
        b.Property(a => a.Location).HasMaxLength(100);
        b.Property(a => a.Condition).HasMaxLength(100);
        b.Property(a => a.Currency).HasMaxLength(3).IsFixedLength();
        b.Property(a => a.StartingPrice).HasPrecision(12, 2);
        b.Property(a => a.MinIncrement).HasPrecision(12, 2);
        b.Property(a => a.ReservePrice).HasPrecision(12, 2);
        b.Property(a => a.CurrentPrice).HasPrecision(12, 2);
        b.Property(a => a.Outcome).HasConversion<string>().HasMaxLength(20);

        // uint + IsRowVersion = PostgreSQL's built-in xmin column. No extra column or trigger needed.
        b.Property(a => a.Version).IsRowVersion();

        b.HasOne<User>().WithMany().HasForeignKey(a => a.LeadingBidderId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(a => a.WinnerId).OnDelete(DeleteBehavior.Restrict);

        // "Live" and "due to close" both filter on these.
        b.HasIndex(a => new { a.ClosedAt, a.EndsAt });
        b.HasIndex(a => a.StartsAt);

        b.Ignore(a => a.MinimumNextBid);
        b.Ignore(a => a.ReserveMet);
    }
}

internal sealed class BidConfiguration : IEntityTypeConfiguration<Bid>
{
    public void Configure(EntityTypeBuilder<Bid> b)
    {
        b.Property(x => x.Amount).HasPrecision(12, 2);
        b.Property(x => x.IdempotencyKey).HasMaxLength(64);

        b.HasOne(x => x.Auction).WithMany().HasForeignKey(x => x.AuctionId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.BidderId).OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new { x.AuctionId, x.Amount }).IsDescending(false, true);
        // The database, not the code, guarantees a retried request can't create a second bid.
        b.HasIndex(x => new { x.BidderId, x.IdempotencyKey }).IsUnique().HasFilter("idempotency_key IS NOT NULL");
    }
}

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.Property(u => u.Email).HasMaxLength(200);
        b.Property(u => u.DisplayName).HasMaxLength(60);
        b.Property(u => u.PasswordHash).HasMaxLength(200);
        b.Property(u => u.Role).HasConversion<string>().HasMaxLength(20);
        b.HasIndex(u => u.Email).IsUnique();
    }
}
