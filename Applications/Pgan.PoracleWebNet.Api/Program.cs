using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Pgan.PoracleWebNet.Api.Configuration;

var builder = WebApplication.CreateBuilder(args);

// Load .env file from the working directory (if present).
// This lets both Docker and standalone users configure via a single .env file at the project root.
// Docker Compose loads .env natively; this covers the standalone (dotnet run / dotnet dll) case.
var envFile = Path.Combine(Directory.GetCurrentDirectory(), ".env");
if (File.Exists(envFile))
{
    foreach (var line in File.ReadAllLines(envFile))
    {
        var trimmed = line.Trim();
        if (trimmed.Length == 0 || trimmed.StartsWith('#'))
        {
            continue;
        }

        var eqIndex = trimmed.IndexOf('=');
        if (eqIndex <= 0)
        {
            continue;
        }

        var key = trimmed[..eqIndex].Trim();
        var value = trimmed[(eqIndex + 1)..].Trim();

        // Don't override variables that are already set in the environment
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
        {
            Environment.SetEnvironmentVariable(key, value);
        }
    }

    // Reload configuration so builder.Configuration picks up the new env vars
    builder.Configuration.AddEnvironmentVariables();
}

// Bridge short env var names (from .env) to .NET's __ convention.
// Docker Compose does this translation in docker-compose.yml; this makes the same .env work standalone.
MapEnvVar("JWT_SECRET", "Jwt__Secret");

// Named in the #583 changelog entry as the way to declare trusted proxies, and never bridged -- so the
// documented escape hatch did nothing and an instance behind a real proxy had no way to opt in. See #596.
MapEnvVar("PROXY_KNOWN_PROXIES", "Proxy__KnownProxies");
MapEnvVar("PROXY_KNOWN_NETWORKS", "Proxy__KnownNetworks");
MapEnvVar("JWT_ISSUER", "Jwt__Issuer", "PoracleWeb");
MapEnvVar("JWT_AUDIENCE", "Jwt__Audience", "PoracleWeb.App");
MapEnvVar("DISCORD_CLIENT_ID", "Discord__ClientId");
MapEnvVar("DISCORD_CLIENT_SECRET", "Discord__ClientSecret");
MapEnvVar("DISCORD_BOT_TOKEN", "Discord__BotToken");
MapEnvVar("DISCORD_GUILD_ID", "Discord__GuildId");
MapEnvVar("DISCORD_GEOFENCE_FORUM_CHANNEL_ID", "Discord__GeofenceForumChannelId");
MapEnvVar("PUBLIC_URL", "Site__PublicUrl");
MapEnvVar("TELEGRAM_ENABLED", "Telegram__Enabled");
MapEnvVar("TELEGRAM_BOT_TOKEN", "Telegram__BotToken");
MapEnvVar("TELEGRAM_BOT_USERNAME", "Telegram__BotUsername");
MapEnvVar("OIDC_ENABLED", "Oidc__Enabled");
MapEnvVar("OIDC_PROVIDER_NAME", "Oidc__ProviderName");
MapEnvVar("OIDC_AUTHORIZATION_URL", "Oidc__AuthorizationUrl");
MapEnvVar("OIDC_TOKEN_URL", "Oidc__TokenUrl");
MapEnvVar("OIDC_END_SESSION_URL", "Oidc__EndSessionUrl");
MapEnvVar("OIDC_USERINFO_URL", "Oidc__UserInfoUrl");
MapEnvVar("OIDC_CLIENT_ID", "Oidc__ClientId");
MapEnvVar("OIDC_CLIENT_SECRET", "Oidc__ClientSecret");
MapEnvVar("OIDC_SCOPES", "Oidc__Scopes");
MapEnvVar("OIDC_IDENTITY_CLAIM", "Oidc__IdentityClaim");
MapEnvVar("OIDC_USERNAME_CLAIM", "Oidc__UsernameClaim");
MapEnvVar("OIDC_AVATAR_CLAIM", "Oidc__AvatarClaim");
MapEnvVar("OIDC_IDENTITY_TYPE", "Oidc__IdentityType");
MapEnvVar("OIDC_USE_PKCE", "Oidc__UsePkce");
// Refresh-token consumption (opt-in, default off). When on, PoracleWeb brokers the provider's
// refresh token server-side for silent renewal + revocation propagation. Provider-agnostic.
MapEnvVar("OIDC_USE_REFRESH_TOKENS", "Oidc__UseRefreshTokens");
MapEnvVar("OIDC_ACCESS_TOKEN_MINUTES", "Oidc__AccessTokenMinutes");
MapEnvVar("OIDC_REFRESH_TOKEN_LIFETIME_DAYS", "Oidc__RefreshTokenLifetimeDays");
MapEnvVar("OIDC_SESSION_REVOKED_RETENTION_DAYS", "Oidc__RevokedRetentionDays");
MapEnvVar("OIDC_OFFLINE_ACCESS_SCOPE", "Oidc__OfflineAccessScope");
MapEnvVar("OIDC_TOKEN_AUTH_METHOD", "Oidc__TokenEndpointAuthMethod");
// Break-glass: forces the local login page regardless of the OIDC sign-in mode. Recovery
// path when an admin switches to OIDC against a broken/unreachable provider and gets locked out.
MapEnvVar("AUTH_FORCE_LOCAL", "Auth__ForceLocal");
MapEnvVar("PORACLE_API_ADDRESS", "Poracle__ApiAddress");
MapEnvVar("PORACLE_API_SECRET", "Poracle__ApiSecret");
MapEnvVar("PORACLE_ADMIN_IDS", "Poracle__AdminIds");
MapEnvVar("KOJI_API_ADDRESS", "Koji__ApiAddress");
MapEnvVar("KOJI_BEARER_TOKEN", "Koji__BearerToken");
MapEnvVar("KOJI_PROJECT_ID", "Koji__ProjectId");
MapEnvVar("KOJI_PROJECT_NAME", "Koji__ProjectName");
MapEnvVar("GOLBAT_API_ADDRESS", "Golbat__ApiAddress");
MapEnvVar("GOLBAT_API_SECRET", "Golbat__ApiSecret");
MapEnvVar("CORS_ORIGIN", "Cors__AllowedOrigins__0");
MapEnvVar("SCANNER_DB_CONNECTION", "ConnectionStrings__ScannerDb");

