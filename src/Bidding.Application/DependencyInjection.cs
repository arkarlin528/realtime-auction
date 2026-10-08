using Bidding.Application.Auctions;
using Bidding.Application.Auth;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bidding.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<AuthService>();
        services.AddScoped<AuctionQueries>();
        services.AddScoped<BiddingService>();
        services.AddScoped<AuctionLifecycle>();
        services.AddValidatorsFromAssemblyContaining<PlaceBidRequestValidator>(includeInternalTypes: true);
        // TryAdd so tests can register a controllable clock first.
        services.TryAddSingleton(TimeProvider.System);
        return services;
    }
}
