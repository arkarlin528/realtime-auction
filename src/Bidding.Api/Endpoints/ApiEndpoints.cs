using Bidding.Api.Auth;
using Bidding.Api.Infrastructure;
using Bidding.Application.Auctions;
using Bidding.Application.Auth;
using Bidding.Application.Common;
using Microsoft.AspNetCore.Mvc;

namespace Bidding.Api.Endpoints;

public static class ApiEndpoints
{
    public const string AuthRateLimit = "auth";
    public const string BidRateLimit = "bids";
    public const string IdempotencyHeader = "Idempotency-Key";

    public static void MapApiEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/time", (TimeProvider clock) => new { now = clock.GetUtcNow() })
            .WithTags("Clock")
            .WithSummary("Server time. Clients use it to line countdowns up with the server, which decides when an auction ends.");

        var auth = api.MapGroup("/auth").WithTags("Auth");
        auth.MapPost("/login", async (LoginRequest request, AuthService service, CancellationToken ct) =>
                await service.LoginAsync(request, ct) is { } response
                    ? Results.Ok(response)
                    : Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Invalid email or password."))
            .Validate<LoginRequest>()
            .RequireRateLimiting(AuthRateLimit)
            .Produces<LoginResponse>();
        auth.MapPost("/register", (RegisterRequest request, AuthService service, CancellationToken ct) =>
                service.RegisterAsync(request, ct))
            .Validate<RegisterRequest>()
            .RequireRateLimiting(AuthRateLimit)
            .WithSummary("Create a bidder account")
            .ProducesProblem(StatusCodes.Status409Conflict);
        auth.MapGet("/me", (AuthService service, CancellationToken ct) => service.MeAsync(ct)).RequireAuthorization();

        var auctions = api.MapGroup("/auctions").WithTags("Auctions");
        auctions.MapGet("/", (AuctionQueries q, CancellationToken ct, AuctionListFilter filter = AuctionListFilter.Live) =>
                q.ListAsync(filter, ct))
            .WithSummary("Live (default), Scheduled or Closed auctions. Public.");
        auctions.MapGet("/{id:int}", (int id, AuctionQueries q, CancellationToken ct) => q.GetAsync(id, ct))
            .WithSummary("One auction with its top bids. Public.")
            .ProducesProblem(StatusCodes.Status404NotFound);
        auctions.MapPost("/", async (CreateAuctionRequest request, AuctionLifecycle lifecycle, CancellationToken ct) =>
            {
                var created = await lifecycle.CreateAsync(request, ct);
                return Results.Created($"/api/auctions/{created.Id}", created);
            })
            .Validate<CreateAuctionRequest>()
            .RequireAuthorization(Policies.Admin)
            .WithSummary("Schedule a new auction (admin)");

        auctions.MapPost("/{id:int}/bids", async (
                int id,
                PlaceBidRequest request,
                [FromHeader(Name = IdempotencyHeader)] string? idempotencyKey,
                ICurrentUser user,
                BiddingService bidding,
                CancellationToken ct) =>
            {
                if (idempotencyKey is { Length: > 64 })
                    return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: $"{IdempotencyHeader} is limited to 64 characters.");

                var result = await bidding.PlaceBidAsync(id, user.UserId!.Value, request.Amount, idempotencyKey, ct);
                return Results.Ok(result);
            })
            .Validate<PlaceBidRequest>()
            .RequireAuthorization()
            .RequireRateLimiting(BidRateLimit)
            .WithSummary($"Place a bid. Send an {IdempotencyHeader} header so a retried request can never bid twice.")
            .Produces<BidResultDto>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        api.MapGet("/me/bids", (AuctionQueries q, CancellationToken ct) => q.MyBidsAsync(ct))
            .WithTags("Auctions")
            .RequireAuthorization()
            .WithSummary("Auctions I've bid on: leading, outbid, won or lost");
    }
}
