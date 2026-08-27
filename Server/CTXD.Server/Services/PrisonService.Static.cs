using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CTXD.Server.Services;

public sealed partial class PrisonService
{
    sealed class PrisonLvDef
    {
        [JsonPropertyName("prison_lv"),JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] public int PrisonLv{get;set;}
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] public int Drawing{get;set;}
    }
    sealed class PrisonDegreeDef
    {
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] public int Degree{get;set;}
        [JsonPropertyName("exp_extra"),JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] public int ExpExtra{get;set;}
        [JsonPropertyName("time_extra"),JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] public int TimeExtra{get;set;}
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] public int Cost{get;set;}
        [JsonPropertyName("exp_free"),JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] public int ExpFree{get;set;}
        [JsonPropertyName("exp_sum"),JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] public int ExpSum{get;set;}
        [JsonPropertyName("get_exp_prob"),JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] public double GetExpProb{get;set;}
        [JsonPropertyName("try_gold"),JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] public int TryGold{get;set;}
    }
    sealed class PrisonLashRewardDef
    {
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] public int Id{get;set;}
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] public int Type{get;set;}
        [JsonPropertyName("low_lv"),JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] public int LowLv{get;set;}
        [JsonPropertyName("high_lv"),JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] public int HighLv{get;set;}
        [JsonPropertyName("official_low"),JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] public int OfficialLow{get;set;}
        [JsonPropertyName("official_high"),JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] public int OfficialHigh{get;set;}
        [JsonPropertyName("exp_reward"),JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] public int ExpReward{get;set;}
        [JsonPropertyName("prison_low_lv"),JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] public int PrisonLowLv{get;set;}
        [JsonPropertyName("prison_high_lv"),JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] public int PrisonHighLv{get;set;}
    }
    sealed class PrisonCatchDef
    {
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] public int Id{get;set;}
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] public int N{get;set;}
        [JsonPropertyName("prison_low_lv"),JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] public int PrisonLowLv{get;set;}
        [JsonPropertyName("prison_high_lv"),JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] public int PrisonHighLv{get;set;}
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] public double Prob{get;set;}
        [JsonPropertyName("prob_lv"),JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] public int ProbLv{get;set;}
    }
    sealed record Holder(int PrisonLv,int LashLv,int GrabNum,int LashNum,long AutoLashExp,int Point,DateTimeOffset? ExpireAt,int TrailGold);

    static readonly ConcurrentDictionary<string,StaticData> Cache=new(StringComparer.OrdinalIgnoreCase);
    sealed record StaticData(IReadOnlyDictionary<int,PrisonLvDef> Levels,IReadOnlyDictionary<int,PrisonDegreeDef> Degrees,PrisonLashRewardDef[] LashRewards,PrisonCatchDef[] CatchRows);
    public static PrisonService FromServices(IServiceProvider services)=>new(
        services.GetRequiredService<GameDb>(),
        services.GetRequiredService<CanonicalContent>(),
        services.GetRequiredService<IPlayerItemInventory>(),
        services.GetRequiredService<ExperienceService>(),
        services.GetRequiredService<TechnologyEffectService>(),
        services.GetRequiredService<DstqActivityService>(),
        services.GetRequiredService<GamePushHub>());

    static StaticData LoadStatic(string dir)
    {
        var opt=new JsonSerializerOptions{PropertyNameCaseInsensitive=true,NumberHandling=JsonNumberHandling.AllowReadingFromString};
        T[] Load<T>(string file)=>JsonSerializer.Deserialize<T[]>(File.ReadAllText(Path.Combine(dir,file)),opt)??throw new InvalidOperationException($"Cannot load {file}.");
        return new(
            Load<PrisonLvDef>("prison_lv.json").ToDictionary(x=>x.PrisonLv),
            Load<PrisonDegreeDef>("prison_degree.json").ToDictionary(x=>x.Degree),
            Load<PrisonLashRewardDef>("prison_lash_reward.json"),
            Load<PrisonCatchDef>("prison_catch_prob_1.json").Concat(Load<PrisonCatchDef>("prison_catch_prob_2.json")).Concat(Load<PrisonCatchDef>("prison_catch_prob_3.json")).ToArray());
    }
}
