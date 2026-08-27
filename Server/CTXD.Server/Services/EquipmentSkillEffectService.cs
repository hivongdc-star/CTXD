using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using CTXD.Server.Data;
using CTXD.Server.Domain;
using Npgsql;

namespace CTXD.Server.Services;

public readonly record struct EquipmentSkillBattleEffect(
    int Attack,int Defense,int Blood,
    int AttackBonus,int DefenseBonus,
    int TacticAttack,int TacticDefense);

/// <summary>
/// Authoritative legacy equipment refresh-skill catalog. Normal equipment consumes refresh_attribute.
/// Suit/Proset contribution is resolved by EquipmentCompositeService and appended exactly once here.
/// </summary>
public static class EquipmentSkillEffectService
{
    sealed class SkillDef
    {
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] public int Id { get; set; }
        [JsonPropertyName("skill_type"),JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] public int SkillType { get; set; }
    }
    sealed class EffectDef
    {
        [JsonPropertyName("skill_id"),JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] public int SkillId { get; set; }
        [JsonPropertyName("skill_lv"),JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] public int SkillLevel { get; set; }
        public string Effect { get; set; }="";
    }
    sealed record Catalog(IReadOnlyDictionary<int,SkillDef[]> SkillsByType,IReadOnlyDictionary<(int skill,int level),EffectDef> Effects);
    static readonly ConcurrentDictionary<string,Catalog> Cache=new(StringComparer.OrdinalIgnoreCase);

    public static string GenerateRefreshAttribute(CanonicalContent content,EquipmentDefinition equipment)
    {
        if(equipment.SkillNum<=0)return "";
        var catalog=Get(content);
        if(!catalog.SkillsByType.TryGetValue(equipment.SkillType,out var skills)||skills.Length==0)
            throw new InvalidOperationException($"Legacy equip_skill missing skill_type={equipment.SkillType}.");
        var result=new string[equipment.SkillNum];
        for(var i=0;i<result.Length;i++)
        {
            var skill=skills[Random.Shared.Next(skills.Length)];
            result[i]=$"{skill.Id}:{equipment.SkillLevelDefault}";
        }
        return string.Join(';',result);
    }

    public static EquipmentSkillBattleEffect Resolve(CanonicalContent content,int skillId,int skillLevel)
    {
        var catalog=Get(content);
        return catalog.Effects.TryGetValue((skillId,skillLevel),out var def)?Resolve(def):default;
    }

    public static async Task<EquipmentSkillBattleEffect> BattleAsync(
        NpgsqlConnection connection,NpgsqlTransaction? transaction,CanonicalContent content,long playerId,int generalId,CancellationToken ct)
    {
        var attributes=new List<string>();
        await using(var cmd=new NpgsqlCommand("SELECT refresh_attribute FROM player_equipment WHERE player_id=$1 AND owner_general_id=$2 AND num>0",connection,transaction))
        {
            cmd.Parameters.AddWithValue(playerId);cmd.Parameters.AddWithValue(generalId);
            await using var r=await cmd.ExecuteReaderAsync(ct);
            while(await r.ReadAsync(ct))attributes.Add(r.IsDBNull(0)?"":r.GetString(0));
        }
        var catalog=Get(content);
        var total=default(EquipmentSkillBattleEffect);
        foreach(var raw in attributes)
        foreach(var token in raw.Split(';',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries))
        {
            var pair=token.Split(':',2,StringSplitOptions.TrimEntries);
            if(pair.Length!=2||!int.TryParse(pair[0],out var skill)||!int.TryParse(pair[1],out var level))continue;
            if(!catalog.Effects.TryGetValue((skill,level),out var def))continue;
            total=Add(total,Resolve(def));
        }
        return Add(total,await EquipmentCompositeService.BattleAsync(connection,transaction,content,playerId,generalId,ct));
    }

    static EquipmentSkillBattleEffect Resolve(EffectDef def)
    {
        var effect=def.Effect.Split('=',2,StringSplitOptions.TrimEntries);
        if(effect.Length!=2||!int.TryParse(effect[1],out var value))return default;
        return effect[0].ToUpperInvariant() switch
        {
            "ATT"=>new(value,0,0,0,0,0,0),
            "DEF"=>new(0,value,0,0,0,0,0),
            "BLOOD"=>new(0,0,value,0,0,0,0),
            "ATT_B"=>new(0,0,0,value,0,0,0),
            "DEF_B"=>new(0,0,0,0,value,0,0),
            "TACTIC_ATT"=>new(0,0,0,0,0,value,0),
            "TACTIC_DEF"=>new(0,0,0,0,0,0,value),
            _=>default
        };
    }

    static EquipmentSkillBattleEffect Add(EquipmentSkillBattleEffect a,EquipmentSkillBattleEffect b)=>new(
        a.Attack+b.Attack,a.Defense+b.Defense,a.Blood+b.Blood,
        a.AttackBonus+b.AttackBonus,a.DefenseBonus+b.DefenseBonus,
        a.TacticAttack+b.TacticAttack,a.TacticDefense+b.TacticDefense);

    static Catalog Get(CanonicalContent content)=>Cache.GetOrAdd(content.BaseDirectory,Load);
    static Catalog Load(string dir)
    {
        var opt=new JsonSerializerOptions{PropertyNameCaseInsensitive=true,NumberHandling=JsonNumberHandling.AllowReadingFromString};
        var skills=JsonSerializer.Deserialize<SkillDef[]>(File.ReadAllText(Path.Combine(dir,"equip_skill.json")),opt)
            ??throw new InvalidOperationException("Cannot load equip_skill.json.");
        var effects=JsonSerializer.Deserialize<EffectDef[]>(File.ReadAllText(Path.Combine(dir,"equip_skill_effect.json")),opt)
            ??throw new InvalidOperationException("Cannot load equip_skill_effect.json.");
        return new(
            skills.GroupBy(x=>x.SkillType).ToDictionary(x=>x.Key,x=>x.OrderBy(y=>y.Id).ToArray()),
            effects.ToDictionary(x=>(x.SkillId,x.SkillLevel)));
    }
}
