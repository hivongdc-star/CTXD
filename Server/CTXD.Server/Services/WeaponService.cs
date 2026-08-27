using System.Collections.Concurrent;
using System.Text.Json;
using CTXD.Server.Data;
using CTXD.Server.Models;
using Npgsql;

namespace CTXD.Server.Services;

public sealed record WeaponItemView(int Id,string Name,string Pic,string Intro,int Type,int Quality,bool Open,int Level,int Times,int TotalTimes,int Attribute,int NextAttribute,int UpgradeCost,int ItemId,int ItemNum,long ItemOwned,string Cost,int GemId,int GemNum,int Incense);
public sealed record WeaponView(WeaponItemView[] Weapons,ResourceView Resources);
public sealed record WeaponUpgradeResult(WeaponItemView Weapon,int Crit,bool LevelUp,ResourceView Resources);
public sealed record WeaponBattleEffect(int Attack,int Defense,int Blood);

public sealed class WeaponService(GameDb db,CanonicalContent content,IPlayerItemInventory items,ResourceProductionService production)
{
    sealed class WeaponDef
    {
        public int Id{get;set;} public string Name{get;set;}=""; public int Quality{get;set;} public string Intro{get;set;}=""; public string Pic{get;set;}=""; public int Type{get;set;}
        public int BaseAttribute{get;set;} public int Strengthen{get;set;} public int IronSeries{get;set;} public double IronExponent{get;set;} public int IronTimesSeries{get;set;}
        public int ItemId{get;set;} public int ItemNum{get;set;} public string Cost{get;set;}=""; public int GemId{get;set;} public int GemNum{get;set;} public int Incense{get;set;}
    }
    sealed record Runtime(int Level,int GemId,int Times);
    static readonly ConcurrentDictionary<string,IReadOnlyDictionary<int,WeaponDef>> DefinitionCache=new(StringComparer.OrdinalIgnoreCase);
    static IReadOnlyDictionary<int,WeaponDef> Definitions(CanonicalContent content)=>DefinitionCache.GetOrAdd(content.BaseDirectory,dir=>
    {
        var opt=new JsonSerializerOptions{PropertyNameCaseInsensitive=true};
        return (JsonSerializer.Deserialize<WeaponDef[]>(File.ReadAllText(Path.Combine(dir,"arms_weapon.json")),opt)??throw new InvalidOperationException("Cannot load arms_weapon.json.")).ToDictionary(x=>x.Id);
    });

    public async Task<WeaponView> GetAsync(long playerId,CancellationToken ct)
    {
        var resources=await production.AccrueAndGetAsync(playerId,ct);
        await using var c=await db.DataSource.OpenConnectionAsync(ct);
        await using(var mark=new NpgsqlCommand("INSERT INTO player_quest_runtime(player_id,arms_weapon_views) VALUES($1,1) ON CONFLICT(player_id) DO UPDATE SET arms_weapon_views=GREATEST(player_quest_runtime.arms_weapon_views,1),updated_at=now()",c)){mark.Parameters.AddWithValue(playerId);await mark.ExecuteNonQueryAsync(ct);}
        var runtime=await ReadRuntimeAsync(c,null,playerId,ct);
        var owned=await ReadBlueprintsAsync(c,null,playerId,ct);
        return new(Definitions(content).Values.OrderBy(x=>x.Id).Select(d=>View(d,runtime.GetValueOrDefault(d.Id),owned.GetValueOrDefault(d.ItemId))).ToArray(),resources);
    }

