using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Bidding.Api.Auth;
using Bidding.Api.Background;
using Bidding.Api.Endpoints;
using Bidding.Api.Infrastructure;
using Bidding.Api.Realtime;
using Bidding.Application;
using Bidding.Application.Common;
using Bidding.Infrastructure;
using Bidding.Infrastructure.Persistence;
using Bidding.Infrastructure.Seeding;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Scalar.AspNetCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, logger) => logger
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console());

// Layers
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddSingleton<ITokenService, JwtTokenService>();
builder.Services.AddSingleton<IAuctionNotifier, SignalRAuctionNotifier>();

// Auth (options bound lazily so environment variables and tests can override them)
builder.Services.AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection(JwtOptions.Section))
    .Validate(o => o.SigningKey.Length >= 32, "Jwt:SigningKey must be at least 32 characters (Jwt__SigningKey).")
    .ValidateOnStart();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>>((options, jwtOptions) =>
    {
        var jwt = jwtOptions.Value;
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = jwt.SecurityKey(),
            RoleClaimType = ClaimTypes.Role,
            NameClaimType = JwtRegisteredClaimNames.Name,
            ClockSkew = TimeSpan.FromSeconds(30),
        };
        // WebSockets can't send headers from the browser, so SignalR passes the token in the query string.
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var token = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(token) && context.HttpContext.Request.Path.StartsWithSegments(AuctionHub.Path))
                    context.Token = token;
                return Task.CompletedTask;
            },
        };
    });
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(Policies.Admin, policy => policy.RequireRole(nameof(Bidding.Domain.Users.UserRole.Admin)));

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(ApiEndpoints.AuthRateLimit, context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
    // Per bidder, not per IP: stops a script from hammering one auction while allowing fast human bidding.
    options.AddPolicy(ApiEndpoints.BidRateLimit, context => RateLimitPartition.GetTokenBucketLimiter(
        context.User.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? "anonymous",
        _ => new TokenBucketRateLimiterOptions
        {
            TokenLimit = 10, TokensPerPeriod = 5, ReplenishmentPeriod = TimeSpan.FromSeconds(1), QueueLimit = 0,
        }));
});

// Realtime. With Redis configured, several API instances share SignalR groups (scale-out).
var signalR = builder.Services.AddSignalR()
    .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
var redis = builder.Configuration["Redis:ConnectionString"];
if (!string.IsNullOrWhiteSpace(redis))
    signalR.AddStackExchangeRedis(redis, o => o.Configuration.ChannelPrefix = StackExchange.Redis.RedisChannel.Literal("bidding"));
builder.Services.AddSingleton<IUserIdProvider, SubClaimUserIdProvider>();

// HTTP plumbing
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    // Caddy -> nginx -> api: the client IP is two hops back. The api port is only reachable inside Docker.
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 2;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ProblemExceptionHandler>();
builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>("database");

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

// Background work
builder.Services.Configure<CloserOptions>(builder.Configuration.GetSection(CloserOptions.Section));
builder.Services.Configure<DemoActivityOptions>(builder.Configuration.GetSection(DemoActivityOptions.Section));
builder.Services.AddHostedService<AuctionCloser>();
builder.Services.AddHostedService<DemoActivity>();

// API docs: Swashbuckle generates OpenAPI, Scalar renders it at /docs
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Container Auction API",
        Version = "v1",
        Description = "Realtime auctions for used shipping containers. Browse anonymously; log in to bid. Live updates on /hubs/auctions (SignalR).",
    });
    var bearer = new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" },
    };
    options.AddSecurityDefinition("Bearer", bearer);
    options.AddSecurityRequirement(new OpenApiSecurityRequirement { [bearer] = [] });
});

var app = builder.Build();

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseSerilogRequestLogging();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.UseSwagger();
app.MapScalarApiReference("/docs", options =>
{
    options.Title = "Container Auction API";
    options.OpenApiRoutePattern = "/swagger/{documentName}/swagger.json";
});
app.MapGet("/", () => Results.Redirect("/docs")).ExcludeFromDescription();

app.MapApiEndpoints();
app.MapHub<AuctionHub>(AuctionHub.Path);
app.MapHealthChecks("/health");

// With several API replicas, migrations must run exactly once: docker-compose runs this image once
// with Database__MigrateOnly=true before starting the replicas (EF Core 8 has no migration lock).
var migrateOnly = app.Configuration.GetValue("Database:MigrateOnly", false);
if (migrateOnly || app.Configuration.GetValue("Database:MigrateOnStartup", false))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<DataSeeder>().MigrateAndSeedAsync();
    if (migrateOnly)
        return;
}

app.Run();

/// <summary>Exposed for WebApplicationFactory in the integration tests.</summary>
public partial class Program;
