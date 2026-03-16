using AutoMapper;
using PGAN.Poracle.Web.Core.Models;
using PGAN.Poracle.Web.Data.Entities;

namespace PGAN.Poracle.Web.Core.Mappings;

public class PoracleMappingProfile : AutoMapper.Profile
{
    public PoracleMappingProfile()
    {
        // Monster mappings
        CreateMap<MonsterEntity, Monster>().ReverseMap();
        CreateMap<MonsterCreate, MonsterEntity>();
        CreateMap<MonsterCreate, Monster>();
        CreateMap<MonsterUpdate, Monster>();

        // Raid mappings
        CreateMap<RaidEntity, Raid>().ReverseMap();
        CreateMap<RaidCreate, RaidEntity>();
        CreateMap<RaidCreate, Raid>();
        CreateMap<RaidUpdate, Raid>();

        // Egg mappings
        CreateMap<EggEntity, Egg>().ReverseMap();
        CreateMap<EggCreate, EggEntity>();
        CreateMap<EggCreate, Egg>();
        CreateMap<EggUpdate, Egg>();

        // Quest mappings
        CreateMap<QuestEntity, Quest>().ReverseMap();
        CreateMap<QuestCreate, QuestEntity>();
        CreateMap<QuestCreate, Quest>();
        CreateMap<QuestUpdate, Quest>();

        // Invasion mappings
        CreateMap<InvasionEntity, Invasion>().ReverseMap();
        CreateMap<InvasionCreate, InvasionEntity>();
        CreateMap<InvasionCreate, Invasion>();
        CreateMap<InvasionUpdate, Invasion>();

        // Lure mappings
        CreateMap<LureEntity, Lure>().ReverseMap();
        CreateMap<LureCreate, LureEntity>();
        CreateMap<LureCreate, Lure>();
        CreateMap<LureUpdate, Lure>();

        // Nest mappings
        CreateMap<NestEntity, Nest>().ReverseMap();
        CreateMap<NestCreate, NestEntity>();
        CreateMap<NestCreate, Nest>();
        CreateMap<NestUpdate, Nest>();

        // Gym mappings
        CreateMap<GymEntity, Gym>().ReverseMap();
        CreateMap<GymCreate, GymEntity>();
        CreateMap<GymCreate, Gym>();
        CreateMap<GymUpdate, Gym>();

        // Human mappings
        CreateMap<HumanEntity, Human>().ReverseMap();

        // Profile mappings
        CreateMap<ProfileEntity, Models.Profile>().ReverseMap();

        // PwebSetting mappings
        CreateMap<PwebSettingEntity, PwebSetting>().ReverseMap();
    }
}