    public async Task<WeaponUpgradeResult> UpgradeAsync(long playerId,int weaponId,CancellationToken ct)
    {
        var defs=Definitions(content);
        if(!defs.TryGetValue(weaponId,out var def))throw new GameException("WEAPON_NO_SUCH_WEAPON","Không có binh khí này.",404);
        await using var c=await db.DataSource.OpenConnectionAsync(ct);await using var t=await c.BeginTransactionAsync(ct);
        await production.AccrueAndGetAsync(playerId,ct,c,t);
        int playerLevel;await using(var p=new NpgsqlCommand("SELECT level FROM players WHERE id=$1 FOR UPDATE",c,t)){p.Parameters.AddWithValue(playerId);var raw=await p.ExecuteScalarAsync(ct);if(raw is null)throw new GameException("PLAYER_NOT_FOUND","Không tìm thấy nhân vật.",404);playerLevel=Convert.ToInt32(raw);}
        Runtime runtime;await using(var q=new NpgsqlCommand("SELECT level,gem_id,times FROM player_weapons WHERE player_id=$1 AND weapon_id=$2 FOR UPDATE",c,t)){q.Parameters.AddWithValue(playerId);q.Parameters.AddWithValue(weaponId);await using var r=await q.ExecuteReaderAsync(ct);if(!await r.ReadAsync(ct))throw new GameException("WEAPON_NO_SUCH_WEAPON","Binh khí chưa được mở.");runtime=new(r.GetInt32(0),r.GetInt32(1),r.GetInt32(2));}
        var crit=1;var levelUp=false;
        if(runtime.Level<1)
        {
            if(!await items.ConsumeAsync(c,t,playerId,def.ItemId,6,def.ItemNum,ct))throw new GameException("WEAPON_ITEM_NOT_ENOUGH","Không đủ mảnh binh khí.");
            var cost=ParseCost(def.Cost);var copper=cost.GetValueOrDefault(1);var wood=cost.GetValueOrDefault(2);var food=cost.GetValueOrDefault(3);var iron=cost.GetValueOrDefault(4);
            await using(var spend=new NpgsqlCommand("UPDATE player_resources SET copper=copper-$2,wood=wood-$3,food=food-$4,iron=iron-$5 WHERE player_id=$1 AND copper>=$2 AND wood>=$3 AND food>=$4 AND iron>=$5",c,t)){spend.Parameters.AddWithValue(playerId);spend.Parameters.AddWithValue(copper);spend.Parameters.AddWithValue(wood);spend.Parameters.AddWithValue(food);spend.Parameters.AddWithValue(iron);if(await spend.ExecuteNonQueryAsync(ct)!=1)throw new GameException("WEAPON_RESOURCE_NOT_ENOUGH","Không đủ tài nguyên để rèn binh khí.");}
            runtime=new(1,runtime.GemId,0);levelUp=true;
        }
        else
        {
            if(runtime.Level>=playerLevel)throw new GameException("WEAPON_LV_LIMIT","Cấp binh khí không thể vượt cấp nhân vật.");
            var total=content.Serial(def.IronTimesSeries,runtime.Level);if(total==0)total=100;
            var full=(int)(def.IronExponent*content.Serial(def.IronSeries,runtime.Level));var ironCost=full/total;
            await using(var spend=new NpgsqlCommand("UPDATE player_resources SET iron=iron-$2 WHERE player_id=$1 AND iron>=$2",c,t)){spend.Parameters.AddWithValue(playerId);spend.Parameters.AddWithValue(ironCost);if(await spend.ExecuteNonQueryAsync(ct)!=1)throw new GameException("IRON_NOT_ENOUGH","Không đủ sắt.");}
            crit=RollCrit();
            if(runtime.Times+crit>=total){runtime=new(runtime.Level+1,runtime.GemId,0);levelUp=true;}else runtime=new(runtime.Level,runtime.GemId,runtime.Times+crit);
        }
        await using(var save=new NpgsqlCommand("UPDATE player_weapons SET level=$3,gem_id=$4,times=$5,updated_at=now() WHERE player_id=$1 AND weapon_id=$2",c,t)){save.Parameters.AddWithValue(playerId);save.Parameters.AddWithValue(weaponId);save.Parameters.AddWithValue(runtime.Level);save.Parameters.AddWithValue(runtime.GemId);save.Parameters.AddWithValue(runtime.Times);await save.ExecuteNonQueryAsync(ct);}
        var refreshed=await production.AccrueAndGetAsync(playerId,ct,c,t);var owned=await BlueprintOwnedAsync(c,t,playerId,def.ItemId,ct);var view=View(def,runtime,owned);await t.CommitAsync(ct);return new(view,crit,levelUp,refreshed);
    }

