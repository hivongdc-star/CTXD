using System.Text.Json;
using System.Text.Json.Serialization;
using CTXD.Server.Data;
using Npgsql;

namespace CTXD.Server.Services;

public sealed record TreasureItemView(int Id,int Position,string Name,string Pic,string Tips,bool Owned,int Type,string Effect);
public sealed record TreasureView(TreasureItemView[] Treasures);
public sealed record TreasureBattleEffect(int AttackBase,int DefenseBase,double AttackCoefficient,double DefenseCoefficient);

public sealed class TreasureService(GameDb db,CanonicalContent content,GamePushHub push)
{
    sealed class TreasureDef
    {
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]public int Id{get;set;}
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]public int Pos{get;set;}
        public string Name{get;set;}="";public string Pic{get;set;}="";
        [JsonPropertyName("tips_owned")]public string TipsOwned{get;set;}="";
        [JsonPropertyName("tips_lack")]public string TipsLack{get;set;}="";
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]public int Type{get;set;}
        public string Effect{get;set;}="";
    }
    readonly TreasureDef[] definitions=Load<TreasureDef[]>(content.BaseDirectory,"treasure.json").OrderBy(x=>x.Pos).ToArray();
    public async Task<TreasureView> GetAsync(long player,CancellationToken ct){await using var c=await db.DataSource.OpenConnectionAsync(ct);var owned=new HashSet<int>();await using(var q=new NpgsqlCommand("SELECT treasure_id FROM player_treasures WHERE player_id=$1",c)){q.Parameters.AddWithValue(player);await using var r=await q.ExecuteReaderAsync(ct);while(await r.ReadAsync(ct))owned.Add(r.GetInt32(0));}return new(definitions.Select(x=>new TreasureItemView(x.Id,x.Pos,x.Name,x.Pic,owned.Contains(x.Id)?x.TipsOwned:x.TipsLack,owned.Contains(x.Id),x.Type,x.Effect)).ToArray());}
    public async Task<TreasureItemView?> TryAcquireAsync(NpgsqlConnection c,NpgsqlTransaction t,long player,int type,double probability,string source,CancellationToken ct){await using(var gate=new NpgsqlCommand("SELECT 1 FROM player_functions WHERE player_id=$1 AND function_id=20",c,t)){gate.Parameters.AddWithValue(player);if(await gate.ExecuteScalarAsync(ct)is null)return null;}if(Random.Shared.NextDouble()>probability)return null;var owned=new HashSet<int>();await using(var q=new NpgsqlCommand("SELECT treasure_id FROM player_treasures WHERE player_id=$1",c,t)){q.Parameters.AddWithValue(player);await using var r=await q.ExecuteReaderAsync(ct);while(await r.ReadAsync(ct))owned.Add(r.GetInt32(0));}var def=definitions.FirstOrDefault(x=>x.Type==type&&!owned.Contains(x.Id));if(def is null)return null;await using var add=new NpgsqlCommand("INSERT INTO player_treasures(player_id,treasure_id,source) VALUES($1,$2,$3) ON CONFLICT DO NOTHING",c,t);add.Parameters.AddWithValue(player);add.Parameters.AddWithValue(def.Id);add.Parameters.AddWithValue(source);if(await add.ExecuteNonQueryAsync(ct)!=1)return null;return new(def.Id,def.Pos,def.Name,def.Pic,def.TipsOwned,true,def.Type,def.Effect);}
    public async Task<TreasureBattleEffect> BattleEffectAsync(NpgsqlConnection c,NpgsqlTransaction t,long player,CancellationToken ct){var effects=new List<string>();await using(var q=new NpgsqlCommand("SELECT treasure_id FROM player_treasures WHERE player_id=$1",c,t)){q.Parameters.AddWithValue(player);await using var r=await q.ExecuteReaderAsync(ct);while(await r.ReadAsync(ct)){var id=r.GetInt32(0);var def=definitions.FirstOrDefault(x=>x.Id==id);if(def is not null)effects.Add(def.Effect);}}var attBase=0;var defBase=0;double att=1,defence=1;foreach(var raw in effects.SelectMany(x=>x.Split(';',StringSplitOptions.RemoveEmptyEntries))){var p=raw.Split('=');if(p.Length!=2||!double.TryParse(p[1],System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var value))continue;switch(p[0].ToUpperInvariant()){case"ATT":att*=1+value;break;case"DEF":defence*=1+value;break;case"ATT_BASE":attBase+=(int)value;break;case"DEF_BASE":defBase+=(int)value;break;}}return new(attBase,defBase,att,defence);}
    public async Task NotifyAcquiredAsync(long player,TreasureItemView item,CancellationToken ct)=>await push.SendAsync(player,"treasure.updated",item,ct);
    static T Load<T>(string dir,string file){var opt=new JsonSerializerOptions{PropertyNameCaseInsensitive=true,NumberHandling=JsonNumberHandling.AllowReadingFromString};return JsonSerializer.Deserialize<T>(File.ReadAllText(Path.Combine(dir,file)),opt)??throw new InvalidOperationException($"Cannot load {file}.");}
}
