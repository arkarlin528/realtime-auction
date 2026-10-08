using Bidding.Application.Auctions;
using Bidding.Domain.Auctions;
using Bidding.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Bidding.Application.Common;

public interface IAppDbContext
{
    DbSet<Auction> Auctions { get; }
    DbSet<Bid> Bids { get; }
    DbSet<User> Users { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>Forget tracked entities, so a retry reloads fresh rows instead of reusing stale ones.</summary>
    void ClearTracking();

    /// <summary>True when the save failed on a unique index, e.g. a replayed idempotency key. Provider-specific, so it lives in Infrastructure.</summary>
    bool IsUniqueViolation(DbUpdateException exception);
}

public interface ICurrentUser
{
    int? UserId { get; }
    UserRole? Role { get; }
}

/// <summary>Realtime fan-out, implemented with SignalR in the API.</summary>
public interface IAuctionNotifier
{
    Task BidPlacedAsync(BidPlacedMessage message, CancellationToken ct);
    Task OutbidAsync(int userId, OutbidMessage message, CancellationToken ct);
    Task AuctionClosedAsync(AuctionClosedMessage message, CancellationToken ct);
    Task AuctionCreatedAsync(int auctionId, CancellationToken ct);
}

public interface IPasswordHasher
{
    string Hash(User user, string password);
    bool Verify(User user, string password);
}

public interface ITokenService
{
    (string Token, DateTimeOffset ExpiresAt) CreateToken(User user);
}

/// <summary>Mapped to HTTP 404.</summary>
public sealed class NotFoundException(string message) : Exception(message);

/// <summary>Mapped to HTTP 409: the request lost a race and should be retried by the user with fresh data.</summary>
public sealed class ConflictException(string message) : Exception(message);
