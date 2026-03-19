using PGAN.Poracle.Web.Core.Models;
using PGAN.Poracle.Web.Data.Entities;

namespace PGAN.Poracle.Web.Core.Mappings;

public class PoracleMappingProfile : AutoMapper.Profile
{
    public PoracleMappingProfile()
    {
        // Monster mappings
        this.CreateMap<MonsterEntity, Monster>().ReverseMap();
        this.CreateMap<MonsterCreate, MonsterEntity>();
        this.CreateMap<MonsterCreate, Monster>();
        this.CreateMap<MonsterUpdate, Monster>();

        // Raid mappings
        this.CreateMap<RaidEntity, Raid>().ReverseMap();
        this.CreateMap<RaidCreate, RaidEntity>();
        this.CreateMap<RaidCreate, Raid>();
        this.CreateMap<RaidUpdate, Raid>();

        // Egg mappings
        this.CreateMap<EggEntity, Egg>().ReverseMap();
        this.CreateMap<EggCreate, EggEntity>();
        this.CreateMap<EggCreate, Egg>();
        this.CreateMap<EggUpdate, Egg>();

        // Quest mappings
        this.CreateMap<QuestEntity, Quest>().ReverseMap();
        this.CreateMap<QuestCreate, QuestEntity>();
        this.CreateMap<QuestCreate, Quest>();
        this.CreateMap<QuestUpdate, Quest>();

        // Invasion mappings
        this.CreateMap<InvasionEntity, Invasion>().ReverseMap();
        this.CreateMap<InvasionCreate, InvasionEntity>();
        this.CreateMap<InvasionCreate, Invasion>();
        this.CreateMap<InvasionUpdate, Invasion>();

        // Lure mappings
        this.CreateMap<LureEntity, Lure>().ReverseMap();
        this.CreateMap<LureCreate, LureEntity>();
        this.CreateMap<LureCreate, Lure>();
        this.CreateMap<LureUpdate, Lure>();

        // Nest mappings
        this.CreateMap<NestEntity, Nest>().ReverseMap();
        this.CreateMap<NestCreate, NestEntity>();
        this.CreateMap<NestCreate, Nest>();
        this.CreateMap<NestUpdate, Nest>();

        // Gym mappings
        this.CreateMap<GymEntity, Gym>().ReverseMap();
        this.CreateMap<GymCreate, GymEntity>();
        this.CreateMap<GymCreate, Gym>();
        this.CreateMap<GymUpdate, Gym>();

        // Human mappings
        this.CreateMap<HumanEntity, Human>().ReverseMap();

        // Profile mappings
        this.CreateMap<ProfileEntity, Profile>().ReverseMap();

        // PwebSetting mappings
        this.CreateMap<PwebSettingEntity, PwebSetting>().ReverseMap();
    }
}
