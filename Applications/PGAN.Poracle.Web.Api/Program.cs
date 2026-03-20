using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using PGAN.Poracle.Web.Api.Configuration;

var builder = WebApplication.CreateBuilder(args);

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
builder.Services.AddHostedService<PGAN.Poracle.Web.Api.Services.AvatarCacheService>();
builder.Services.AddHostedService<PGAN.Poracle.Web.Api.Services.DtsCacheService>();

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
    options.AddFixedWindowLimiter("auth", limiterOptions =>
    {
        limiterOptions.PermitLimit = 30;
        limiterOptions.Window = TimeSpan.FromSeconds(60);
        limiterOptions.QueueLimit = 2;
        limiterOptions.AutoReplenishment = true;
    });
    options.AddFixedWindowLimiter("auth-read", limiterOptions =>
    {
        limiterOptions.PermitLimit = 120;
        limiterOptions.Window = TimeSpan.FromSeconds(60);
        limiterOptions.QueueLimit = 0;
        limiterOptions.AutoReplenishment = true;
    });
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

// CORS
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    {
        if (allowedOrigins is { Length: > 0 })
        {
            policy.WithOrigins(allowedOrigins);
        }
        else
        {
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
    var db = scope.ServiceProvider.GetRequiredService<PGAN.Poracle.Web.Data.PoracleContext>();
    try
    {
        await db.Database.ExecuteSqlRawAsync(
            "ALTER TABLE pweb_settings MODIFY COLUMN `value` LONGTEXT NULL");
    }
    catch (Exception ex)
    {
        var startupLogger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger("Startup");
        startupLogger.LogWarning(ex, "Could not alter pweb_settings.value column (may already be LONGTEXT).");
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
            logger.LogError(exceptionFeature.Error, "Unhandled exception occurred.");
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
        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["X-XSS-Protection"] = "0";
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self' 'unsafe-hashes' 'sha256-MhtPZXr7+LpJUY5qtMutB+qWfQtMaPccfe7QXtCcEYc='; style-src 'self' 'unsafe-inline'; font-src 'self' https://fonts.gstatic.com; img-src 'self' data: https:; connect-src 'self' https://raw.githubusercontent.com";
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
