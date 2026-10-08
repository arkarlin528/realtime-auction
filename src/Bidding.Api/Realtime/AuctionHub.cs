using Bidding.Application.Auctions;
using Bidding.Application.Common;
using Microsoft.AspNetCore.SignalR;

namespace Bidding.Api.Realtime;

/// <summary>Strongly typed client contract: a renamed method is a compile error, not a silent no-op.</summary>
public interface IAuctionClient
{
    Task BidPlaced(BidPlacedMessage message);
    Task Outbid(OutbidMessage message);
    Task AuctionClosed(AuctionClosedMessage message);
    Task AuctionCreated(int auctionId);
}

/// <summary>
/// Watching is public (prices are public on any auction site), so anonymous connections may join
/// rooms. Bidding happens over REST with a JWT. If the connection carries a token, the user also
/// gets private "you've been outbid" messages through Clients.User.
/// </summary>
public sealed class AuctionHub : Hub<IAuctionClient>
{
    public const string Path = "/hubs/auctions";
    public const string Lobby = "lobby";

    public static string Room(int auctionId) => $"auction-{auctionId}";

    public Task JoinAuction(int auctionId) => Groups.AddToGroupAsync(Context.ConnectionId, Room(auctionId));

    public Task LeaveAuction(int auctionId) => Groups.RemoveFromGroupAsync(Context.ConnectionId, Room(auctionId));

    public Task JoinLobby() => Groups.AddToGroupAsync(Context.ConnectionId, Lobby);

    public Task LeaveLobby() => Groups.RemoveFromGroupAsync(Context.ConnectionId, Lobby);
}

internal sealed class SignalRAuctionNotifier(IHubContext<AuctionHub, IAuctionClient> hub) : IAuctionNotifier
{
    public Task BidPlacedAsync(BidPlacedMessage message, CancellationToken ct) =>
        hub.Clients.Groups(AuctionHub.Room(message.AuctionId), AuctionHub.Lobby).BidPlaced(message);

    public Task OutbidAsync(int userId, OutbidMessage message, CancellationToken ct) =>
        hub.Clients.User(userId.ToString()).Outbid(message);

    public Task AuctionClosedAsync(AuctionClosedMessage message, CancellationToken ct) =>
        hub.Clients.Groups(AuctionHub.Room(message.AuctionId), AuctionHub.Lobby).AuctionClosed(message);

    public Task AuctionCreatedAsync(int auctionId, CancellationToken ct) =>
        hub.Clients.Group(AuctionHub.Lobby).AuctionCreated(auctionId);
}
