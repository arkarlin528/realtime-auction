using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bidding.Application.Auctions;
using Bidding.Application.Auth;
using Bidding.Application.Common;
using Bidding.Domain.Users;
using Bidding.Infrastructure.Persistence;
using Bidding.Infrastructure.Seeding;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace Bidding.Api.IntegrationTests;

/// <summary>
/// Boots the real API against a throwaway PostgreSQL database, with a fake clock so auction
/// timing (anti-sniping, closing) can be tested exactly instead of with sleeps.
/// Set BIDDING_TEST_PG to point at another server (CI uses a postgres service container).
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string DemoPassword = "Integration-Test-Pass-1";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _connectionString;
    private int _userCounter;

    public FakeTimeProvider Clock { get; } = new(DateTimeOffset.UtcNow);

    public ApiFactory()
    {
        var server = Environment.GetEnvironmentVariable("BIDDING_TEST_PG")
                     ?? "Host=localhost;Port=5432;Username=auction_dev;Password=auction_dev_local";
        _connectionString = $"{server};Database=bidding_tests_{Guid.NewGuid():N}";
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration(config => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = _connectionString,
            ["Jwt:SigningKey"] = "integration-tests-signing-key-0123456789abcdef",
            ["Seed:DemoPassword"] = DemoPassword,
            ["Seed:Auctions"] = "false",
            ["Database:MigrateOnStartup"] = "true",
            ["DemoActivity:Enabled"] = "false",
        }));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
        });
    }

    public Task InitializeAsync()
    {
        _ = Services; // builds the host: migrate + seed
        return Task.CompletedTask;
    }

    public new async Task DisposeAsync()
    {
        await using (var scope = Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureDeletedAsync();
        }
        await base.DisposeAsync();
    }

    public HttpClient ClientWithToken(string token)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>Signs in a seeded account over HTTP (once per account; login is rate limited).</summary>
    public Task<string> TokenFor(string email) => _tokens.GetOrAdd(email, async e =>
    {
        var response = await CreateClient().PostAsJsonAsync("/api/auth/login", new LoginRequest(e, DemoPassword));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<LoginResponse>(Json))!.AccessToken;
    });

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Task<string>> _tokens = new();

    /// <summary>
    /// Creates a fresh bidder straight in the database and mints a token for them. Used by the
    /// concurrency tests, which need many distinct bidders (and registration is rate limited).
    /// </summary>
    public async Task<(int Id, string Token)> NewBidderAsync(string? name = null)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var n = Interlocked.Increment(ref _userCounter);
        var user = new User($"test-bidder-{n}-{Guid.NewGuid():N}@test.local", name ?? $"Test Bidder{n}", UserRole.Bidder);
        user.SetPasswordHash(hasher.Hash(user, DemoPassword));
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var (token, _) = scope.ServiceProvider.GetRequiredService<ITokenService>().CreateToken(user);
        return (user.Id, token);
    }

    /// <summary>Schedules an auction through the admin API, relative to the fake clock.</summary>
    public async Task<AuctionSummaryDto> NewAuctionAsync(TimeSpan? startsIn = null, TimeSpan? runsFor = null,
        decimal startingPrice = 1000m, decimal increment = 50m, decimal? reserve = null)
    {
        var admin = ClientWithToken(await TokenFor(DataSeeder.AdminEmail));
        var start = Clock.GetUtcNow() + (startsIn ?? TimeSpan.Zero);
        var response = await admin.PostAsJsonAsync("/api/auctions", new CreateAuctionRequest(
            "40ft High Cube, test", "Test container", "40HC", "Laem Chabang", "Cargo-worthy (CW)", 2019,
            startingPrice, increment, reserve, start, start + (runsFor ?? TimeSpan.FromMinutes(10))), Json);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuctionSummaryDto>(Json))!;
    }

    /// <summary>Runs the closer once, as the background job would every second.</summary>
    public async Task<int> CloseDueAuctionsAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AuctionLifecycle>().CloseDueAsync(CancellationToken.None);
    }

    /// <summary>A SignalR connection over the in-memory test server (long polling: TestServer has no real sockets).</summary>
    public HubConnection Hub(string? token = null) => new HubConnectionBuilder()
        .WithUrl(new Uri(Server.BaseAddress, "hubs/auctions"), options =>
        {
            options.HttpMessageHandlerFactory = _ => Server.CreateHandler();
            options.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.LongPolling;
            if (token is not null)
                options.AccessTokenProvider = () => Task.FromResult<string?>(token);
        })
        .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
        .Build();
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}

internal static class HttpExtensions
{
    public static async Task<T> ReadAs<T>(this HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>(ApiFactory.Json))
        ?? throw new InvalidOperationException("Empty response body.");

    public static Task<HttpResponseMessage> Bid(this HttpClient client, int auctionId, decimal amount, string? idempotencyKey = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/auctions/{auctionId}/bids")
        {
            Content = JsonContent.Create(new PlaceBidRequest(amount), options: ApiFactory.Json),
        };
        if (idempotencyKey is not null)
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        return client.SendAsync(request);
    }
}