// Auto-compose MySQL connection strings from short env vars (DB_HOST, DB_PORT, etc.)
// so the same .env works for both Docker Compose and standalone mode.
ComposeConnectionString("PoracleDb", "DB_HOST", "DB_PORT", "DB_NAME", "DB_USER", "DB_PASSWORD", "poracle");
ComposeConnectionString("PoracleWebDb", "WEB_DB_HOST", "WEB_DB_PORT", "WEB_DB_NAME", "WEB_DB_USER", "WEB_DB_PASSWORD", "poracle_web");

// Auto-infer TELEGRAM_ENABLED=true when bot credentials are both set but Enabled was not
// explicitly configured. Prevents the common first-time-setup mistake of setting bot token
// and username but forgetting TELEGRAM_ENABLED=true, which silently hides the Telegram button.
var telegramEnabled = Environment.GetEnvironmentVariable("Telegram__Enabled");
var telegramToken = Environment.GetEnvironmentVariable("Telegram__BotToken");
var telegramUsername = Environment.GetEnvironmentVariable("Telegram__BotUsername");
if (string.IsNullOrEmpty(telegramEnabled)
    && !string.IsNullOrEmpty(telegramToken)
    && !string.IsNullOrEmpty(telegramUsername))
{
    Environment.SetEnvironmentVariable("Telegram__Enabled", "true");
}

// Auto-infer OIDC__Enabled=true when the full provider config is present but Enabled was
// not explicitly set — same first-time-setup safeguard as Telegram above.
var oidcEnabled = Environment.GetEnvironmentVariable("Oidc__Enabled");
if (string.IsNullOrEmpty(oidcEnabled)
    && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("Oidc__ClientId"))
    && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("Oidc__AuthorizationUrl"))
    && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("Oidc__TokenUrl"))
    && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("Oidc__UserInfoUrl")))
{
    Environment.SetEnvironmentVariable("Oidc__Enabled", "true");
}

// Reload configuration after env var bridging
builder.Configuration.AddEnvironmentVariables();

