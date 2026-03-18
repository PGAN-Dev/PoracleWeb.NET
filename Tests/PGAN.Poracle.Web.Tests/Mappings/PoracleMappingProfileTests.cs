using AutoMapper;
using Microsoft.Extensions.DependencyInjection;
using PGAN.Poracle.Web.Core.Mappings;
using PGAN.Poracle.Web.Core.Models;
using PGAN.Poracle.Web.Data.Entities;

namespace PGAN.Poracle.Web.Tests.Mappings;

public class PoracleMappingProfileTests
{
    private static IMapper CreateMapper()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAutoMapper(cfg => cfg.AddProfile<PoracleMappingProfile>());
        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IMapper>();
    }

    [Fact]
    public void Mapper_CanBeCreated()
    {
        var mapper = CreateMapper();
        Assert.NotNull(mapper);
    }

    [Fact]
    public void MonsterEntity_MapsTo_Monster()
    {
        var mapper = CreateMapper();
        var entity = new MonsterEntity { Uid = 1, PokemonId = 25, Id = "user1" };
        var model = mapper.Map<Monster>(entity);
        Assert.Equal(1, model.Uid);
        Assert.Equal(25, model.PokemonId);
        Assert.Equal("user1", model.Id);
    }

    [Fact]
    public void Monster_MapsTo_MonsterEntity()
    {
        var mapper = CreateMapper();
        var model = new Monster { Uid = 1, PokemonId = 150, Id = "user1", Ping = "test" };
        var entity = mapper.Map<MonsterEntity>(model);
        Assert.Equal(1, entity.Uid);
        Assert.Equal(150, entity.PokemonId);
    }

    [Fact]
    public void MonsterCreate_MapsTo_Monster()
    {
        var mapper = CreateMapper();
        var create = new MonsterCreate();
        var model = mapper.Map<Monster>(create);
        Assert.NotNull(model);
    }

    [Fact]
    public void HumanEntity_MapsTo_Human()
    {
        var mapper = CreateMapper();
        var entity = new HumanEntity { Id = "user1", Name = "TestUser", Enabled = 1, CurrentProfileNo = 2 };
        var model = mapper.Map<Human>(entity);
        Assert.Equal("user1", model.Id);
        Assert.Equal("TestUser", model.Name);
        Assert.Equal(1, model.Enabled);
        Assert.Equal(2, model.CurrentProfileNo);
    }

    [Fact]
    public void ProfileEntity_MapsTo_Profile()
    {
        var mapper = CreateMapper();
        var entity = new ProfileEntity { Id = "user1", ProfileNo = 3, Name = "PvP" };
        var model = mapper.Map<PGAN.Poracle.Web.Core.Models.Profile>(entity);
        Assert.Equal("user1", model.Id);
        Assert.Equal(3, model.ProfileNo);
        Assert.Equal("PvP", model.Name);
    }

    [Fact]
    public void PwebSettingEntity_MapsTo_PwebSetting()
    {
        var mapper = CreateMapper();
        var entity = new PwebSettingEntity { Setting = "key", Value = "val" };
        var model = mapper.Map<PwebSetting>(entity);
        Assert.Equal("key", model.Setting);
        Assert.Equal("val", model.Value);
    }
}
