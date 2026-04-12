using Pgan.PoracleWebNet.Core.Mappings;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Data.Entities;

namespace Pgan.PoracleWebNet.Tests.Mappings;

public class MappingExtensionTests
{
    // ── MonsterCreate.ToMonster ──────────────────────────────

    [Fact]
    public void MonsterCreate_ToMonster_CopiesAllProperties()
    {
        var create = new MonsterCreate
        {
            PokemonId = 25,
            Ping = "<@123>",
            Distance = 500,
            MinIv = 90,
            MaxIv = 100,
            MinCp = 1000,
            MaxCp = 5000,
            MinLevel = 30,
            MaxLevel = 50,
            MinWeight = 10,
            MaxWeight = 999,
            Atk = 15,
            Def = 14,
            Sta = 13,
            MaxAtk = 15,
            MaxDef = 15,
            MaxSta = 15,
            PvpRankingWorst = 100,
            PvpRankingBest = 1,
            PvpRankingMinCp = 2500,
            PvpRankingLeague = 2500,
            Form = 42,
            Size = 3,
            MaxSize = 5,
            Gender = 1,
            Clean = 1,
            Template = "myTemplate",
        };

        var model = create.ToMonster();

        Assert.Equal(25, model.PokemonId);
        Assert.Equal("<@123>", model.Ping);
        Assert.Equal(500, model.Distance);
        Assert.Equal(90, model.MinIv);
        Assert.Equal(100, model.MaxIv);
        Assert.Equal(1000, model.MinCp);
        Assert.Equal(5000, model.MaxCp);
        Assert.Equal(30, model.MinLevel);
        Assert.Equal(50, model.MaxLevel);
        Assert.Equal(10, model.MinWeight);
        Assert.Equal(999, model.MaxWeight);
        Assert.Equal(15, model.Atk);
        Assert.Equal(14, model.Def);
        Assert.Equal(13, model.Sta);
        Assert.Equal(15, model.MaxAtk);
        Assert.Equal(15, model.MaxDef);
        Assert.Equal(15, model.MaxSta);
        Assert.Equal(100, model.PvpRankingWorst);
        Assert.Equal(1, model.PvpRankingBest);
        Assert.Equal(2500, model.PvpRankingMinCp);
        Assert.Equal(2500, model.PvpRankingLeague);
        Assert.Equal(42, model.Form);
        Assert.Equal(3, model.Size);
        Assert.Equal(5, model.MaxSize);
        Assert.Equal(1, model.Gender);
        Assert.Equal(1, model.Clean);
        Assert.Equal("myTemplate", model.Template);
    }

    // ── HumanEntity.ToModel ─────────────────────────────────

    [Fact]
    public void HumanEntity_ToModel_MapsAllFields()
    {
        var entity = new HumanEntity
        {
            Id = "user1",
            Name = "TestUser",
            Type = "discord:user",
            Enabled = 1,
            Area = "[]",
            Latitude = 40.7128,
            Longitude = -74.0060,
            Fails = 2,
            Language = "en",
            AdminDisable = 0,
            CurrentProfileNo = 2,
            CommunityMembership = "groupA",
        };

        var model = entity.ToModel();

        Assert.Equal("user1", model.Id);
        Assert.Equal("TestUser", model.Name);
        Assert.Equal("discord:user", model.Type);
        Assert.Equal(1, model.Enabled);
        Assert.Equal("[]", model.Area);
        Assert.Equal(40.7128, model.Latitude);
        Assert.Equal(-74.0060, model.Longitude);
        Assert.Equal(2, model.Fails);
        Assert.Equal("en", model.Language);
        Assert.Equal(0, model.AdminDisable);
        Assert.Equal(2, model.CurrentProfileNo);
        Assert.Equal("groupA", model.CommunityMembership);
    }

    // ── ProfileEntity.ToModel ───────────────────────────────

    [Fact]
    public void ProfileEntity_ToModel_MapsAllFields()
    {
        var entity = new ProfileEntity
        {
            Id = "user1",
            ProfileNo = 3,
            Name = "PvP",
            Area = "[\"downtown\"]",
            Latitude = 51.5074,
            Longitude = -0.1278,
        };

        var model = entity.ToModel();

        Assert.Equal("user1", model.Id);
        Assert.Equal(3, model.ProfileNo);
        Assert.Equal("PvP", model.Name);
        Assert.Equal("[\"downtown\"]", model.Area);
        Assert.Equal(51.5074, model.Latitude);
        Assert.Equal(-0.1278, model.Longitude);
    }