    public static async Task AssignAsync(NpgsqlConnection c,NpgsqlTransaction t,CanonicalContent content,long playerId,int weaponId,CancellationToken ct)
    {
        if(!Definitions(content).ContainsKey(weaponId))throw new GameException("WEAPON_STATIC_MISSING",$"Legacy weapon {weaponId} is missing.",500);
        await using var cmd=new NpgsqlCommand("INSERT INTO player_weapons(player_id,weapon_id,level,gem_id,times) VALUES($1,$2,0,0,0) ON CONFLICT DO NOTHING",c,t);cmd.Parameters.AddWithValue(playerId);cmd.Parameters.AddWithValue(weaponId);await cmd.ExecuteNonQueryAsync(ct);
    }

    public static async Task<WeaponBattleEffect> BattleEffectAsync(NpgsqlConnection c,NpgsqlTransaction? t,CanonicalContent content,long playerId,CancellationToken ct)
    {
        var defs=Definitions(content);var attack=0;var defense=0;var blood=0;
        await using var q=new NpgsqlCommand("SELECT weapon_id,level FROM player_weapons WHERE player_id=$1 AND level>0",c,t);q.Parameters.AddWithValue(playerId);await using var r=await q.ExecuteReaderAsync(ct);
        while(await r.ReadAsync(ct)){if(!defs.TryGetValue(r.GetInt32(0),out var d))continue;var lv=r.GetInt32(1);var value=d.BaseAttribute+d.Strengthen*Math.Max(0,lv-1);switch(d.Type){case 1:attack+=value;break;case 2:defense+=value;break;case 3:blood+=value;break;}}
        return new(attack,defense,blood);
    }

    static async Task<Dictionary<int,Runtime>> ReadRuntimeAsync(NpgsqlConnection c,NpgsqlTransaction? t,long player,CancellationToken ct){var d=new Dictionary<int,Runtime>();await using var q=new NpgsqlCommand("SELECT weapon_id,level,gem_id,times FROM player_weapons WHERE player_id=$1",c,t);q.Parameters.AddWithValue(player);await using var r=await q.ExecuteReaderAsync(ct);while(await r.ReadAsync(ct))d[r.GetInt32(0)]=new(r.GetInt32(1),r.GetInt32(2),r.GetInt32(3));return d;}
    static async Task<Dictionary<int,long>> ReadBlueprintsAsync(NpgsqlConnection c,NpgsqlTransaction? t,long player,CancellationToken ct){var d=new Dictionary<int,long>();await using var q=new NpgsqlCommand("SELECT item_id,quantity FROM player_items WHERE player_id=$1 AND item_type=6",c,t);q.Parameters.AddWithValue(player);await using var r=await q.ExecuteReaderAsync(ct);while(await r.ReadAsync(ct))d[r.GetInt32(0)]=r.GetInt64(1);return d;}
    static async Task<long> BlueprintOwnedAsync(NpgsqlConnection c,NpgsqlTransaction t,long player,int item,CancellationToken ct){await using var q=new NpgsqlCommand("SELECT COALESCE((SELECT quantity FROM player_items WHERE player_id=$1 AND item_id=$2 AND item_type=6),0)",c,t);q.Parameters.AddWithValue(player);q.Parameters.AddWithValue(item);return Convert.ToInt64(await q.ExecuteScalarAsync(ct));}
    WeaponItemView View(WeaponDef d,Runtime? r,long owned){var lv=r?.Level??0;var times=r?.Times??0;var total=lv>0?content.Serial(d.IronTimesSeries,lv):0;if(lv>0&&total==0)total=100;var upgrade=lv>0?(int)(d.IronExponent*content.Serial(d.IronSeries,lv))/Math.Max(1,total):0;var attr=d.BaseAttribute+d.Strengthen*Math.Max(0,lv-1);var next=d.BaseAttribute+d.Strengthen*Math.Max(0,lv);return new(d.Id,d.Name,d.Pic,d.Intro,d.Type,d.Quality,r is not null,lv,times,total,attr,next,upgrade,d.ItemId,d.ItemNum,owned,d.Cost,d.GemId,d.GemNum,d.Incense);}
    static Dictionary<int,int> ParseCost(string raw){var d=new Dictionary<int,int>();foreach(var part in raw.Split(';',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries)){var p=part.Split(',',StringSplitOptions.TrimEntries);if(p.Length==2&&int.TryParse(p[0],out var type)&&int.TryParse(p[1],out var value))d[type]=value;}return d;}
    static int RollCrit(){var x=Random.Shared.NextDouble();return x<.69?1:x<.89?2:x<.99?4:10;}
}
