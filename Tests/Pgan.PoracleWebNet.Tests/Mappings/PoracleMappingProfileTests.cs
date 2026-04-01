using AutoMapper;
using Microsoft.Extensions.DependencyInjection;
using Pgan.PoracleWebNet.Core.Mappings;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Data.Entities;

namespace Pgan.PoracleWebNet.Tests.Mappings;

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
    public void MapperCanBeCreated()
    {
        var mapper = CreateMapper();
        Assert.NotNull(mapper);
    }

    [Fact]
    public void MonsterCreateMapsToMonster()
    {
        var mapper = CreateMapper();
        var create = new MonsterCreate();
        var model = mapper.Map<Monster>(create);
        Assert.NotNull(model);
    }

    [Fact]
    public void HumanEntityMapsToHuman()
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
    public void ProfileEntityMapsToProfile()
    {
        var mapper = CreateMapper();
        var entity = new ProfileEntity { Id = "user1", ProfileNo = 3, Name = "PvP" };
        var model = mapper.Map<Core.Models.Profile>(entity);
        Assert.Equal("user1", model.Id);
        Assert.Equal(3, model.ProfileNo);
        Assert.Equal("PvP", model.Name);
    }

    [Fact]
    public void PwebSettingEntityMapsToPwebSetting()
    {
        var mapper = CreateMapper();
        var entity = new PwebSettingEntity { Setting = "key", Value = "val" };
        var model = mapper.Map<PwebSetting>(entity);
        Assert.Equal("key", model.Setting);
        Assert.Equal("val", model.Value);
    }
}
