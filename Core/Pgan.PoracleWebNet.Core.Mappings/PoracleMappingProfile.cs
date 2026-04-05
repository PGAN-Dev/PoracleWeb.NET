using System.Text.Json;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Data.Entities;

namespace Pgan.PoracleWebNet.Core.Mappings;

public class PoracleMappingProfile : AutoMapper.Profile
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public PoracleMappingProfile()
    {
        // Monster mappings
        this.CreateMap<MonsterCreate, Monster>();
        this.CreateMap<MonsterUpdate, Monster>()
            .ForAllMembers(opts => opts.Condition((_, _, srcMember) => srcMember != null));

        // Raid mappings
        this.CreateMap<RaidCreate, Raid>();
        this.CreateMap<RaidUpdate, Raid>()
            .ForAllMembers(opts => opts.Condition((_, _, srcMember) => srcMember != null));

        // Egg mappings
        this.CreateMap<EggCreate, Egg>();
        this.CreateMap<EggUpdate, Egg>()
            .ForAllMembers(opts => opts.Condition((_, _, srcMember) => srcMember != null));

        // Quest mappings
        this.CreateMap<QuestCreate, Quest>();
        this.CreateMap<QuestUpdate, Quest>()
            .ForAllMembers(opts => opts.Condition((_, _, srcMember) => srcMember != null));

        // Invasion mappings
        this.CreateMap<InvasionCreate, Invasion>();
        this.CreateMap<InvasionUpdate, Invasion>()
            .ForAllMembers(opts => opts.Condition((_, _, srcMember) => srcMember != null));

        // Lure mappings
        this.CreateMap<LureCreate, Lure>();
        this.CreateMap<LureUpdate, Lure>()
            .ForAllMembers(opts => opts.Condition((_, _, srcMember) => srcMember != null));

        // Nest mappings
        this.CreateMap<NestCreate, Nest>();
        this.CreateMap<NestUpdate, Nest>()
            .ForAllMembers(opts => opts.Condition((_, _, srcMember) => srcMember != null));

        // Gym mappings
        this.CreateMap<GymCreate, Gym>();
        this.CreateMap<GymUpdate, Gym>()
            .ForAllMembers(opts => opts.Condition((_, _, srcMember) => srcMember != null));

        // Fort Change mappings
        this.CreateMap<FortChangeCreate, FortChange>();
        this.CreateMap<FortChangeUpdate, FortChange>()
            .ForAllMembers(opts => opts.Condition((_, _, srcMember) => srcMember != null));

        // MaxBattle mappings
        this.CreateMap<MaxBattleCreate, MaxBattle>();
        this.CreateMap<MaxBattleUpdate, MaxBattle>()
            .ForAllMembers(opts => opts.Condition((_, _, srcMember) => srcMember != null));

        // Human mappings
        this.CreateMap<HumanEntity, Human>().ReverseMap();

        // Profile mappings
        this.CreateMap<ProfileEntity, Profile>().ReverseMap();

        // PwebSetting mappings
        this.CreateMap<PwebSettingEntity, PwebSetting>().ReverseMap();

        // UserGeofence mappings
        this.CreateMap<UserGeofenceEntity, UserGeofence>()
            .ForMember(dest => dest.Polygon, opt => opt.Ignore());
        this.CreateMap<UserGeofence, UserGeofenceEntity>()
            .ForMember(dest => dest.Id, opt => opt.Ignore());

        // Site Settings
        this.CreateMap<SiteSettingEntity, SiteSetting>().ReverseMap();

        // Webhook Delegates
        this.CreateMap<WebhookDelegateEntity, WebhookDelegate>().ReverseMap();

        // Quick Pick Definitions — custom mapping for FiltersJson ↔ Filters dictionary
        this.CreateMap<QuickPickDefinitionEntity, QuickPickDefinition>()
            .ForMember(d => d.Filters, opt => opt.MapFrom(s =>
                string.IsNullOrEmpty(s.FiltersJson)
                    ? new Dictionary<string, object?>()
                    : JsonSerializer.Deserialize<Dictionary<string, object?>>(s.FiltersJson, JsonOptions)))
            .ForMember(d => d.OwnerUserId, opt => opt.MapFrom(s => s.OwnerUserId));

        this.CreateMap<QuickPickDefinition, QuickPickDefinitionEntity>()
            .ForMember(d => d.FiltersJson, opt => opt.MapFrom(s =>
                JsonSerializer.Serialize(s.Filters, JsonOptions)))
            .ForMember(d => d.OwnerUserId, opt => opt.MapFrom(s => s.OwnerUserId));

        // Quick Pick Applied States — custom mapping for JSON list columns
        this.CreateMap<QuickPickAppliedStateEntity, QuickPickAppliedState>()
            .ForMember(d => d.ExcludePokemonIds, opt => opt.MapFrom(s =>
                string.IsNullOrEmpty(s.ExcludePokemonIdsJson)
                    ? new List<int>()
                    : JsonSerializer.Deserialize<List<int>>(s.ExcludePokemonIdsJson, JsonOptions)))
            .ForMember(d => d.TrackedUids, opt => opt.MapFrom(s =>
                string.IsNullOrEmpty(s.TrackedUidsJson)
                    ? new List<int>()
                    : JsonSerializer.Deserialize<List<int>>(s.TrackedUidsJson, JsonOptions)));

        this.CreateMap<QuickPickAppliedState, QuickPickAppliedStateEntity>()
            .ForMember(d => d.ExcludePokemonIdsJson, opt => opt.MapFrom(s =>
                JsonSerializer.Serialize(s.ExcludePokemonIds, JsonOptions)))
            .ForMember(d => d.TrackedUidsJson, opt => opt.MapFrom(s =>
                JsonSerializer.Serialize(s.TrackedUids, JsonOptions)));
    }
}
