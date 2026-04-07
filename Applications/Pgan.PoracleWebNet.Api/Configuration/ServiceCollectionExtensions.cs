using Microsoft.EntityFrameworkCore;
using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Mappings;
using Pgan.PoracleWebNet.Core.Repositories;
using Pgan.PoracleWebNet.Core.Services;
using Pgan.PoracleWebNet.Data;
using Pgan.PoracleWebNet.Data.Scanner;

namespace Pgan.PoracleWebNet.Api.Configuration;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPoracleServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Register DbContext with Oracle MySQL provider
        var connectionString = configuration.GetConnectionString("PoracleDb");
        services.AddDbContext<PoracleContext>(options =>
            options.UseMySQL(connectionString!));

        // Register PoracleWebContext for the poracle_web database (owned by this app)
        var webConnectionString = configuration.GetConnectionString("PoracleWebDb");
        services.AddDbContext<PoracleWebContext>(options =>
            options.UseMySQL(webConnectionString!)
                .ReplaceService<Microsoft.EntityFrameworkCore.Migrations.IHistoryRepository, MariaDbHistoryRepository>());

        // Register MemoryCache
        services.AddMemoryCache();

        // Register AutoMapper
        services.AddAutoMapper(cfg => cfg.AddProfile<PoracleMappingProfile>());

        // Register Repositories
        services.AddScoped<IHumanRepository, HumanRepository>();
        services.AddScoped<IProfileRepository, ProfileRepository>();
        services.AddScoped<IPwebSettingRepository, PwebSettingRepository>();
        services.AddScoped<IUserGeofenceRepository, UserGeofenceRepository>();
        services.AddScoped<ISiteSettingRepository, SiteSettingRepository>();
        services.AddScoped<IWebhookDelegateRepository, WebhookDelegateRepository>();
        services.AddScoped<IQuickPickDefinitionRepository, QuickPickDefinitionRepository>();
        services.AddScoped<IQuickPickAppliedStateRepository, QuickPickAppliedStateRepository>();

        // Register Services
        services.AddScoped<IMonsterService, MonsterService>();
        services.AddScoped<IRaidService, RaidService>();
        services.AddScoped<IEggService, EggService>();
        services.AddScoped<IQuestService, QuestService>();
        services.AddScoped<IInvasionService, InvasionService>();
        services.AddScoped<ILureService, LureService>();
        services.AddScoped<INestService, NestService>();
        services.AddScoped<IGymService, GymService>();
        services.AddScoped<IFortChangeService, FortChangeService>();
        services.AddScoped<IMaxBattleService, MaxBattleService>();
        services.AddScoped<IHumanService, HumanService>();
        services.AddScoped<IProfileService, ProfileService>();
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<ICleaningService, CleaningService>();
        services.AddScoped<IPwebSettingService, PwebSettingService>();
        services.AddSingleton<IMasterDataService, MasterDataService>();
        services.AddScoped<IQuickPickService, QuickPickService>();
        services.AddScoped<IUserGeofenceService, UserGeofenceService>();
        services.AddScoped<ISiteSettingService, SiteSettingService>();
        services.AddScoped<IWebhookDelegateService, WebhookDelegateService>();
        services.AddScoped<ISettingsMigrationService, SettingsMigrationService>();
        services.AddScoped<IProfileOverviewService, ProfileOverviewService>();
        services.AddScoped<ITestAlertService, TestAlertService>();

        // Register Scanner DB (optional - only if connection string is configured)
        var scannerConnectionString = configuration.GetConnectionString("ScannerDb");
        if (!string.IsNullOrEmpty(scannerConnectionString))
        {
            services.AddDbContext<RdmScannerContext>(options =>
                options.UseMySQL(scannerConnectionString));
            services.AddScoped<IScannerService, RdmScannerService>();
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

        // Register HttpClient for PoracleNG tracking proxy (alarm CRUD — replaces direct DB writes)
        services.AddHttpClient<IPoracleTrackingProxy, PoracleTrackingProxy>();

        // Register HttpClient for PoracleNG human/profile proxy (replaces direct DB writes)
        services.AddHttpClient<IPoracleHumanProxy, PoracleHumanProxy>();

        // Register Poracle Server settings and service (multi-server restart)
        services.Configure<PoracleServerSettings>(config =>
        {
            var servers = configuration.GetSection("Poracle:Servers").Get<List<PoracleServerConfig>>();
            if (servers != null)
            {
                config.Servers = servers;
            }

            config.SshKeyPath = configuration["Poracle:SshKeyPath"] ?? "/app/ssh_key";
        });
        services.AddHttpClient<IPoracleServerService, PoracleServerService>();

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

        // Register settings
        services.Configure<JwtSettings>(configuration.GetSection("Jwt"));
        services.Configure<DiscordSettings>(configuration.GetSection("Discord"));
        services.Configure<TelegramSettings>(configuration.GetSection("Telegram"));
        services.Configure<PoracleSettings>(configuration.GetSection("Poracle"));
        services.Configure<KojiSettings>(configuration.GetSection("Koji"));

        return services;
    }
}