    // ── PwebSettingEntity.ToModel ────────────────────────────

    [Fact]
    public void PwebSettingEntity_ToModel_MapsAllFields()
    {
        var entity = new PwebSettingEntity { Setting = "site_title", Value = "My Poracle" };

        var model = entity.ToModel();

        Assert.Equal("site_title", model.Setting);
        Assert.Equal("My Poracle", model.Value);
    }

    // ── MaxBattleCreate.ToMaxBattle ─────────────────────────

    [Fact]
    public void MaxBattleCreate_ToMaxBattle_CopiesAllProperties()
    {
        var create = new MaxBattleCreate
        {
            PokemonId = 9000,
            Ping = "<@456>",
            Distance = 50,
            Gmax = 1,
            Level = 3,
            Form = 1,
            Clean = 0,
            Template = "maxTemplate",
            Move = 100,
            Evolution = 2,
            StationId = "station123",
        };

        var model = create.ToMaxBattle();

        Assert.Equal(9000, model.PokemonId);
        Assert.Equal("<@456>", model.Ping);
        Assert.Equal(50, model.Distance);
        Assert.Equal(1, model.Gmax);
        Assert.Equal(3, model.Level);
        Assert.Equal(1, model.Form);
        Assert.Equal(0, model.Clean);
        Assert.Equal("maxTemplate", model.Template);
        Assert.Equal(100, model.Move);
        Assert.Equal(2, model.Evolution);
        Assert.Equal("station123", model.StationId);
    }

    // ── MaxBattleUpdate.ApplyUpdate — null-skip behavior ────

    [Fact]
    public void MaxBattleUpdate_ApplyUpdate_NullPropertiesPreserveExistingValues()
    {
        var existing = new MaxBattle
        {
            Uid = 1,
            PokemonId = 9000,
            Ping = "<@original>",
            Distance = 50,
            Gmax = 1,
            Level = 3,
            Form = 7,
            Clean = 0,
            Template = "origTemplate",
            Move = 200,
            Evolution = 2,
            StationId = "station123",
        };

        // All properties null — nothing should change
        var update = new MaxBattleUpdate();

        update.ApplyUpdate(existing);

        Assert.Equal(1, existing.Uid);
        Assert.Equal(9000, existing.PokemonId);
        Assert.Equal("<@original>", existing.Ping);
        Assert.Equal(50, existing.Distance);
        Assert.Equal(1, existing.Gmax);
        Assert.Equal(3, existing.Level);
        Assert.Equal(7, existing.Form);
        Assert.Equal(0, existing.Clean);
        Assert.Equal("origTemplate", existing.Template);
        Assert.Equal(200, existing.Move);
        Assert.Equal(2, existing.Evolution);
        Assert.Equal("station123", existing.StationId);
    }

    [Fact]
    public void MaxBattleUpdate_ApplyUpdate_NonNullPropertiesOverwriteExisting()
    {
        var existing = new MaxBattle
        {
            Uid = 1,
            PokemonId = 9000,
            Ping = "<@original>",
            Distance = 50,
            Gmax = 1,
            Level = 3,
            Form = 7,
            Clean = 0,
            Template = "origTemplate",
            Move = 200,
            Evolution = 2,
            StationId = "station123",
        };

        var update = new MaxBattleUpdate
        {
            Gmax = 0,           // explicitly set to 0 (should overwrite)
            Level = 5,          // explicitly set
            StationId = null,   // null — should preserve existing
            Template = null,    // null — should preserve existing
            Distance = 999,     // explicitly set
        };

        update.ApplyUpdate(existing);

        Assert.Equal(0, existing.Gmax);               // overwritten to 0
        Assert.Equal(5, existing.Level);               // overwritten to 5
        Assert.Equal(999, existing.Distance);          // overwritten to 999
        Assert.Equal("station123", existing.StationId); // preserved (null in update)
        Assert.Equal("origTemplate", existing.Template); // preserved (null in update)
        // Unrelated fields remain unchanged
        Assert.Equal(1, existing.Uid);
        Assert.Equal(9000, existing.PokemonId);
        Assert.Equal("<@original>", existing.Ping);
        Assert.Equal(7, existing.Form);
        Assert.Equal(0, existing.Clean);
        Assert.Equal(200, existing.Move);
        Assert.Equal(2, existing.Evolution);
    }

