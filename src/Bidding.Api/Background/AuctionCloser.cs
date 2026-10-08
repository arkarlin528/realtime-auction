using Bidding.Application.Auctions;
using Microsoft.Extensions.Options;

namespace Bidding.Api.Background;

public sealed class CloserOptions
{
    public const string Section = "Closer";

    public int IntervalMilliseconds { get; set; } = 1000;
}

/// <summary>
/// Settles auctions whose time is up. Runs every second; closing is idempotent, so running it on
/// several API instances at once is safe (the row version lets only one close win).
/// </summary>
internal sealed class AuctionCloser(
    IServiceScopeFactory scopes,
    IOptions<CloserOptions> options,
    TimeProvider clock,
    ILogger<AuctionCloser> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(options.Value.IntervalMilliseconds), clock);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<AuctionLifecycle>().CloseDueAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Closing due auctions failed; retrying next tick");
            }
        }
    }
}