// Configurable port — checked in order: ASPNETCORE_URLS (Docker), PORT env var, Server:Port config, CLI arg
// ASPNETCORE_URLS takes highest precedence (set by Docker); PORT is the simple .env-friendly option.
if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
{
    var portEnv = Environment.GetEnvironmentVariable("PORT");
    if (!string.IsNullOrEmpty(portEnv) && int.TryParse(portEnv, out var envPort))
    {
        builder.WebHost.UseUrls($"http://+:{envPort}");
    }
    else
    {
        var port = builder.Configuration.GetValue<int?>("Server:Port");
        if (port.HasValue)
        {
            builder.WebHost.UseUrls($"http://+:{port.Value}");
        }
    }
}

// Startup config validation — fail fast if critical settings are missing
var poracleDb = builder.Configuration.GetConnectionString("PoracleDb");
if (string.IsNullOrWhiteSpace(poracleDb))
{
    throw new InvalidOperationException("Configuration 'ConnectionStrings:PoracleDb' is required but was not provided.");
}

var jwtSecret = builder.Configuration["Jwt:Secret"];
if (string.IsNullOrWhiteSpace(jwtSecret) || jwtSecret.Length < 32)
{
    throw new InvalidOperationException("Configuration 'Jwt:Secret' is required and must be at least 32 characters.");
}

var discordClientId = builder.Configuration["Discord:ClientId"];
if (string.IsNullOrWhiteSpace(discordClientId))
{
    throw new InvalidOperationException("Configuration 'Discord:ClientId' is required but was not provided.");
}

var discordClientSecret = builder.Configuration["Discord:ClientSecret"];
if (string.IsNullOrWhiteSpace(discordClientSecret))
{
    throw new InvalidOperationException("Configuration 'Discord:ClientSecret' is required but was not provided.");
}

// Add controllers. The global FeatureDisabledExceptionFilter maps any FeatureDisabledException
// thrown from a service into HTTP 403 — covers callers that bypass [RequireFeatureEnabled]
// (e.g. QuickPickService → MonsterService.CreateAsync). See #236.
builder.Services.AddControllers(options =>
{
    options.Filters.Add<Pgan.PoracleWebNet.Api.Filters.FeatureDisabledExceptionFilter>();
    options.Filters.Add<Pgan.PoracleWebNet.Api.Filters.SummaryBackendUnavailableExceptionFilter>();
    options.Filters.Add<Pgan.PoracleWebNet.Api.Filters.TrackingConflictExceptionFilter>();
    options.Filters.Add<Pgan.PoracleWebNet.Api.Filters.AlarmValidationExceptionFilter>();
    options.Filters.Add<Pgan.PoracleWebNet.Api.Filters.AccountGoneExceptionFilter>();
    options.Filters.Add<Pgan.PoracleWebNet.Api.Filters.BlockedAccountFilter>();
});

// Add Poracle services (DbContext, repositories, services, settings)
builder.Services.AddPoracleServices(builder.Configuration);

// Background services
builder.Services.AddHostedService<Pgan.PoracleWebNet.Api.Services.AvatarCacheService>();
builder.Services.AddHostedService<Pgan.PoracleWebNet.Api.Services.DtsCacheService>();
builder.Services.AddHostedService<Pgan.PoracleWebNet.Api.Services.SettingsMigrationStartupService>();
builder.Services.AddHostedService<Pgan.PoracleWebNet.Api.Services.Oidc.OidcSessionCleanupService>();

// JWT Authentication
var jwtSettings = builder.Configuration.GetSection("Jwt").Get<JwtSettings>()!;
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options => options.TokenValidationParameters = new TokenValidationParameters
{
    ValidateIssuer = true,
    ValidateAudience = true,
    ValidateLifetime = true,
    ValidateIssuerSigningKey = true,
    ValidIssuer = jwtSettings.Issuer,
    ValidAudience = jwtSettings.Audience,
    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Secret))
});

builder.Services.AddAuthorization();

