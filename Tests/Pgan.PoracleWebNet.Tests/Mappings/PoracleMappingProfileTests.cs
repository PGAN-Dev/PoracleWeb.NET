using System.Text.Json;
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

    // ── RaidCreate.ToRaid ─────────────────────────────────────

    [Fact]
    public void RaidCreate_ToRaid_CopiesAllProperties()
    {
        var create = new RaidCreate
        {
            PokemonId = 150,
            Ping = "<@raid>",
            Distance = 1000,
            Team = 3,
            Level = 5,
            Form = 10,
            Clean = 1,
            Template = "raidTemplate",
            Move = 200,
            Evolution = 3,
            Exclusive = 1,
            GymId = "gym123",
            RsvpChanges = 1,
        };

        var model = create.ToRaid();

        Assert.Equal(150, model.PokemonId);
        Assert.Equal("<@raid>", model.Ping);
        Assert.Equal(1000, model.Distance);
        Assert.Equal(3, model.Team);
        Assert.Equal(5, model.Level);
        Assert.Equal(10, model.Form);
        Assert.Equal(1, model.Clean);
        Assert.Equal("raidTemplate", model.Template);
        Assert.Equal(200, model.Move);
        Assert.Equal(3, model.Evolution);
        Assert.Equal(1, model.Exclusive);
        Assert.Equal("gym123", model.GymId);
        Assert.Equal(1, model.RsvpChanges);
    }

    // ── EggCreate.ToEgg ─────────────────────────────────────

    [Fact]
    public void EggCreate_ToEgg_CopiesAllProperties()
    {
        var create = new EggCreate
        {
            Ping = "<@egg>",
            Distance = 750,
            Team = 2,
            Level = 3,
            Clean = 1,
            Template = "eggTemplate",
            Exclusive = 1,
            GymId = "gym456",
            RsvpChanges = 1,
        };

        var model = create.ToEgg();

        Assert.Equal("<@egg>", model.Ping);
        Assert.Equal(750, model.Distance);
        Assert.Equal(2, model.Team);
        Assert.Equal(3, model.Level);
        Assert.Equal(1, model.Clean);
        Assert.Equal("eggTemplate", model.Template);
        Assert.Equal(1, model.Exclusive);
        Assert.Equal("gym456", model.GymId);
        Assert.Equal(1, model.RsvpChanges);
    }

    // ── QuestCreate.ToQuest ─────────────────────────────────

    [Fact]
    public void QuestCreate_ToQuest_CopiesAllProperties()
    {
        var create = new QuestCreate
        {
            Ping = "<@quest>",
            Distance = 300,
            Reward = 25,
            RewardType = 2,
            Shiny = 1,
            Clean = 1,
            Template = "questTemplate",
            Form = 7,
        };

        var model = create.ToQuest();

        Assert.Equal("<@quest>", model.Ping);
        Assert.Equal(300, model.Distance);
        Assert.Equal(25, model.Reward);
        Assert.Equal(2, model.RewardType);
        Assert.Equal(1, model.Shiny);
        Assert.Equal(1, model.Clean);
        Assert.Equal("questTemplate", model.Template);
        Assert.Equal(7, model.Form);
    }

    // ── InvasionCreate.ToInvasion ───────────────────────────

    [Fact]
    public void InvasionCreate_ToInvasion_CopiesAllProperties()
    {
        var create = new InvasionCreate
        {
            Ping = "<@invasion>",
            Distance = 400,
            Gender = 2,
            GruntType = "fire",
            Clean = 1,
            Template = "invasionTemplate",
        };

        var model = create.ToInvasion();

        Assert.Equal("<@invasion>", model.Ping);
        Assert.Equal(400, model.Distance);
        Assert.Equal(2, model.Gender);
        Assert.Equal("fire", model.GruntType);
        Assert.Equal(1, model.Clean);
        Assert.Equal("invasionTemplate", model.Template);
    }

    // ── LureCreate.ToLure ───────────────────────────────────

    [Fact]
    public void LureCreate_ToLure_CopiesAllProperties()
    {
        var create = new LureCreate
        {
            Ping = "<@lure>",
            Distance = 250,
            LureId = 501,
            Clean = 1,
            Template = "lureTemplate",
        };

        var model = create.ToLure();

        Assert.Equal("<@lure>", model.Ping);
        Assert.Equal(250, model.Distance);
        Assert.Equal(501, model.LureId);
        Assert.Equal(1, model.Clean);
        Assert.Equal("lureTemplate", model.Template);
    }

    // ── NestCreate.ToNest ───────────────────────────────────

    [Fact]
    public void NestCreate_ToNest_CopiesAllProperties()
    {
        var create = new NestCreate
        {
            Ping = "<@nest>",
            Distance = 600,
            PokemonId = 92,
            MinSpawnAvg = 5,
            Form = 3,
            Clean = 1,
            Template = "nestTemplate",
        };

        var model = create.ToNest();

        Assert.Equal("<@nest>", model.Ping);
        Assert.Equal(600, model.Distance);
        Assert.Equal(92, model.PokemonId);
        Assert.Equal(5, model.MinSpawnAvg);
        Assert.Equal(3, model.Form);
        Assert.Equal(1, model.Clean);
        Assert.Equal("nestTemplate", model.Template);
    }

    // ── GymCreate.ToGym ─────────────────────────────────────

    [Fact]
    public void GymCreate_ToGym_CopiesAllProperties()
    {
        var create = new GymCreate
        {
            Ping = "<@gym>",
            Distance = 800,
            Team = 1,
            SlotChanges = 1,
            Clean = 1,
            Template = "gymTemplate",
            BattleChanges = 1,
            GymId = "gym789",
        };

        var model = create.ToGym();

        Assert.Equal("<@gym>", model.Ping);
        Assert.Equal(800, model.Distance);
        Assert.Equal(1, model.Team);
        Assert.Equal(1, model.SlotChanges);
        Assert.Equal(1, model.Clean);
        Assert.Equal("gymTemplate", model.Template);
        Assert.Equal(1, model.BattleChanges);
        Assert.Equal("gym789", model.GymId);
    }

    // ── FortChangeCreate.ToFortChange ───────────────────────

    [Fact]
    public void FortChangeCreate_ToFortChange_CopiesAllProperties()
    {
        var create = new FortChangeCreate
        {
            Ping = "<@fort>",
            Distance = 150,
            FortType = "pokestop",
            IncludeEmpty = 1,
            ChangeTypes = ["name", "location"],
            Clean = 1,
            Template = "fortTemplate",
        };

        var model = create.ToFortChange();

        Assert.Equal("<@fort>", model.Ping);
        Assert.Equal(150, model.Distance);
        Assert.Equal("pokestop", model.FortType);
        Assert.Equal(1, model.IncludeEmpty);
        Assert.Equal(["name", "location"], model.ChangeTypes);
        Assert.Equal(1, model.Clean);
        Assert.Equal("fortTemplate", model.Template);
    }

    // ── MonsterUpdate.ApplyUpdate — null-skip behavior ──────

    [Fact]
    public void MonsterUpdate_ApplyUpdate_NullPreservesExisting()
    {
        var existing = new Monster
        {
            Uid = 10,
            PokemonId = 25,
            Ping = "<@orig>",
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
            Template = "origTemplate",
        };

        var update = new MonsterUpdate();

        update.ApplyUpdate(existing);

        Assert.Equal(10, existing.Uid);
        Assert.Equal(25, existing.PokemonId);
        Assert.Equal("<@orig>", existing.Ping);
        Assert.Equal(500, existing.Distance);
        Assert.Equal(90, existing.MinIv);
        Assert.Equal(100, existing.MaxIv);
        Assert.Equal(1000, existing.MinCp);
        Assert.Equal(5000, existing.MaxCp);
        Assert.Equal(30, existing.MinLevel);
        Assert.Equal(50, existing.MaxLevel);
        Assert.Equal(10, existing.MinWeight);
        Assert.Equal(999, existing.MaxWeight);
        Assert.Equal(15, existing.Atk);
        Assert.Equal(14, existing.Def);
        Assert.Equal(13, existing.Sta);
        Assert.Equal(15, existing.MaxAtk);
        Assert.Equal(15, existing.MaxDef);
        Assert.Equal(15, existing.MaxSta);
        Assert.Equal(100, existing.PvpRankingWorst);
        Assert.Equal(1, existing.PvpRankingBest);
        Assert.Equal(2500, existing.PvpRankingMinCp);
        Assert.Equal(2500, existing.PvpRankingLeague);
        Assert.Equal(42, existing.Form);
        Assert.Equal(3, existing.Size);
        Assert.Equal(5, existing.MaxSize);
        Assert.Equal(1, existing.Gender);
        Assert.Equal(1, existing.Clean);
        Assert.Equal("origTemplate", existing.Template);
    }

    [Fact]
    public void MonsterUpdate_ApplyUpdate_PartialOverwrite()
    {
        var existing = new Monster
        {
            Uid = 10,
            PokemonId = 25,
            Ping = "<@orig>",
            Distance = 500,
            MinIv = 90,
            MaxIv = 100,
            MinCp = 1000,
            MaxCp = 5000,
            MinLevel = 30,
            MaxLevel = 50,
            Form = 42,
            Clean = 1,
            Template = "origTemplate",
        };

        var update = new MonsterUpdate
        {
            MinIv = 95,
            Distance = 999,
            Template = "newTemplate",
        };

        update.ApplyUpdate(existing);

        Assert.Equal(95, existing.MinIv);
        Assert.Equal(999, existing.Distance);
        Assert.Equal("newTemplate", existing.Template);
        // Preserved
        Assert.Equal(10, existing.Uid);
        Assert.Equal(25, existing.PokemonId);
        Assert.Equal("<@orig>", existing.Ping);
        Assert.Equal(100, existing.MaxIv);
        Assert.Equal(1000, existing.MinCp);
        Assert.Equal(5000, existing.MaxCp);
        Assert.Equal(30, existing.MinLevel);
        Assert.Equal(50, existing.MaxLevel);
        Assert.Equal(42, existing.Form);
        Assert.Equal(1, existing.Clean);
    }

    // ── RaidUpdate.ApplyUpdate — null-skip behavior ─────────

    [Fact]
    public void RaidUpdate_ApplyUpdate_NullPreservesExisting()
    {
        var existing = new Raid
        {
            Uid = 5,
            PokemonId = 150,
            Ping = "<@orig>",
            Distance = 1000,
            Team = 3,
            Level = 5,
            Form = 10,
            Clean = 1,
            Template = "origTemplate",
            Move = 200,
            Evolution = 3,
            Exclusive = 1,
            GymId = "gym123",
            RsvpChanges = 1,
        };

        var update = new RaidUpdate();

        update.ApplyUpdate(existing);

        Assert.Equal(5, existing.Uid);
        Assert.Equal(150, existing.PokemonId);
        Assert.Equal("<@orig>", existing.Ping);
        Assert.Equal(1000, existing.Distance);
        Assert.Equal(3, existing.Team);
        Assert.Equal(5, existing.Level);
        Assert.Equal(10, existing.Form);
        Assert.Equal(1, existing.Clean);
        Assert.Equal("origTemplate", existing.Template);
        Assert.Equal(200, existing.Move);
        Assert.Equal(3, existing.Evolution);
        Assert.Equal(1, existing.Exclusive);
        Assert.Equal("gym123", existing.GymId);
        Assert.Equal(1, existing.RsvpChanges);
    }

    [Fact]
    public void RaidUpdate_ApplyUpdate_PartialOverwrite()
    {
        var existing = new Raid
        {
            Uid = 5,
            PokemonId = 150,
            Ping = "<@orig>",
            Distance = 1000,
            Team = 3,
            Level = 5,
            Form = 10,
            Clean = 1,
            Template = "origTemplate",
            Move = 200,
            Evolution = 3,
            Exclusive = 1,
            GymId = "gym123",
            RsvpChanges = 1,
        };

        var update = new RaidUpdate
        {
            Level = 6,
            Distance = 500,
            GymId = "newGym",
        };

        update.ApplyUpdate(existing);

        Assert.Equal(6, existing.Level);
        Assert.Equal(500, existing.Distance);
        Assert.Equal("newGym", existing.GymId);
        // Preserved
        Assert.Equal(5, existing.Uid);
        Assert.Equal(150, existing.PokemonId);
        Assert.Equal("<@orig>", existing.Ping);
        Assert.Equal(3, existing.Team);
        Assert.Equal(10, existing.Form);
        Assert.Equal(1, existing.Clean);
        Assert.Equal("origTemplate", existing.Template);
        Assert.Equal(200, existing.Move);
        Assert.Equal(3, existing.Evolution);
        Assert.Equal(1, existing.Exclusive);
        Assert.Equal(1, existing.RsvpChanges);
    }

    // ── EggUpdate.ApplyUpdate — null-skip behavior ──────────

    [Fact]
    public void EggUpdate_ApplyUpdate_NullPreservesExisting()
    {
        var existing = new Egg
        {
            Uid = 7,
            Ping = "<@orig>",
            Distance = 750,
            Team = 2,
            Level = 3,
            Clean = 1,
            Template = "origTemplate",
            Exclusive = 1,
            GymId = "gym456",
            RsvpChanges = 1,
        };

        var update = new EggUpdate();

        update.ApplyUpdate(existing);

        Assert.Equal(7, existing.Uid);
        Assert.Equal("<@orig>", existing.Ping);
        Assert.Equal(750, existing.Distance);
        Assert.Equal(2, existing.Team);
        Assert.Equal(3, existing.Level);
        Assert.Equal(1, existing.Clean);
        Assert.Equal("origTemplate", existing.Template);
        Assert.Equal(1, existing.Exclusive);
        Assert.Equal("gym456", existing.GymId);
        Assert.Equal(1, existing.RsvpChanges);
    }

    [Fact]
    public void EggUpdate_ApplyUpdate_PartialOverwrite()
    {
        var existing = new Egg
        {
            Uid = 7,
            Ping = "<@orig>",
            Distance = 750,
            Team = 2,
            Level = 3,
            Clean = 1,
            Template = "origTemplate",
            Exclusive = 1,
            GymId = "gym456",
            RsvpChanges = 1,
        };

        var update = new EggUpdate
        {
            Team = 1,
            Level = 5,
            Exclusive = 0,
        };

        update.ApplyUpdate(existing);

        Assert.Equal(1, existing.Team);
        Assert.Equal(5, existing.Level);
        Assert.Equal(0, existing.Exclusive);
        // Preserved
        Assert.Equal(7, existing.Uid);
        Assert.Equal("<@orig>", existing.Ping);
        Assert.Equal(750, existing.Distance);
        Assert.Equal(1, existing.Clean);
        Assert.Equal("origTemplate", existing.Template);
        Assert.Equal("gym456", existing.GymId);
        Assert.Equal(1, existing.RsvpChanges);
    }

    // ── QuestUpdate.ApplyUpdate — null-skip behavior ────────

    [Fact]
    public void QuestUpdate_ApplyUpdate_NullPreservesExisting()
    {
        var existing = new Quest
        {
            Uid = 12,
            Ping = "<@orig>",
            Distance = 300,
            Reward = 25,
            RewardType = 2,
            Shiny = 1,
            Clean = 1,
            Template = "origTemplate",
            Form = 7,
        };

        var update = new QuestUpdate();

        update.ApplyUpdate(existing);

        Assert.Equal(12, existing.Uid);
        Assert.Equal("<@orig>", existing.Ping);
        Assert.Equal(300, existing.Distance);
        Assert.Equal(25, existing.Reward);
        Assert.Equal(2, existing.RewardType);
        Assert.Equal(1, existing.Shiny);
        Assert.Equal(1, existing.Clean);
        Assert.Equal("origTemplate", existing.Template);
        Assert.Equal(7, existing.Form);
    }

    [Fact]
    public void QuestUpdate_ApplyUpdate_PartialOverwrite()
    {
        var existing = new Quest
        {
            Uid = 12,
            Ping = "<@orig>",
            Distance = 300,
            Reward = 25,
            RewardType = 2,
            Shiny = 1,
            Clean = 1,
            Template = "origTemplate",
            Form = 7,
        };

        var update = new QuestUpdate
        {
            Reward = 50,
            Shiny = 0,
            Template = "newTemplate",
        };

        update.ApplyUpdate(existing);

        Assert.Equal(50, existing.Reward);
        Assert.Equal(0, existing.Shiny);
        Assert.Equal("newTemplate", existing.Template);
        // Preserved
        Assert.Equal(12, existing.Uid);
        Assert.Equal("<@orig>", existing.Ping);
        Assert.Equal(300, existing.Distance);
        Assert.Equal(2, existing.RewardType);
        Assert.Equal(1, existing.Clean);
        Assert.Equal(7, existing.Form);
    }

    // ── InvasionUpdate.ApplyUpdate — null-skip behavior ─────

    [Fact]
    public void InvasionUpdate_ApplyUpdate_NullPreservesExisting()
    {
        var existing = new Invasion
        {
            Uid = 20,
            Ping = "<@orig>",
            Distance = 400,
            Gender = 2,
            GruntType = "fire",
            Clean = 1,
            Template = "origTemplate",
        };

        var update = new InvasionUpdate();

        update.ApplyUpdate(existing);

        Assert.Equal(20, existing.Uid);
        Assert.Equal("<@orig>", existing.Ping);
        Assert.Equal(400, existing.Distance);
        Assert.Equal(2, existing.Gender);
        Assert.Equal("fire", existing.GruntType);
        Assert.Equal(1, existing.Clean);
        Assert.Equal("origTemplate", existing.Template);
    }

    [Fact]
    public void InvasionUpdate_ApplyUpdate_PartialOverwrite()
    {
        var existing = new Invasion
        {
            Uid = 20,
            Ping = "<@orig>",
            Distance = 400,
            Gender = 2,
            GruntType = "fire",
            Clean = 1,
            Template = "origTemplate",
        };

        var update = new InvasionUpdate
        {
            Gender = 1,
            GruntType = "water",
        };

        update.ApplyUpdate(existing);

        Assert.Equal(1, existing.Gender);
        Assert.Equal("water", existing.GruntType);
        // Preserved
        Assert.Equal(20, existing.Uid);
        Assert.Equal("<@orig>", existing.Ping);
        Assert.Equal(400, existing.Distance);
        Assert.Equal(1, existing.Clean);
        Assert.Equal("origTemplate", existing.Template);
    }

    // ── LureUpdate.ApplyUpdate — null-skip behavior ─────────

    [Fact]
    public void LureUpdate_ApplyUpdate_NullPreservesExisting()
    {
        var existing = new Lure
        {
            Uid = 30,
            Ping = "<@orig>",
            Distance = 250,
            LureId = 501,
            Clean = 1,
            Template = "origTemplate",
        };

        var update = new LureUpdate();

        update.ApplyUpdate(existing);

        Assert.Equal(30, existing.Uid);
        Assert.Equal("<@orig>", existing.Ping);
        Assert.Equal(250, existing.Distance);
        Assert.Equal(501, existing.LureId);
        Assert.Equal(1, existing.Clean);
        Assert.Equal("origTemplate", existing.Template);
    }

    [Fact]
    public void LureUpdate_ApplyUpdate_PartialOverwrite()
    {
        var existing = new Lure
        {
            Uid = 30,
            Ping = "<@orig>",
            Distance = 250,
            LureId = 501,
            Clean = 1,
            Template = "origTemplate",
        };

        var update = new LureUpdate
        {
            LureId = 502,
            Distance = 100,
        };

        update.ApplyUpdate(existing);

        Assert.Equal(502, existing.LureId);
        Assert.Equal(100, existing.Distance);
        // Preserved
        Assert.Equal(30, existing.Uid);
        Assert.Equal("<@orig>", existing.Ping);
        Assert.Equal(1, existing.Clean);
        Assert.Equal("origTemplate", existing.Template);
    }

    // ── NestUpdate.ApplyUpdate — null-skip behavior ─────────

    [Fact]
    public void NestUpdate_ApplyUpdate_NullPreservesExisting()
    {
        var existing = new Nest
        {
            Uid = 40,
            Ping = "<@orig>",
            Distance = 600,
            PokemonId = 92,
            MinSpawnAvg = 5,
            Form = 3,
            Clean = 1,
            Template = "origTemplate",
        };

        var update = new NestUpdate();

        update.ApplyUpdate(existing);

        Assert.Equal(40, existing.Uid);
        Assert.Equal("<@orig>", existing.Ping);
        Assert.Equal(600, existing.Distance);
        Assert.Equal(92, existing.PokemonId);
        Assert.Equal(5, existing.MinSpawnAvg);
        Assert.Equal(3, existing.Form);
        Assert.Equal(1, existing.Clean);
        Assert.Equal("origTemplate", existing.Template);
    }

    [Fact]
    public void NestUpdate_ApplyUpdate_PartialOverwrite()
    {
        var existing = new Nest
        {
            Uid = 40,
            Ping = "<@orig>",
            Distance = 600,
            PokemonId = 92,
            MinSpawnAvg = 5,
            Form = 3,
            Clean = 1,
            Template = "origTemplate",
        };

        var update = new NestUpdate
        {
            MinSpawnAvg = 10,
            Form = 0,
        };

        update.ApplyUpdate(existing);

        Assert.Equal(10, existing.MinSpawnAvg);
        Assert.Equal(0, existing.Form);
        // Preserved
        Assert.Equal(40, existing.Uid);
        Assert.Equal("<@orig>", existing.Ping);
        Assert.Equal(600, existing.Distance);
        Assert.Equal(92, existing.PokemonId);
        Assert.Equal(1, existing.Clean);
        Assert.Equal("origTemplate", existing.Template);
    }

    // ── GymUpdate.ApplyUpdate — null-skip behavior ──────────

    [Fact]
    public void GymUpdate_ApplyUpdate_NullPreservesExisting()
    {
        var existing = new Gym
        {
            Uid = 50,
            Ping = "<@orig>",
            Distance = 800,
            Team = 1,
            SlotChanges = 1,
            Clean = 1,
            Template = "origTemplate",
            BattleChanges = 1,
            GymId = "gym789",
        };

        var update = new GymUpdate();

        update.ApplyUpdate(existing);

        Assert.Equal(50, existing.Uid);
        Assert.Equal("<@orig>", existing.Ping);
        Assert.Equal(800, existing.Distance);
        Assert.Equal(1, existing.Team);
        Assert.Equal(1, existing.SlotChanges);
        Assert.Equal(1, existing.Clean);
        Assert.Equal("origTemplate", existing.Template);
        Assert.Equal(1, existing.BattleChanges);
        Assert.Equal("gym789", existing.GymId);
    }

    [Fact]
    public void GymUpdate_ApplyUpdate_PartialOverwrite()
    {
        var existing = new Gym
        {
            Uid = 50,
            Ping = "<@orig>",
            Distance = 800,
            Team = 1,
            SlotChanges = 1,
            Clean = 1,
            Template = "origTemplate",
            BattleChanges = 1,
            GymId = "gym789",
        };

        var update = new GymUpdate
        {
            Team = 2,
            BattleChanges = 0,
            GymId = "newGym",
        };

        update.ApplyUpdate(existing);

        Assert.Equal(2, existing.Team);
        Assert.Equal(0, existing.BattleChanges);
        Assert.Equal("newGym", existing.GymId);
        // Preserved
        Assert.Equal(50, existing.Uid);
        Assert.Equal("<@orig>", existing.Ping);
        Assert.Equal(800, existing.Distance);
        Assert.Equal(1, existing.SlotChanges);
        Assert.Equal(1, existing.Clean);
        Assert.Equal("origTemplate", existing.Template);
    }

    // ── FortChangeUpdate.ApplyUpdate — null-skip behavior ───

    [Fact]
    public void FortChangeUpdate_ApplyUpdate_NullPreservesExisting()
    {
        var existing = new FortChange
        {
            Uid = 60,
            Ping = "<@orig>",
            Distance = 150,
            FortType = "pokestop",
            IncludeEmpty = 1,
            ChangeTypes = ["name", "location"],
            Clean = 1,
            Template = "origTemplate",
        };

        var update = new FortChangeUpdate();

        update.ApplyUpdate(existing);

        Assert.Equal(60, existing.Uid);
        Assert.Equal("<@orig>", existing.Ping);
        Assert.Equal(150, existing.Distance);
        Assert.Equal("pokestop", existing.FortType);
        Assert.Equal(1, existing.IncludeEmpty);
        Assert.Equal(["name", "location"], existing.ChangeTypes);
        Assert.Equal(1, existing.Clean);
        Assert.Equal("origTemplate", existing.Template);
    }

    [Fact]
    public void FortChangeUpdate_ApplyUpdate_PartialOverwrite()
    {
        var existing = new FortChange
        {
            Uid = 60,
            Ping = "<@orig>",
            Distance = 150,
            FortType = "pokestop",
            IncludeEmpty = 1,
            ChangeTypes = ["name", "location"],
            Clean = 1,
            Template = "origTemplate",
        };

        var update = new FortChangeUpdate
        {
            FortType = "gym",
            ChangeTypes = ["removal", "new"],
            IncludeEmpty = 0,
        };

        update.ApplyUpdate(existing);

        Assert.Equal("gym", existing.FortType);
        Assert.Equal(["removal", "new"], existing.ChangeTypes);
        Assert.Equal(0, existing.IncludeEmpty);
        // Preserved
        Assert.Equal(60, existing.Uid);
        Assert.Equal("<@orig>", existing.Ping);
        Assert.Equal(150, existing.Distance);
        Assert.Equal(1, existing.Clean);
        Assert.Equal("origTemplate", existing.Template);
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

    // ── SiteSettingEntity.ToModel ────────────────────────────

    [Fact]
    public void SiteSettingEntity_ToModel_MapsAllFields()
    {
        var entity = new SiteSettingEntity
        {
            Id = 7,
            Category = "branding",
            Key = "site_title",
            Value = "My Poracle",
            ValueType = "string",
        };

        var model = entity.ToModel();

        Assert.Equal(7, model.Id);
        Assert.Equal("branding", model.Category);
        Assert.Equal("site_title", model.Key);
        Assert.Equal("My Poracle", model.Value);
        Assert.Equal("string", model.ValueType);
    }

    // ── WebhookDelegateEntity.ToModel ────────────────────────

    [Fact]
    public void WebhookDelegateEntity_ToModel_MapsAllFields()
    {
        var now = DateTime.UtcNow;
        var entity = new WebhookDelegateEntity
        {
            Id = 3,
            WebhookId = "http://host:4000/webhook",
            UserId = "user42",
            CreatedAt = now,
        };

        var model = entity.ToModel();

        Assert.Equal(3, model.Id);
        Assert.Equal("http://host:4000/webhook", model.WebhookId);
        Assert.Equal("user42", model.UserId);
        Assert.Equal(now, model.CreatedAt);
    }

    // ── QuickPickAppliedStateEntity.ToModel ──────────────────

    [Fact]
    public void QuickPickAppliedStateEntity_ToModel_MapsAllFields()
    {
        var now = DateTime.UtcNow;
        var entity = new QuickPickAppliedStateEntity
        {
            UserId = "user1",
            ProfileNo = 2,
            QuickPickId = "qp1",
            AlarmType = "raid",
            AppliedAt = now,
            ExcludePokemonIdsJson = "[10,25,150]",
            TrackedUidsJson = "[100,200,300]",
        };

        var model = entity.ToModel();

        Assert.Equal("user1", model.UserId);
        Assert.Equal(2, model.ProfileNo);
        Assert.Equal("qp1", model.QuickPickId);
        Assert.Equal("raid", model.AlarmType);
        Assert.Equal(now, model.AppliedAt);
        Assert.Equal([10, 25, 150], model.ExcludePokemonIds);
        Assert.Equal([100, 200, 300], model.TrackedUids);
    }

    // ══════════════════════════════════════════════════════════
    // ToEntity() tests
    // ══════════════════════════════════════════════════════════

    [Fact]
    public void Human_ToEntity_MapsAllFieldsAndNullCoalescesStrings()
    {
        var lastChecked = DateTime.UtcNow;
        var model = new Human
        {
            Id = "user1",
            Name = null,
            Type = null,
            Enabled = 1,
            Area = null,
            Latitude = 40.7128,
            Longitude = -74.006,
            Fails = 3,
            Language = "en",
            AdminDisable = 0,
            LastChecked = lastChecked,
            DisabledDate = null,
            CurrentProfileNo = 2,
            CommunityMembership = null,
        };

        var entity = model.ToEntity();

        Assert.Equal("user1", entity.Id);
        Assert.Equal(string.Empty, entity.Name);
        Assert.Equal(string.Empty, entity.Type);
        Assert.Equal(1, entity.Enabled);
        Assert.Equal(string.Empty, entity.Area);
        Assert.Equal(40.7128, entity.Latitude);
        Assert.Equal(-74.006, entity.Longitude);
        Assert.Equal(3, entity.Fails);
        Assert.Equal("en", entity.Language);
        Assert.Equal(0, entity.AdminDisable);
        Assert.Equal(lastChecked, entity.LastChecked);
        Assert.Null(entity.DisabledDate);
        Assert.Equal(2, entity.CurrentProfileNo);
        Assert.Equal(string.Empty, entity.CommunityMembership);
    }

    [Fact]
    public void Profile_ToEntity_MapsAllFields()
    {
        var model = new Profile
        {
            Id = "user1",
            ProfileNo = 3,
            Name = "PvP",
            Area = "[\"downtown\"]",
            Latitude = 51.5074,
            Longitude = -0.1278,
        };

        var entity = model.ToEntity();

        Assert.Equal("user1", entity.Id);
        Assert.Equal(3, entity.ProfileNo);
        Assert.Equal("PvP", entity.Name);
        Assert.Equal("[\"downtown\"]", entity.Area);
        Assert.Equal(51.5074, entity.Latitude);
        Assert.Equal(-0.1278, entity.Longitude);
    }

    [Fact]
    public void PwebSetting_ToEntity_MapsAllFields()
    {
        var model = new PwebSetting { Setting = "site_title", Value = "My Site" };

        var entity = model.ToEntity();

        Assert.Equal("site_title", entity.Setting);
        Assert.Equal("My Site", entity.Value);
    }

    [Fact]
    public void UserGeofence_ToEntity_SkipsId()
    {
        var now = DateTime.UtcNow;
        var model = new UserGeofence
        {
            Id = 999,
            HumanId = "user1",
            KojiName = "my-area",
            DisplayName = "My Area",
            GroupName = "region1",
            ParentId = 5,
            PolygonJson = "[[1.0,2.0]]",
            Status = "active",
            SubmittedAt = now,
            ReviewedBy = "admin1",
            ReviewedAt = now,
            ReviewNotes = "OK",
            PromotedName = "promoted",
            DiscordThreadId = "thread1",
            CreatedAt = now,
            UpdatedAt = now,
        };

        var entity = model.ToEntity();

        // Id is intentionally NOT mapped — auto-generated by the database
        Assert.Equal(0, entity.Id);
        Assert.Equal("user1", entity.HumanId);
        Assert.Equal("my-area", entity.KojiName);
        Assert.Equal("My Area", entity.DisplayName);
        Assert.Equal("region1", entity.GroupName);
        Assert.Equal(5, entity.ParentId);
        Assert.Equal("[[1.0,2.0]]", entity.PolygonJson);
        Assert.Equal("active", entity.Status);
        Assert.Equal(now, entity.SubmittedAt);
        Assert.Equal("admin1", entity.ReviewedBy);
        Assert.Equal(now, entity.ReviewedAt);
        Assert.Equal("OK", entity.ReviewNotes);
        Assert.Equal("promoted", entity.PromotedName);
        Assert.Equal("thread1", entity.DiscordThreadId);
        Assert.Equal(now, entity.CreatedAt);
        Assert.Equal(now, entity.UpdatedAt);
    }

    [Fact]
    public void SiteSetting_ToEntity_MapsAllFields()
    {
        var model = new SiteSetting
        {
            Id = 7,
            Category = "branding",
            Key = "site_title",
            Value = "My Poracle",
            ValueType = "string",
        };

        var entity = model.ToEntity();

        Assert.Equal(7, entity.Id);
        Assert.Equal("branding", entity.Category);
        Assert.Equal("site_title", entity.Key);
        Assert.Equal("My Poracle", entity.Value);
        Assert.Equal("string", entity.ValueType);
    }

    [Fact]
    public void WebhookDelegate_ToEntity_MapsAllFields()
    {
        var now = DateTime.UtcNow;
        var model = new WebhookDelegate
        {
            Id = 5,
            WebhookId = "http://host:4000/webhook",
            UserId = "user42",
            CreatedAt = now,
        };

        var entity = model.ToEntity();

        Assert.Equal(5, entity.Id);
        Assert.Equal("http://host:4000/webhook", entity.WebhookId);
        Assert.Equal("user42", entity.UserId);
        Assert.Equal(now, entity.CreatedAt);
    }

    [Fact]
    public void QuickPickDefinition_ToEntity_SerializesFiltersAndSetsTimestamps()
    {
        var model = new QuickPickDefinition
        {
            Id = "qp1",
            Name = "100IV",
            Description = "Perfect Pokemon",
            Icon = "star",
            Category = "Pokemon",
            AlarmType = "monster",
            SortOrder = 1,
            Enabled = true,
            Scope = "global",
            OwnerUserId = null,
            Filters = new Dictionary<string, object?> { ["minIv"] = 100, ["maxIv"] = 100 },
        };

        var before = DateTime.UtcNow;
        var entity = model.ToEntity();
        var after = DateTime.UtcNow;

        Assert.Equal("qp1", entity.Id);
        Assert.Equal("100IV", entity.Name);
        Assert.Equal("Perfect Pokemon", entity.Description);
        Assert.Equal("star", entity.Icon);
        Assert.Equal("Pokemon", entity.Category);
        Assert.Equal("monster", entity.AlarmType);
        Assert.Equal(1, entity.SortOrder);
        Assert.True(entity.Enabled);
        Assert.Equal("global", entity.Scope);
        Assert.Null(entity.OwnerUserId);

        // FiltersJson should contain serialized JSON
        Assert.Contains("minIv", entity.FiltersJson);
        Assert.Contains("maxIv", entity.FiltersJson);

        // Timestamps should be near DateTime.UtcNow
        Assert.InRange(entity.CreatedAt, before, after);
        Assert.InRange(entity.UpdatedAt, before, after);
    }

    [Fact]
    public void QuickPickAppliedState_ToEntity_SerializesJsonLists()
    {
        var now = DateTime.UtcNow;
        var model = new QuickPickAppliedState
        {
            UserId = "user1",
            ProfileNo = 2,
            QuickPickId = "qp1",
            AlarmType = "monster",
            AppliedAt = now,
            ExcludePokemonIds = [10, 25, 150],
            TrackedUids = [100, 200],
        };

        var entity = model.ToEntity();

        Assert.Equal("user1", entity.UserId);
        Assert.Equal(2, entity.ProfileNo);
        Assert.Equal("qp1", entity.QuickPickId);
        Assert.Equal("monster", entity.AlarmType);
        Assert.Equal(now, entity.AppliedAt);

        // Verify JSON serialization
        Assert.Equal("[10,25,150]", entity.ExcludePokemonIdsJson);
        Assert.Equal("[100,200]", entity.TrackedUidsJson);
    }

    // ══════════════════════════════════════════════════════════
    // ApplyTo() tests
    // ══════════════════════════════════════════════════════════

    [Fact]
    public void Human_ApplyTo_UpdatesFieldsAndNullCoalesces()
    {
        var entity = new HumanEntity
        {
            Id = "user1",
            Name = "OldName",
            Type = "discord:user",
            Enabled = 1,
            Area = "[\"old\"]",
            Latitude = 10.0,
            Longitude = 20.0,
            Fails = 0,
            Language = "en",
            AdminDisable = 0,
            CurrentProfileNo = 1,
            CommunityMembership = "groupA",
        };

        var model = new Human
        {
            Id = "ignored",
            Name = null,
            Type = "telegram:user",
            Enabled = 0,
            Area = "[\"new\"]",
            Latitude = 30.0,
            Longitude = 40.0,
            Fails = 5,
            Language = "de",
            AdminDisable = 1,
            CurrentProfileNo = 3,
            CommunityMembership = null,
        };

        model.ApplyTo(entity);

        // Id is NOT changed by ApplyTo (it modifies dest fields, but Id is set by the caller)
        Assert.Equal("user1", entity.Id);
        // null Name coalesced to string.Empty
        Assert.Equal(string.Empty, entity.Name);
        Assert.Equal("telegram:user", entity.Type);
        Assert.Equal(0, entity.Enabled);
        Assert.Equal("[\"new\"]", entity.Area);
        Assert.Equal(30.0, entity.Latitude);
        Assert.Equal(40.0, entity.Longitude);
        Assert.Equal(5, entity.Fails);
        Assert.Equal("de", entity.Language);
        Assert.Equal(1, entity.AdminDisable);
        Assert.Equal(3, entity.CurrentProfileNo);
        Assert.Equal(string.Empty, entity.CommunityMembership);
    }

    [Fact]
    public void Profile_ApplyTo_UpdatesNameAreaLatLon_NotIdOrProfileNo()
    {
        var entity = new ProfileEntity
        {
            Id = "user1",
            ProfileNo = 2,
            Name = "OldName",
            Area = "[\"old\"]",
            Latitude = 10.0,
            Longitude = 20.0,
        };

        var model = new Profile
        {
            Id = "ignored",
            ProfileNo = 99,
            Name = "NewName",
            Area = "[\"new\"]",
            Latitude = 30.0,
            Longitude = 40.0,
        };

        model.ApplyTo(entity);

        // Id and ProfileNo must NOT be changed
        Assert.Equal("user1", entity.Id);
        Assert.Equal(2, entity.ProfileNo);
        // Updated fields
        Assert.Equal("NewName", entity.Name);
        Assert.Equal("[\"new\"]", entity.Area);
        Assert.Equal(30.0, entity.Latitude);
        Assert.Equal(40.0, entity.Longitude);
    }

    [Fact]
    public void PwebSetting_ApplyTo_UpdatesValueOnly()
    {
        var entity = new PwebSettingEntity { Setting = "site_title", Value = "Old" };

        var model = new PwebSetting { Setting = "different_key", Value = "New" };

        model.ApplyTo(entity);

        // Setting (key) is NOT changed
        Assert.Equal("site_title", entity.Setting);
        // Value IS changed
        Assert.Equal("New", entity.Value);
    }

    [Fact]
    public void UserGeofence_ApplyTo_SkipsIdCreatedAtUpdatedAt()
    {
        var originalCreated = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var originalUpdated = new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var entity = new UserGeofenceEntity
        {
            Id = 42,
            HumanId = "old-user",
            KojiName = "old-name",
            DisplayName = "Old Name",
            GroupName = "old-group",
            ParentId = 1,
            PolygonJson = "[[0,0]]",
            Status = "active",
            CreatedAt = originalCreated,
            UpdatedAt = originalUpdated,
        };

        var now = DateTime.UtcNow;
        var model = new UserGeofence
        {
            Id = 999,
            HumanId = "new-user",
            KojiName = "new-name",
            DisplayName = "New Name",
            GroupName = "new-group",
            ParentId = 10,
            PolygonJson = "[[1,1],[2,2]]",
            Status = "pending_review",
            SubmittedAt = now,
            ReviewedBy = "admin1",
            ReviewedAt = now,
            ReviewNotes = "Looks good",
            PromotedName = "promoted",
            DiscordThreadId = "thread1",
        };

        model.ApplyTo(entity);

        // Id, CreatedAt, UpdatedAt are NOT changed
        Assert.Equal(42, entity.Id);
        Assert.Equal(originalCreated, entity.CreatedAt);
        Assert.Equal(originalUpdated, entity.UpdatedAt);
        // All other fields are updated
        Assert.Equal("new-user", entity.HumanId);
        Assert.Equal("new-name", entity.KojiName);
        Assert.Equal("New Name", entity.DisplayName);
        Assert.Equal("new-group", entity.GroupName);
        Assert.Equal(10, entity.ParentId);
        Assert.Equal("[[1,1],[2,2]]", entity.PolygonJson);
        Assert.Equal("pending_review", entity.Status);
        Assert.Equal(now, entity.SubmittedAt);
        Assert.Equal("admin1", entity.ReviewedBy);
        Assert.Equal(now, entity.ReviewedAt);
        Assert.Equal("Looks good", entity.ReviewNotes);
        Assert.Equal("promoted", entity.PromotedName);
        Assert.Equal("thread1", entity.DiscordThreadId);
    }

    [Fact]
    public void QuickPickDefinition_ApplyTo_SerializesFiltersAndSetsUpdatedAtOnly()
    {
        var originalCreated = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var originalUpdated = new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var entity = new QuickPickDefinitionEntity
        {
            Id = "qp1",
            Name = "OldName",
            CreatedAt = originalCreated,
            UpdatedAt = originalUpdated,
            FiltersJson = "{}",
        };

        var model = new QuickPickDefinition
        {
            Id = "qp1",
            Name = "NewName",
            Description = "Updated",
            Icon = "fire",
            Category = "Raids",
            AlarmType = "raid",
            SortOrder = 5,
            Enabled = false,
            Scope = "user",
            OwnerUserId = "user1",
            Filters = new Dictionary<string, object?> { ["level"] = 5 },
        };

        var before = DateTime.UtcNow;
        model.ApplyTo(entity);
        var after = DateTime.UtcNow;

        // CreatedAt is NOT changed
        Assert.Equal(originalCreated, entity.CreatedAt);
        // UpdatedAt IS changed
        Assert.InRange(entity.UpdatedAt, before, after);
        // Other fields are updated
        Assert.Equal("NewName", entity.Name);
        Assert.Equal("Updated", entity.Description);
        Assert.Equal("fire", entity.Icon);
        Assert.Equal("Raids", entity.Category);
        Assert.Equal("raid", entity.AlarmType);
        Assert.Equal(5, entity.SortOrder);
        Assert.False(entity.Enabled);
        Assert.Equal("user", entity.Scope);
        Assert.Equal("user1", entity.OwnerUserId);
        // FiltersJson is serialized
        Assert.Contains("level", entity.FiltersJson);
    }

    // ══════════════════════════════════════════════════════════
    // JSON round-trip and edge case tests
    // ══════════════════════════════════════════════════════════

    [Fact]
    public void QuickPickDefinitionEntity_ToModel_NullFiltersJson_ReturnsEmptyDictionary()
    {
        var entity = new QuickPickDefinitionEntity
        {
            Id = "qp-null",
            Name = "NullFilters",
            FiltersJson = null!,
        };

        var model = entity.ToModel();

        Assert.NotNull(model.Filters);
        Assert.Empty(model.Filters);
    }

    [Fact]
    public void QuickPickDefinition_RoundTrip_PreservesFilters()
    {
        var original = new QuickPickDefinition
        {
            Id = "qp-rt",
            Name = "RoundTrip",
            Filters = new Dictionary<string, object?>
            {
                ["minIv"] = 90,
                ["maxIv"] = 100,
                ["pokemonId"] = 25,
            },
        };

        var entity = original.ToEntity();
        var roundTripped = entity.ToModel();

        Assert.Equal(3, roundTripped.Filters.Count);
        Assert.True(roundTripped.Filters.ContainsKey("minIv"));
        Assert.True(roundTripped.Filters.ContainsKey("maxIv"));
        Assert.True(roundTripped.Filters.ContainsKey("pokemonId"));
        // Values are JsonElements after deserialization, compare via ToString
        Assert.Equal("90", roundTripped.Filters["minIv"]?.ToString());
        Assert.Equal("100", roundTripped.Filters["maxIv"]?.ToString());
        Assert.Equal("25", roundTripped.Filters["pokemonId"]?.ToString());
    }

    [Fact]
    public void QuickPickDefinition_ApplyTo_SerializesFiltersToCamelCase()
    {
        var entity = new QuickPickDefinitionEntity
        {
            Id = "qp1",
            Name = "Test",
            FiltersJson = "{}",
        };

        var model = new QuickPickDefinition
        {
            Filters = new Dictionary<string, object?> { ["minIv"] = 100, ["maxCp"] = 5000 },
        };

        model.ApplyTo(entity);

        // Verify the JSON uses camelCase property names (keys are already camelCase strings)
        Assert.Contains("minIv", entity.FiltersJson);
        Assert.Contains("maxCp", entity.FiltersJson);
        // Verify it's valid JSON
        var parsed = JsonSerializer.Deserialize<Dictionary<string, object?>>(entity.FiltersJson);
        Assert.NotNull(parsed);
        Assert.Equal(2, parsed.Count);
    }

    [Fact]
    public void QuickPickAppliedStateEntity_ToModel_EmptyJsonFields_ReturnsEmptyLists()
    {
        var entity = new QuickPickAppliedStateEntity
        {
            UserId = "user1",
            QuickPickId = "qp1",
            ExcludePokemonIdsJson = "",
            TrackedUidsJson = "",
        };

        var model = entity.ToModel();

        Assert.NotNull(model.ExcludePokemonIds);
        Assert.Empty(model.ExcludePokemonIds);
        Assert.NotNull(model.TrackedUids);
        Assert.Empty(model.TrackedUids);
    }

    [Fact]
    public void QuickPickAppliedStateEntity_ToModel_NullExcludeJson_ReturnsEmptyList()
    {
        var entity = new QuickPickAppliedStateEntity
        {
            UserId = "user1",
            QuickPickId = "qp1",
            ExcludePokemonIdsJson = null,
            TrackedUidsJson = "[1,2,3]",
        };

        var model = entity.ToModel();

        Assert.NotNull(model.ExcludePokemonIds);
        Assert.Empty(model.ExcludePokemonIds);
        Assert.Equal([1, 2, 3], model.TrackedUids);
    }

    [Fact]
    public void QuickPickAppliedState_ToEntity_SerializesListsToJson()
    {
        var model = new QuickPickAppliedState
        {
            UserId = "user1",
            ProfileNo = 1,
            QuickPickId = "qp1",
            ExcludePokemonIds = [10, 25],
            TrackedUids = [100, 200, 300],
        };

        var entity = model.ToEntity();

        Assert.Equal("[10,25]", entity.ExcludePokemonIdsJson);
        Assert.Equal("[100,200,300]", entity.TrackedUidsJson);
    }

    [Fact]
    public void QuickPickAppliedState_RoundTrip_PreservesLists()
    {
        var now = DateTime.UtcNow;
        var original = new QuickPickAppliedState
        {
            UserId = "user1",
            ProfileNo = 2,
            QuickPickId = "qp1",
            AlarmType = "raid",
            AppliedAt = now,
            ExcludePokemonIds = [10, 25, 150],
            TrackedUids = [100, 200],
        };

        var entity = original.ToEntity();
        var roundTripped = entity.ToModel();

        Assert.Equal("user1", roundTripped.UserId);
        Assert.Equal(2, roundTripped.ProfileNo);
        Assert.Equal("qp1", roundTripped.QuickPickId);
        Assert.Equal("raid", roundTripped.AlarmType);
        Assert.Equal(now, roundTripped.AppliedAt);
        Assert.Equal([10, 25, 150], roundTripped.ExcludePokemonIds);
        Assert.Equal([100, 200], roundTripped.TrackedUids);
    }

    [Fact]
    public void UserGeofence_ToEntity_ToModel_RoundTrip_PreservesFields()
    {
        var now = DateTime.UtcNow;
        var original = new UserGeofence
        {
            Id = 42,
            HumanId = "user1",
            KojiName = "my-area",
            DisplayName = "My Area",
            GroupName = "region1",
            ParentId = 5,
            PolygonJson = "[[1.0,2.0],[3.0,4.0]]",
            Status = "pending_review",
            SubmittedAt = now,
            ReviewedBy = "admin1",
            ReviewedAt = now,
            ReviewNotes = "Good",
            PromotedName = "promoted-area",
            DiscordThreadId = "thread123",
            CreatedAt = now,
            UpdatedAt = now,
            Polygon = [[1.0, 2.0], [3.0, 4.0]],
        };

        var entity = original.ToEntity();
        var roundTripped = entity.ToModel();

        // Id is skipped by ToEntity (stays at 0), so entity.Id is 0 and roundTripped.Id is 0
        Assert.Equal(0, roundTripped.Id);
        // All other fields survive the round trip
        Assert.Equal("user1", roundTripped.HumanId);
        Assert.Equal("my-area", roundTripped.KojiName);
        Assert.Equal("My Area", roundTripped.DisplayName);
        Assert.Equal("region1", roundTripped.GroupName);
        Assert.Equal(5, roundTripped.ParentId);
        Assert.Equal("[[1.0,2.0],[3.0,4.0]]", roundTripped.PolygonJson);
        Assert.Equal("pending_review", roundTripped.Status);
        Assert.Equal(now, roundTripped.SubmittedAt);
        Assert.Equal("admin1", roundTripped.ReviewedBy);
        Assert.Equal(now, roundTripped.ReviewedAt);
        Assert.Equal("Good", roundTripped.ReviewNotes);
        Assert.Equal("promoted-area", roundTripped.PromotedName);
        Assert.Equal("thread123", roundTripped.DiscordThreadId);
        Assert.Equal(now, roundTripped.CreatedAt);
        Assert.Equal(now, roundTripped.UpdatedAt);
        // Polygon is intentionally NOT mapped by ToModel
        Assert.Null(roundTripped.Polygon);
    }
}
