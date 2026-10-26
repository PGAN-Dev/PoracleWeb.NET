using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Abstractions.Services;

using Pgan.PoracleWebNet.Core.Repositories;
using Pgan.PoracleWebNet.Core.Services;
using Pgan.PoracleWebNet.Core.Services.Pvp;
using Pgan.PoracleWebNet.Core.Services.TestAlerts;
using Pgan.PoracleWebNet.Data;
using Pgan.PoracleWebNet.Data.Scanner;

namespace Pgan.PoracleWebNet.Api.Configuration;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPoracleServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Register PoracleWebContext for the poracle_web database (owned by this app)
        var webConnectionString = configuration.GetConnectionString("PoracleWebDb");
        services.AddDbContext<PoracleWebContext>(options =>
            options.UseMySQL(webConnectionString!)
                .ReplaceService<Microsoft.EntityFrameworkCore.Migrations.IHistoryRepository, MariaDbHistoryRepository>());

        // Register MemoryCache
        services.AddMemoryCache();

        // Persist DataProtection keys so they survive container restarts.
        // Docker: DATA_DIR=/app/data (set in Dockerfile, volume-mounted in docker-compose.yml).
        // Standalone: falls back to ./data/ relative to the working directory.
        var dataDir = configuration["DATA_DIR"] ?? Path.Join(Directory.GetCurrentDirectory(), "data");
        var dataDirFullPath = Path.GetFullPath(dataDir);
        var keyDirectoryPath = Path.GetFullPath(Path.Join(dataDirFullPath, "dataprotection-keys"));
        var expectedPrefix = dataDirFullPath.EndsWith(Path.DirectorySeparatorChar)
            ? dataDirFullPath
            : dataDirFullPath + Path.DirectorySeparatorChar;

        if (!keyDirectoryPath.StartsWith(expectedPrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Resolved DataProtection key path is outside DATA_DIR.");
        }

        services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(keyDirectoryPath))
            .SetApplicationName("Pgan.PoracleWebNet.Api");

        // Register Repositories
        services.AddScoped<IUserGeofenceRepository, UserGeofenceRepository>();
        services.AddScoped<ISiteSettingRepository, SiteSettingRepository>();
        services.AddScoped<IWebhookDelegateRepository, WebhookDelegateRepository>();
        services.AddScoped<IAccountLinkRepository, AccountLinkRepository>();
        services.AddScoped<IQuickPickDefinitionRepository, QuickPickDefinitionRepository>();
        services.AddScoped<IQuickPickAppliedStateRepository, QuickPickAppliedStateRepository>();
        services.AddScoped<IOidcSessionRepository, OidcSessionRepository>();

        // Register Services
        services.AddScoped<IMonsterService, MonsterService>();
        // Keeps quick-pick tracked uids pointing at live rows when an edit moves them (#403).
        // Registered before the alarm services, which all depend on it.
        services.AddScoped<ITrackedUidRemapper, TrackedUidRemapper>();
        services.AddScoped<IRaidService, RaidService>();
        services.AddScoped<IEggService, EggService>();
        services.AddScoped<IQuestService, QuestService>();
        services.AddScoped<IInvasionService, InvasionService>();
        services.AddScoped<ILureService, LureService>();
        services.AddScoped<IPokestopEventService, PokestopEventService>();
        services.AddScoped<INestService, NestService>();
        services.AddScoped<IGymService, GymService>();
        services.AddScoped<IFortChangeService, FortChangeService>();
        services.AddScoped<IMaxBattleService, MaxBattleService>();
        services.AddScoped<IHumanService, HumanService>();
        services.AddScoped<ILinkedAccount, LinkedAccount>();
        services.AddScoped<LinkedAlarmRouter>();
        services.AddScoped<QuickPickUidTransfer>();
        services.AddScoped<AlarmMover>();
        services.AddScoped<AlarmMoveDispatcher>();
        services.AddScoped<ILinkedHumanProvisioner, LinkedHumanProvisioner>();
        services.AddScoped<IAccountLinkService, AccountLinkService>();
        services.AddScoped<ITelegramRegistrationCheck, TelegramRegistrationCheck>();
        services.AddHttpClient<ITelegramMembershipClient, TelegramBotApiClient>(client =>
        {
            client.BaseAddress = new Uri("https://api.telegram.org/");
            client.Timeout = TimeSpan.FromSeconds(10);
        });
        services.AddScoped<IPairedHumanWriter, PairedHumanWriter>();
        services.AddScoped<IPairDriftService, PairDriftService>();
        // Lets UserAreaPolicy ask for the link without a construction cycle (see UserAreaPolicy.OwnerIdsAsync).
        services.AddScoped(sp => new Lazy<ILinkedAccount>(() => sp.GetRequiredService<ILinkedAccount>()));
        services.AddScoped<IUserPurgeService, UserPurgeService>();
        services.AddScoped<IProfileService, ProfileService>();
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<ICleaningService, CleaningService>();
        services.AddSingleton<IMasterDataService, MasterDataService>();
        services.AddSingleton<IRaidLevelService, RaidLevelService>();
        services.AddSingleton<IPvpRankService, PvpRankService>();
        services.AddScoped<IQuickPickService, QuickPickService>();
        services.AddScoped<IUserGeofenceService, UserGeofenceService>();
        services.AddScoped<IUserAreaPolicy, UserAreaPolicy>();
        services.AddScoped<IUserAreaWriter, UserAreaWriter>();
        services.AddScoped<ISiteSettingService, SiteSettingService>();
        services.AddScoped<ISummaryCapabilityService, SummaryCapabilityService>();
        services.AddScoped<IPokemonEditCapabilityService, PokemonEditCapabilityService>();
        services.AddScoped<IPersonalShinyConfigService, PersonalShinyConfigService>();
        services.AddScoped<IUpstreamFeatureFlagService, UpstreamFeatureFlagService>();
        services.AddScoped<IFeatureGate, FeatureGate>();
        services.AddScoped<IWebhookDelegateService, WebhookDelegateService>();
        services.AddScoped<ISettingsMigrationService, SettingsMigrationService>();
        services.AddScoped<IProfileOverviewService, ProfileOverviewService>();
        services.AddScoped<ITestAlertService, TestAlertService>();
        services.AddScoped<ITestPayloadBuilder, PokemonTestPayloadBuilder>();
        services.AddScoped<ITestPayloadBuilder, RaidOrEggTestPayloadBuilder>();
        services.AddScoped<ITestPayloadBuilder, QuestTestPayloadBuilder>();
        services.AddScoped<ITestPayloadBuilder, PokestopTestPayloadBuilder>();
        services.AddScoped<ITestPayloadBuilder, NestTestPayloadBuilder>();
        services.AddScoped<ITestPayloadBuilder, GymTestPayloadBuilder>();
        services.AddScoped<IGeoJsonService, GeoJsonService>();

        // Register Scanner DB (optional - only if connection string is configured)
        var scannerConnectionString = configuration.GetConnectionString("ScannerDb");
        if (!string.IsNullOrEmpty(scannerConnectionString))
        {
            services.AddDbContext<ScannerContext>(options =>
                options.UseMySQL(scannerConnectionString));
            services.AddScoped<IScannerService, ScannerService>();
        }

        // Register Golbat API proxy (optional — only if API address is configured)
        var golbatApiAddress = configuration["Golbat:ApiAddress"];
        if (!string.IsNullOrEmpty(golbatApiAddress))
        {
            services.Configure<GolbatSettings>(configuration.GetSection("Golbat"));
            services.AddHttpClient<IGolbatApiProxy, GolbatApiProxy>();
            services.AddSingleton<IPokemonAvailabilityService, PokemonAvailabilityService>();
        }

        // Register HttpClient for Poracle API (config, geofences, templates — read-only proxy)
        services.AddHttpClient<IPoracleApiProxy, PoracleApiProxy>();

        // The one outbound call PoracleWeb makes. Anonymous, cached for six hours, and switchable off
        // with disable_update_check for deployments that do not want egress at all.
        services.AddHttpClient<IUpdateCheckService, UpdateCheckService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(5);
            // GitHub refuses anonymous API calls that do not identify themselves.
            client.DefaultRequestHeaders.UserAgent.ParseAdd("PoracleWeb.NET");
        });

        // Which PoracleNG this is. /health is unauthenticated, so this needs no secret and still answers
        // when the API key is wrong -- a state that otherwise looks exactly like the server being down.
        services.AddHttpClient<IPoracleServerProfileService, PoracleServerProfileService>(client =>
        {
            // A diagnostic must not hold a request open: an unreachable server should answer
            // "unknown" quickly rather than stall the admin page behind a default 100s timeout.
            client.Timeout = TimeSpan.FromSeconds(5);
        });

        services.AddHttpClient<IPoracleV2SchemaService, PoracleV2SchemaService>(client =>
        {
            // Same reasoning as the profile probe above, with more headroom: /openapi.json is most of a
            // megabyte on 5.2.1 where /health is a line.
            client.Timeout = TimeSpan.FromSeconds(15);
        });

        // Register HttpClient for PoracleNG tracking proxy (every alarm read and write, including bulk
        // distance and cleaning, through /api/v2)
        services.AddHttpClient<IPoracleTrackingProxy, PoracleTrackingProxy>();

        // Pokestop events (upstream "incident") are v2-only, so they get their own proxy rather than a
        // second wire shape inside the tracking one. Like the tracking proxy it authorises override areas
        // through IUserAreaPolicy and then writes trusted, so a rule limited to a user's own fence saves.
        services.AddHttpClient<IPoracleIncidentProxy, PoracleIncidentProxy>();

        // Register HttpClient for PoracleNG human/profile proxy (replaces direct DB writes)
        services.AddHttpClient<IPoracleHumanProxy, PoracleHumanProxy>();

        // Rule repair for PoracleNG 5.4.0: a singleton, so one scan or apply runs per process. Each run
        // takes its own scope per human from IServiceScopeFactory, never a request's.
        services.AddSingleton<IRuleRepairJob, RuleRepairJob>();

        // Register HttpClient for PoracleNG summary schedule proxy (quest summary delivery)
        services.AddHttpClient<IPoracleSummaryProxy, PoracleSummaryProxy>();

        // Register HttpClient for PoracleNG's v2 mute store (quiet periods).
        services.AddHttpClient<IPoracleMuteProxy, PoracleMuteProxy>();

        // Register HttpClient for PoracleNG's linked game accounts (player ids for personal shiny alerts).
        services.AddHttpClient<IPoracleAccountProxy, PoracleAccountProxy>();

        // The opaque ids the account API hands out instead of player ids, keyed from the JWT secret
        // (validated at startup to be at least 32 characters) so they survive a restart.
        services.AddSingleton(_ => PlayerAccountHandles.FromSecret(configuration["Jwt:Secret"] ?? string.Empty));

        // Register HttpClient for Discord notification service
        services.AddHttpClient<IDiscordNotificationService, DiscordNotificationService>(client =>
        {
            client.BaseAddress = new Uri("https://discordapp.com/api/v9/");
            var botToken = configuration["Discord:BotToken"];
            if (!string.IsNullOrEmpty(botToken))
            {
                client.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bot", botToken);
            }
        });

        // Unauthenticated client for pulling the static map off the tileserver before uploading it to
        // Discord. Kept separate so the bot token never leaves discordapp.com.
        services.AddHttpClient(DiscordNotificationService.MapImageHttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
        });

        // Register HttpClient for Koji API
        var kojiToken = configuration["Koji:BearerToken"] ?? string.Empty;
        services.AddHttpClient<IKojiService, KojiService>(client =>
        {
            if (!string.IsNullOrEmpty(kojiToken))
            {
                client.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", kojiToken);
            }
        });

        // Register the generic OIDC HTTP client (code exchange / refresh / userinfo) and the
        // server-side refresh-session service (opaque-token rotation + encrypted RT storage).
        // An identity provider behind Cloudflare 403s a request with no User-Agent, which fails
        // every code exchange and refresh before it reaches the provider.
        services.AddHttpClient<Services.Oidc.IOidcClient, Services.Oidc.OidcClient>(client =>
            client.DefaultRequestHeaders.UserAgent.ParseAdd("PoracleWeb.NET"));
        services.AddScoped<Services.Oidc.IOidcSessionService, Services.Oidc.OidcSessionService>();

        // Register JWT service (shared token generation across controllers)
        services.AddSingleton<IJwtService, JwtService>();

        // Admin status and delegated webhooks, resolved live rather than trusted from a claim minted
        // at login. See #624 and #626.
        services.AddScoped<Services.IUserRoleResolver, Services.UserRoleResolver>();
        services.AddScoped<Services.IDestinationDirectory, Services.DestinationDirectory>();

        // Re-asks, on every request an impersonation token makes, whether whoever started it still may.
        // Wired into JwtBearer's OnTokenValidated in Program.cs.
        services.AddScoped<Services.IImpersonationAuthority, Services.ImpersonationAuthority>();

        // Bounds that check while PoracleNG or poracle_web is down; holds per-impersonator state, so one per process.
        services.AddSingleton<Services.ImpersonationRoleProbe>();

        // Register settings
        services.Configure<JwtSettings>(configuration.GetSection("Jwt"));
        services.Configure<DiscordSettings>(configuration.GetSection("Discord"));
        services.Configure<TelegramSettings>(configuration.GetSection("Telegram"));
        services.Configure<OidcSettings>(configuration.GetSection("Oidc"));
        services.Configure<PoracleSettings>(configuration.GetSection("Poracle"));
        services.Configure<KojiSettings>(configuration.GetSection("Koji"));

        return services;
    }
}
