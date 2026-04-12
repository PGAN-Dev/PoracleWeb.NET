using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Mappings;

public static class AlarmMappingExtensions
{
    // ── Monster ──────────────────────────────────────────────

    public static Monster ToMonster(this MonsterCreate src) => new()
    {
        PokemonId = src.PokemonId,
        Ping = src.Ping,
        Distance = src.Distance,
        MinIv = src.MinIv,
        MaxIv = src.MaxIv,
        MinCp = src.MinCp,
        MaxCp = src.MaxCp,
        MinLevel = src.MinLevel,
        MaxLevel = src.MaxLevel,
        MinWeight = src.MinWeight,
        MaxWeight = src.MaxWeight,
        Atk = src.Atk,
        Def = src.Def,
        Sta = src.Sta,
        MaxAtk = src.MaxAtk,
        MaxDef = src.MaxDef,
        MaxSta = src.MaxSta,
        PvpRankingWorst = src.PvpRankingWorst,
        PvpRankingBest = src.PvpRankingBest,
        PvpRankingMinCp = src.PvpRankingMinCp,
        PvpRankingLeague = src.PvpRankingLeague,
        Form = src.Form,
        Size = src.Size,
        MaxSize = src.MaxSize,
        Gender = src.Gender,
        Clean = src.Clean,
        Template = src.Template,
    };

    public static void ApplyUpdate(this MonsterUpdate src, Monster dest)
    {
        if (src.Ping != null) dest.Ping = src.Ping;
        if (src.Distance != null) dest.Distance = src.Distance.Value;
        if (src.MinIv != null) dest.MinIv = src.MinIv.Value;
        if (src.MaxIv != null) dest.MaxIv = src.MaxIv.Value;
        if (src.MinCp != null) dest.MinCp = src.MinCp.Value;
        if (src.MaxCp != null) dest.MaxCp = src.MaxCp.Value;
        if (src.MinLevel != null) dest.MinLevel = src.MinLevel.Value;
        if (src.MaxLevel != null) dest.MaxLevel = src.MaxLevel.Value;
        if (src.MinWeight != null) dest.MinWeight = src.MinWeight.Value;
        if (src.MaxWeight != null) dest.MaxWeight = src.MaxWeight.Value;
        if (src.Atk != null) dest.Atk = src.Atk.Value;
        if (src.Def != null) dest.Def = src.Def.Value;
        if (src.Sta != null) dest.Sta = src.Sta.Value;
        if (src.MaxAtk != null) dest.MaxAtk = src.MaxAtk.Value;
        if (src.MaxDef != null) dest.MaxDef = src.MaxDef.Value;
        if (src.MaxSta != null) dest.MaxSta = src.MaxSta.Value;
        if (src.PvpRankingWorst != null) dest.PvpRankingWorst = src.PvpRankingWorst.Value;
        if (src.PvpRankingBest != null) dest.PvpRankingBest = src.PvpRankingBest.Value;
        if (src.PvpRankingMinCp != null) dest.PvpRankingMinCp = src.PvpRankingMinCp.Value;
        if (src.PvpRankingLeague != null) dest.PvpRankingLeague = src.PvpRankingLeague.Value;
        if (src.Form != null) dest.Form = src.Form.Value;
        if (src.Size != null) dest.Size = src.Size.Value;
        if (src.MaxSize != null) dest.MaxSize = src.MaxSize.Value;
        if (src.Gender != null) dest.Gender = src.Gender.Value;
        if (src.Clean != null) dest.Clean = src.Clean.Value;
        if (src.Template != null) dest.Template = src.Template;
    }

    // ── Raid ─────────────────────────────────────────────────

    public static Raid ToRaid(this RaidCreate src) => new()
    {
        PokemonId = src.PokemonId,
        Ping = src.Ping,
        Distance = src.Distance,
        Team = src.Team,
        Level = src.Level,
        Form = src.Form,
        Clean = src.Clean,
        Template = src.Template,
        Move = src.Move,
        Evolution = src.Evolution,
        Exclusive = src.Exclusive,
        GymId = src.GymId,
        RsvpChanges = src.RsvpChanges,
    };

