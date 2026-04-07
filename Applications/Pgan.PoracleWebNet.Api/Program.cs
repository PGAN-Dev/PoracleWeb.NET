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
MapEnvVar("DISCORD_CLIENT_ID", "Discord__ClientId");
MapEnvVar("DISCORD_CLIENT_SECRET", "Discord__ClientSecret");
MapEnvVar("DISCORD_BOT_TOKEN", "Discord__BotToken");
MapEnvVar("DISCORD_GUILD_ID", "Discord__GuildId");
MapEnvVar("DISCORD_GEOFENCE_FORUM_CHANNEL_ID", "Discord__GeofenceForumChannelId");
MapEnvVar("TELEGRAM_ENABLED", "Telegram__Enabled");
MapEnvVar("TELEGRAM_BOT_TOKEN", "Telegram__BotToken");
MapEnvVar("TELEGRAM_BOT_USERNAME", "Telegram__BotUsername");
MapEnvVar("PORACLE_API_ADDRESS", "Poracle__ApiAddress");
MapEnvVar("PORACLE_API_SECRET", "Poracle__ApiSecret");
MapEnvVar("PORACLE_ADMIN_IDS", "Poracle__AdminIds");
MapEnvVar("PORACLE_SSH_KEY_PATH", "Poracle__SshKeyPath");
MapEnvVar("KOJI_API_ADDRESS", "Koji__ApiAddress");
MapEnvVar("KOJI_BEARER_TOKEN", "Koji__BearerToken");
MapEnvVar("KOJI_PROJECT_ID", "Koji__ProjectId");
MapEnvVar("KOJI_PROJECT_NAME", "Koji__ProjectName");
MapEnvVar("CORS_ORIGIN", "Cors__AllowedOrigins__0");
MapEnvVar("SCANNER_DB_CONNECTION", "ConnectionStrings__ScannerDb");

// Map Poracle server env vars (PORACLE_SERVER_N_*) to Poracle__Servers__N__*
for (var i = 1; i <= 10; i++)
{
    var idx = i - 1; // .env uses 1-based, .NET config uses 0-based
    var host = Environment.GetEnvironmentVariable($"PORACLE_SERVER_{i}_HOST");
    if (string.IsNullOrEmpty(host))
    {
        continue;
    }

    MapEnvVar($"PORACLE_SERVER_{i}_HOST", $"Poracle__Servers__{idx}__Host");
    MapEnvVar($"PORACLE_SERVER_{i}_API", $"Poracle__Servers__{idx}__ApiAddress");
    MapEnvVar($"PORACLE_SERVER_{i}_SSH_USER", $"Poracle__Servers__{idx}__SshUser");
    MapEnvVar($"PORACLE_SERVER_{i}_RESTART_CMD", $"Poracle__Servers__{idx}__RestartCommand");
    MapEnvVar($"PORACLE_SERVER_{i}_GROUP_MAP", $"Poracle__Servers__{idx}__GroupMapPath");

    // Set server name default if not already set
    var nameKey = $"Poracle__Servers__{idx}__Name";
    if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(nameKey)))
    {
        Environment.SetEnvironmentVariable(nameKey, i == 1 ? "Main" : $"Server {i}");
    }
}

// Auto-compose MySQL connection strings from short env vars (DB_HOST, DB_PORT, etc.)
// so the same .env works for both Docker Compose and standalone mode.
ComposeConnectionString("PoracleDb", "DB_HOST", "DB_PORT", "DB_NAME", "DB_USER", "DB_PASSWORD", "poracle");
ComposeConnectionString("PoracleWebDb", "WEB_DB_HOST", "WEB_DB_PORT", "WEB_DB_NAME", "WEB_DB_USER", "WEB_DB_PASSWORD", "poracle_web");

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

// Add controllers
builder.Services.AddControllers();

// Add Poracle services (DbContext, repositories, services, settings)
builder.Services.AddPoracleServices(builder.Configuration);

// Background services
builder.Services.AddHostedService<Pgan.PoracleWebNet.Api.Services.AvatarCacheService>();
builder.Services.AddHostedService<Pgan.PoracleWebNet.Api.Services.DtsCacheService>();
builder.Services.AddHostedService<Pgan.PoracleWebNet.Api.Services.SettingsMigrationStartupService>();

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
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromSeconds(60),
                QueueLimit = 2,
                AutoReplenishment = true,
            }));
    options.AddPolicy("auth-read", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 120,
                Window = TimeSpan.FromSeconds(60),
                QueueLimit = 0,
                AutoReplenishment = true,
            }));
    options.AddPolicy("test-alert", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
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
#pragma warning disable ASPDEPR005
forwardedHeadersOptions.KnownNetworks.Clear();
#pragma warning restore ASPDEPR005
forwardedHeadersOptions.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedHeadersOptions);

// Security headers
app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        var headers = context.Response.Headers;
        headers.XContentTypeOptions = "nosniff";
        headers.XFrameOptions = "DENY";
        headers.XXSSProtection = "0";
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        headers.ContentSecurityPolicy = "default-src 'self'; script-src 'self' 'unsafe-hashes' 'sha256-MhtPZXr7+LpJUY5qtMutB+qWfQtMaPccfe7QXtCcEYc='; style-src 'self' 'unsafe-inline'; font-src 'self' https://fonts.gstatic.com; img-src 'self' data: https:; connect-src 'self' https://raw.githubusercontent.com";
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

app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Serve Angular SPA
app.UseDefaultFiles();
app.UseStaticFiles();
if (!app.Environment.IsDevelopment())
{
    app.MapFallbackToFile("index.html");
}

app.Run();

// Maps a short env var name to .NET's __ convention if the target is not already set.
static void MapEnvVar(string shortName, string configName)
{
    var value = Environment.GetEnvironmentVariable(shortName);
    if (!string.IsNullOrEmpty(value) && string.IsNullOrEmpty(Environment.GetEnvironmentVariable(configName)))
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