    [Fact]
    public void MaxBattleUpdate_ApplyUpdate_StringFieldsOverwriteWhenNonNull()
    {
        var existing = new MaxBattle
        {
            Ping = "<@old>",
            Template = "oldTemplate",
            StationId = "oldStation",
        };

        var update = new MaxBattleUpdate
        {
            Ping = "<@new>",
            Template = "newTemplate",
            StationId = "newStation",
        };

        update.ApplyUpdate(existing);

        Assert.Equal("<@new>", existing.Ping);
        Assert.Equal("newTemplate", existing.Template);
        Assert.Equal("newStation", existing.StationId);
    }

    // ── QuickPickDefinitionEntity.ToModel ───────────────────

    [Fact]
    public void QuickPickDefinitionEntity_ToModel_DeserializesFiltersJson()
    {
        var entity = new QuickPickDefinitionEntity
        {
            Id = "qp1",
            Name = "100IV Pokemon",
            Description = "Tracks perfect Pokemon",
            Icon = "star",
            Category = "Pokemon",
            AlarmType = "monster",
            SortOrder = 1,
            Enabled = true,
            Scope = "global",
            OwnerUserId = null,
            FiltersJson = """{"minIv":100,"maxIv":100}""",
        };

        var model = entity.ToModel();

        Assert.Equal("qp1", model.Id);
        Assert.Equal("100IV Pokemon", model.Name);
        Assert.Equal("Tracks perfect Pokemon", model.Description);
        Assert.Equal("star", model.Icon);
        Assert.Equal("Pokemon", model.Category);
        Assert.Equal("monster", model.AlarmType);
        Assert.Equal(1, model.SortOrder);
        Assert.True(model.Enabled);
        Assert.Equal("global", model.Scope);
        Assert.Null(model.OwnerUserId);
        Assert.NotNull(model.Filters);
        Assert.Equal(2, model.Filters.Count);
        Assert.True(model.Filters.ContainsKey("minIv"));
        Assert.True(model.Filters.ContainsKey("maxIv"));
    }

    [Fact]
    public void QuickPickDefinitionEntity_ToModel_EmptyFiltersJson_ReturnsEmptyDictionary()
    {
        var entity = new QuickPickDefinitionEntity
        {
            Id = "qp2",
            Name = "Empty",
            FiltersJson = "",
        };

        var model = entity.ToModel();

        Assert.NotNull(model.Filters);
        Assert.Empty(model.Filters);
    }

    // ── UserGeofenceEntity.ToModel ──────────────────────────

    [Fact]
    public void UserGeofenceEntity_ToModel_PolygonIsNull()
    {
        var entity = new UserGeofenceEntity
        {
            Id = 42,
            HumanId = "user99",
            KojiName = "my-area",
            DisplayName = "My Area",
            GroupName = "region1",
            ParentId = 5,
            PolygonJson = "[[1.0,2.0],[3.0,4.0]]",
            Status = "active",
            ReviewedBy = "admin1",
            ReviewNotes = "Looks good",
            PromotedName = null,
            DiscordThreadId = "thread123",
        };

        var model = entity.ToModel();

        Assert.Equal(42, model.Id);
        Assert.Equal("user99", model.HumanId);
        Assert.Equal("my-area", model.KojiName);
        Assert.Equal("My Area", model.DisplayName);
        Assert.Equal("region1", model.GroupName);
        Assert.Equal(5, model.ParentId);
        Assert.Equal("[[1.0,2.0],[3.0,4.0]]", model.PolygonJson);
        Assert.Equal("active", model.Status);
        Assert.Equal("admin1", model.ReviewedBy);
        Assert.Equal("Looks good", model.ReviewNotes);
        Assert.Null(model.PromotedName);
        Assert.Equal("thread123", model.DiscordThreadId);
        // Polygon is intentionally NOT mapped — populated by the service layer
        Assert.Null(model.Polygon);
    }
}