    public static void ApplyUpdate(this RaidUpdate src, Raid dest)
    {
        if (src.Ping != null) dest.Ping = src.Ping;
        if (src.Distance != null) dest.Distance = src.Distance.Value;
        if (src.Team != null) dest.Team = src.Team.Value;
        if (src.Level != null) dest.Level = src.Level.Value;
        if (src.Form != null) dest.Form = src.Form.Value;
        if (src.Clean != null) dest.Clean = src.Clean.Value;
        if (src.Template != null) dest.Template = src.Template;
        if (src.Move != null) dest.Move = src.Move.Value;
        if (src.Evolution != null) dest.Evolution = src.Evolution.Value;
        if (src.Exclusive != null) dest.Exclusive = src.Exclusive.Value;
        if (src.GymId != null) dest.GymId = src.GymId;
        if (src.RsvpChanges != null) dest.RsvpChanges = src.RsvpChanges.Value;
    }

    // ── Egg ──────────────────────────────────────────────────

    public static Egg ToEgg(this EggCreate src) => new()
    {
        Ping = src.Ping,
        Distance = src.Distance,
        Team = src.Team,
        Level = src.Level,
        Clean = src.Clean,
        Template = src.Template,
        Exclusive = src.Exclusive,
        GymId = src.GymId,
        RsvpChanges = src.RsvpChanges,
    };

    public static void ApplyUpdate(this EggUpdate src, Egg dest)
    {
        if (src.Ping != null) dest.Ping = src.Ping;
        if (src.Distance != null) dest.Distance = src.Distance.Value;
        if (src.Team != null) dest.Team = src.Team.Value;
        if (src.Level != null) dest.Level = src.Level.Value;
        if (src.Clean != null) dest.Clean = src.Clean.Value;
        if (src.Template != null) dest.Template = src.Template;
        if (src.Exclusive != null) dest.Exclusive = src.Exclusive.Value;
        if (src.GymId != null) dest.GymId = src.GymId;
        if (src.RsvpChanges != null) dest.RsvpChanges = src.RsvpChanges.Value;
    }

    // ── Quest ────────────────────────────────────────────────

    public static Quest ToQuest(this QuestCreate src) => new()
    {
        Ping = src.Ping,
        Distance = src.Distance,
        Reward = src.Reward,
        RewardType = src.RewardType,
        Shiny = src.Shiny,
        Clean = src.Clean,
        Template = src.Template,
        Form = src.Form,
    };

    public static void ApplyUpdate(this QuestUpdate src, Quest dest)
    {
        if (src.Ping != null) dest.Ping = src.Ping;
        if (src.Distance != null) dest.Distance = src.Distance.Value;
        if (src.Reward != null) dest.Reward = src.Reward.Value;
        if (src.RewardType != null) dest.RewardType = src.RewardType.Value;
        if (src.Shiny != null) dest.Shiny = src.Shiny.Value;
        if (src.Clean != null) dest.Clean = src.Clean.Value;
        if (src.Template != null) dest.Template = src.Template;
        if (src.Form != null) dest.Form = src.Form.Value;
    }

    // ── Invasion ─────────────────────────────────────────────

    public static Invasion ToInvasion(this InvasionCreate src) => new()
    {
        Ping = src.Ping,
        Distance = src.Distance,
        Gender = src.Gender,
        GruntType = src.GruntType,
        Clean = src.Clean,
        Template = src.Template,
    };

    public static void ApplyUpdate(this InvasionUpdate src, Invasion dest)
    {
        if (src.Ping != null) dest.Ping = src.Ping;
        if (src.Distance != null) dest.Distance = src.Distance.Value;
        if (src.Gender != null) dest.Gender = src.Gender.Value;
        if (src.GruntType != null) dest.GruntType = src.GruntType;
        if (src.Clean != null) dest.Clean = src.Clean.Value;
        if (src.Template != null) dest.Template = src.Template;
    }

    // ── Lure ─────────────────────────────────────────────────

    public static Lure ToLure(this LureCreate src) => new()
    {
        Ping = src.Ping,
        Distance = src.Distance,
        LureId = src.LureId,
        Clean = src.Clean,
        Template = src.Template,
    };

    public static void ApplyUpdate(this LureUpdate src, Lure dest)
    {
        if (src.Ping != null) dest.Ping = src.Ping;
        if (src.Distance != null) dest.Distance = src.Distance.Value;
        if (src.LureId != null) dest.LureId = src.LureId.Value;
        if (src.Clean != null) dest.Clean = src.Clean.Value;
        if (src.Template != null) dest.Template = src.Template;
    }

