using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

public class RaidLevelServiceTests
{
    [Fact]
    public async Task GetAllAsync_returns_19_levels_in_order()
    {
        var sut = new RaidLevelService();

        var levels = await sut.GetAllAsync();

        Assert.Equal(19, levels.Count);
        for (var i = 0; i < 19; i++)
        {
            Assert.Equal(i + 1, levels[i].Value);
        }
    }

    [Fact]
    public async Task GetAllAsync_assigns_categories_per_masterfile()
    {
        var sut = new RaidLevelService();

        var levels = (await sut.GetAllAsync()).ToDictionary(l => l.Value);

        // Star tiers 1-5
        for (var v = 1; v <= 5; v++) Assert.Equal("star", levels[v].Category);
        // Mega 6, Mega Legendary 7
        Assert.Equal("mega", levels[6].Category);
        Assert.Equal("mega", levels[7].Category);
        // Ultra Beast 8, Elite 9, Primal 10
        Assert.Equal("special", levels[8].Category);
        Assert.Equal("special", levels[9].Category);
        Assert.Equal("special", levels[10].Category);
        // Shadow 11-15
        for (var v = 11; v <= 15; v++) Assert.Equal("shadow", levels[v].Category);
        // Super Mega 16-17
        Assert.Equal("superMega", levels[16].Category);
        Assert.Equal("superMega", levels[17].Category);
        // Coordinated 18-19
        Assert.Equal("coordinated", levels[18].Category);
        Assert.Equal("coordinated", levels[19].Category);
    }

    [Fact]
    public async Task GetAllAsync_uses_canonical_masterfile_names()
    {
        var sut = new RaidLevelService();

        var levels = (await sut.GetAllAsync()).ToDictionary(l => l.Value);

        // Fixes the prior Elite mislabel: level 7 is Mega Legendary, NOT Elite
        Assert.Equal("Mega Legendary Raid", levels[7].Name);
        // Elite is at level 9
        Assert.Equal("Elite Raid", levels[9].Name);
        // Level 5 is Legendary, not "T5"
        Assert.Equal("Legendary Raid", levels[5].Name);
        // Star tiers carry the literal star nomenclature
        Assert.Equal("1 Star Raid", levels[1].Name);
        Assert.Equal("4 Star Raid", levels[4].Name);
    }

    [Fact]
    public async Task GetAllAsync_includes_plural_names()
    {
        var sut = new RaidLevelService();

        var levels = (await sut.GetAllAsync()).ToDictionary(l => l.Value);

        Assert.Equal("Mega Raids", levels[6].NamePlural);
        Assert.Equal("Elite Raids", levels[9].NamePlural);
        Assert.Equal("Legendary Raids", levels[5].NamePlural);
    }
}