// Rate limiting
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            IpPartitionKey(httpContext),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromSeconds(60),
                // Zero, like every other policy here. A queue of 2 did not reject requests 31 and 32 --
                // it parked them until the window rolled over, up to a minute later, so on a shared
                // egress IP the 31st person to log in got a spinner instead of "too many requests",
                // and an intermediate proxy could time the request out entirely. See #546.
                QueueLimit = 0,
                AutoReplenishment = true,
            }));
    options.AddPolicy("auth-read", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            UserOrIpPartitionKey(httpContext),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 120,
                Window = TimeSpan.FromSeconds(60),
                QueueLimit = 0,
                AutoReplenishment = true,
            }));
    options.AddPolicy("test-alert", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            UserOrIpPartitionKey(httpContext),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromSeconds(60),
                QueueLimit = 0,
                AutoReplenishment = true,
            }));
    options.AddPolicy("geojson-import", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            UserOrIpPartitionKey(httpContext),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromSeconds(60),
                QueueLimit = 0,
                AutoReplenishment = true,
            }));
    options.AddPolicy("scanner-search", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            UserOrIpPartitionKey(httpContext),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 60,
                Window = TimeSpan.FromSeconds(60),
                QueueLimit = 0,
                AutoReplenishment = true,
            }));
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.ContentType = "application/json";
        await context.HttpContext.Response.WriteAsync(
            JsonSerializer.Serialize(new
            {
                error = "Too many requests. Please try again later."
            }),
            cancellationToken);
    };
});

// CORS — require explicit origin whitelist to prevent credential leakage
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();
if (allowedOrigins is not { Length: > 0 } && !builder.Environment.IsDevelopment())
{
    throw new InvalidOperationException(
        "Configuration 'Cors:AllowedOrigins' is required in non-development environments. " +
        "Set it to the origin(s) of your frontend (e.g., [\"https://poracle.example.com\"]).");
}

builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    {
        if (allowedOrigins is { Length: > 0 })
        {
            policy.WithOrigins(allowedOrigins);
        }
        else
        {
            // Development only — never runs in production due to startup check above
            policy.SetIsOriginAllowed(_ => true);
        }

        policy.AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    }));

// OpenAPI
builder.Services.AddOpenApi();

var app = builder.Build();

// Ensure pweb_settings.value can hold JSON blobs (quick pick definitions/applied states)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<Pgan.PoracleWebNet.Data.PoracleContext>();
    try
    {
        await db.Database.ExecuteSqlRawAsync(
            "ALTER TABLE pweb_settings MODIFY COLUMN `value` LONGTEXT NULL");
    }
    catch (Exception ex)
    {
        var startupLogger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger("Startup");
        StartupLog.LogPwebSettingsAlterFailed(startupLogger, ex);
    }
}

// Apply pending EF Core migrations for the PoracleWeb database
using (var scope = app.Services.CreateScope())
{
    var webDb = scope.ServiceProvider.GetRequiredService<Pgan.PoracleWebNet.Data.PoracleWebContext>();
    try
    {
        await webDb.Database.MigrateAsync();
    }
    catch (Exception ex)
    {
        var startupLogger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger("Startup");
        StartupLog.LogPoracleWebDbEnsureCreatedFailed(startupLogger, ex);
    }
}

// Global exception handling
app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
    {
        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("GlobalExceptionHandler");
        var exceptionFeature = context.Features.Get<IExceptionHandlerFeature>();
        if (exceptionFeature is not null)
        {
            StartupLog.LogUnhandledException(logger, exceptionFeature.Error);
        }

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(
            JsonSerializer.Serialize(new
            {
                error = "An unexpected error occurred."
            }));
    }));

// Support reverse proxies (X-Forwarded-For, X-Forwarded-Proto, X-Forwarded-Host)
var forwardedHeadersOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
};
// Clearing both lists tells ASP.NET to believe X-Forwarded-For from ANY peer, so a client could name its
// own address and hand itself a fresh rate-limit bucket per request -- including on the login endpoints
// the per-IP partitioning exists to protect. Trust only the proxies the deployment names.
//
// PROXY_KNOWN_PROXIES / PROXY_KNOWN_NETWORKS take comma-separated addresses and CIDR ranges. With
// neither set the header is ignored entirely and the connection address is used, which is correct for a
// direct-exposed instance and safe for one behind a proxy that has not been declared yet -- it means
// everyone behind that proxy shares a bucket, rather than everyone being able to forge one. See #583.
foreach (var proxy in SplitConfigList(builder.Configuration["Proxy:KnownProxies"]))
{
    if (System.Net.IPAddress.TryParse(proxy, out var address))
    {
        forwardedHeadersOptions.KnownProxies.Add(address);
    }
}