    // ── Nest ─────────────────────────────────────────────────

    public static Nest ToNest(this NestCreate src) => new()
    {
        Ping = src.Ping,
        Distance = src.Distance,
        PokemonId = src.PokemonId,
        MinSpawnAvg = src.MinSpawnAvg,
        Form = src.Form,
        Clean = src.Clean,
        Template = src.Template,
    };

    public static void ApplyUpdate(this NestUpdate src, Nest dest)
    {
        if (src.Ping != null) dest.Ping = src.Ping;
        if (src.Distance != null) dest.Distance = src.Distance.Value;
        if (src.MinSpawnAvg != null) dest.MinSpawnAvg = src.MinSpawnAvg.Value;
        if (src.Form != null) dest.Form = src.Form.Value;
        if (src.Clean != null) dest.Clean = src.Clean.Value;
        if (src.Template != null) dest.Template = src.Template;
    }

    // ── Gym ──────────────────────────────────────────────────

    public static Gym ToGym(this GymCreate src) => new()
    {
        Ping = src.Ping,
        Distance = src.Distance,
        Team = src.Team,
        SlotChanges = src.SlotChanges,
        Clean = src.Clean,
        Template = src.Template,
        BattleChanges = src.BattleChanges,
        GymId = src.GymId,
    };

    public static void ApplyUpdate(this GymUpdate src, Gym dest)
    {
        if (src.Ping != null) dest.Ping = src.Ping;
        if (src.Distance != null) dest.Distance = src.Distance.Value;
        if (src.Team != null) dest.Team = src.Team.Value;
        if (src.SlotChanges != null) dest.SlotChanges = src.SlotChanges.Value;
        if (src.Clean != null) dest.Clean = src.Clean.Value;
        if (src.Template != null) dest.Template = src.Template;
        if (src.BattleChanges != null) dest.BattleChanges = src.BattleChanges.Value;
        if (src.GymId != null) dest.GymId = src.GymId;
    }

    // ── FortChange ───────────────────────────────────────────

    public static FortChange ToFortChange(this FortChangeCreate src) => new()
    {
        Ping = src.Ping,
        Distance = src.Distance,
        FortType = src.FortType,
        IncludeEmpty = src.IncludeEmpty,
        ChangeTypes = src.ChangeTypes,
        Clean = src.Clean,
        Template = src.Template,
    };

    public static void ApplyUpdate(this FortChangeUpdate src, FortChange dest)
    {
        if (src.Ping != null) dest.Ping = src.Ping;
        if (src.Distance != null) dest.Distance = src.Distance.Value;
        if (src.FortType != null) dest.FortType = src.FortType;
        if (src.IncludeEmpty != null) dest.IncludeEmpty = src.IncludeEmpty.Value;
        if (src.ChangeTypes != null) dest.ChangeTypes = src.ChangeTypes;
        if (src.Clean != null) dest.Clean = src.Clean.Value;
        if (src.Template != null) dest.Template = src.Template;
    }

    // ── MaxBattle ────────────────────────────────────────────

    public static MaxBattle ToMaxBattle(this MaxBattleCreate src) => new()
    {
        PokemonId = src.PokemonId,
        Ping = src.Ping,
        Distance = src.Distance,
        Gmax = src.Gmax,
        Level = src.Level,
        Form = src.Form,
        Clean = src.Clean,
        Template = src.Template,
        Move = src.Move,
        Evolution = src.Evolution,
        StationId = src.StationId,
    };

    public static void ApplyUpdate(this MaxBattleUpdate src, MaxBattle dest)
    {
        if (src.Ping != null) dest.Ping = src.Ping;
        if (src.Distance != null) dest.Distance = src.Distance.Value;
        if (src.Gmax != null) dest.Gmax = src.Gmax.Value;
        if (src.Level != null) dest.Level = src.Level.Value;
        if (src.Form != null) dest.Form = src.Form.Value;
        if (src.Clean != null) dest.Clean = src.Clean.Value;
        if (src.Template != null) dest.Template = src.Template;
        if (src.Move != null) dest.Move = src.Move.Value;
        if (src.Evolution != null) dest.Evolution = src.Evolution.Value;
        if (src.StationId != null) dest.StationId = src.StationId;
    }
}