foreach (var network in SplitConfigList(builder.Configuration["Proxy:KnownNetworks"]))
{
    var parts = network.Split('/', 2);
    if (parts.Length == 2
        && System.Net.IPAddress.TryParse(parts[0], out var prefix)
        && int.TryParse(parts[1], out var length))
    {
#pragma warning disable ASPDEPR005
        forwardedHeadersOptions.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(prefix, length));
#pragma warning restore ASPDEPR005
    }
}

app.UseForwardedHeaders(forwardedHeadersOptions);

// Security headers -- values live in SecurityHeaders so they can be unit-tested
app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        SecurityHeaders.Apply(context.Response.Headers);
        return Task.CompletedTask;
    });
    await next();
});

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors();


app.UseAuthentication();
app.UseAuthorization();

// After authentication, deliberately. Registered before it, the partition key could not see
// User.Identity, so every "per-user" policy silently fell back to the IP -- one person behind a shared
// egress could exhaust the allowance for everyone behind it, and the per-user policies were per-user in
// name only. See #581.
app.UseRateLimiter();

app.MapControllers();

// Serve Angular SPA
app.UseDefaultFiles();
app.UseStaticFiles();
if (!app.Environment.IsDevelopment())
{
    app.MapFallbackToFile("index.html");
}

app.Run();

// Rate-limit partition key for anonymous endpoints (login, callback): per-IP only.
static string IpPartitionKey(HttpContext ctx) =>
    "ip:" + (ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown");

// Rate-limit partition key for endpoints that require auth: prefer userId when present
// (so shared-NAT / corporate proxies don't force multiple users into one bucket), fall
// back to IP for unauthenticated callers or requests where the claim is missing.
static string UserOrIpPartitionKey(HttpContext ctx)
{
    var userId = ctx.User?.FindFirst("userId")?.Value;
    return !string.IsNullOrEmpty(userId)
        ? "user:" + userId
        : IpPartitionKey(ctx);
}

// Maps a short env var name to .NET's __ convention if the target is not already set.
/// <summary>Splits a comma-separated config value, ignoring blanks.</summary>
static string[] SplitConfigList(string? value) =>
    string.IsNullOrWhiteSpace(value)
        ? []
        : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

static void MapEnvVar(string shortName, string configName, string? defaultValue = null)
{
    if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(configName)))
    {
        return;
    }
    var value = Environment.GetEnvironmentVariable(shortName);
    if (string.IsNullOrEmpty(value))
    {
        value = defaultValue;
    }
    if (!string.IsNullOrEmpty(value))
    {
        Environment.SetEnvironmentVariable(configName, value);
    }
}

// Composes a MySQL connection string from individual DB_HOST/DB_PORT/etc. env vars
// when the full ConnectionStrings__* env var is not already set.
static void ComposeConnectionString(string name, string hostVar, string portVar, string dbVar, string userVar, string passVar, string defaultDb)
{
    var csKey = $"ConnectionStrings__{name}";
    if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(csKey)))
    {
        return;
    }

    var host = Environment.GetEnvironmentVariable(hostVar);
    var pass = Environment.GetEnvironmentVariable(passVar);
    if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(pass))
    {
        return;
    }

    var port = Environment.GetEnvironmentVariable(portVar) ?? "3306";
    var db = Environment.GetEnvironmentVariable(dbVar) ?? defaultDb;
    var user = Environment.GetEnvironmentVariable(userVar) ?? "root";

    Environment.SetEnvironmentVariable(csKey,
        $"Server={host};Port={port};Database={db};User={user};Password={pass};AllowZeroDateTime=true;ConvertZeroDateTime=true");
}

internal static partial class StartupLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not alter pweb_settings.value column (may already be LONGTEXT).")]
    public static partial void LogPwebSettingsAlterFailed(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not ensure PoracleWeb database tables exist.")]
    public static partial void LogPoracleWebDbEnsureCreatedFailed(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception occurred.")]
    public static partial void LogUnhandledException(ILogger logger, Exception ex);
}
