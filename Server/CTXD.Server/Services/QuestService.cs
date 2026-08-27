using System.Text.Json;
using CTXD.Server.Data;
using CTXD.Server.Domain;
using Npgsql;

namespace CTXD.Server.Services;

public sealed record QuestView(int TaskId,string Name,string IntroLong,string IntroShort,string Target,int[] TargetArgs,bool Completed,string? Dependency,object[] Rewards);
public sealed record QuestClaimResult(int TaskId,int NextTaskId,object[] Rewards);
public sealed record QuestRuntimeResult(string Kind,int Remaining);
public sealed record QuestBranchView(int BranchId,int Index,string Name,string IntroLong,string IntroShort,string Target,bool Completed,bool Claimed,object[] Rewards);
public sealed record QuestBranchClaimResult(int BranchId,int Index,object[] Rewards);

public sealed class QuestService(GameDb db,CanonicalContent content,ExperienceService experience,ResourceProductionService production)
{
    const int LimboBranchId=804;
    static readonly object[] LimboRewards=[new{kind="copper",args=new[]{2000}}];

    public async Task<QuestView> GetCurrentAsync(long playerId,CancellationToken ct)
    {
        await using var c=await db.DataSource.OpenConnectionAsync(ct);
        var taskId=await CurrentTaskIdAsync(c,null,playerId,false,ct);
        var task=GetTask(taskId);
        var (completed,dependency)=await EvaluateAsync(c,null,playerId,task,ct);
        return View(task,completed,dependency);
    }

    public async Task<QuestClaimResult> ClaimCurrentAsync(long playerId,CancellationToken ct)
    {
        await using var c=await db.DataSource.OpenConnectionAsync(ct);
        await using var t=await c.BeginTransactionAsync(ct);
        var taskId=await CurrentTaskIdAsync(c,t,playerId,true,ct);
        var task=GetTask(taskId);
        var (completed,dependency)=await EvaluateAsync(c,t,playerId,task,ct);
        if(!completed) throw new GameException("QUEST_NOT_COMPLETE",dependency is null?"Quest condition is not complete.":$"Quest dependency is not available: {dependency}.");
        EnsureRewardsSupported(task);
        await using(var claim=new NpgsqlCommand("INSERT INTO player_quest_claims(player_id,task_id) VALUES($1,$2) ON CONFLICT DO NOTHING",c,t))
        { claim.Parameters.AddWithValue(playerId);claim.Parameters.AddWithValue(task.Id);if(await claim.ExecuteNonQueryAsync(ct)!=1)throw new GameException("QUEST_ALREADY_CLAIMED","Quest reward was already claimed.",409); }
        foreach(var reward in task.Reward) await ApplyRewardAsync(c,t,playerId,reward,ct);
        await using(var advance=new NpgsqlCommand("UPDATE players SET current_task_id=$2,updated_at=now() WHERE id=$1 AND current_task_id=$3",c,t))
        { advance.Parameters.AddWithValue(playerId);advance.Parameters.AddWithValue(task.NextTaskId==0?task.Id:task.NextTaskId);advance.Parameters.AddWithValue(task.Id);if(await advance.ExecuteNonQueryAsync(ct)!=1)throw new GameException("QUEST_STATE_CHANGED","Quest state changed during claim.",409); }
        await t.CommitAsync(ct);
        return new(task.Id,task.NextTaskId,RewardViews(task));
    }

    public async Task<QuestRuntimeResult> KillKidnapperAsync(long playerId,int kidnapperId,CancellationToken ct)
    {
        if(kidnapperId is <1 or >2)throw new GameException("KIDNAPPER_INVALID","Legacy kidnapper id must be 1 or 2.");
        await using var c=await db.DataSource.OpenConnectionAsync(ct);await using var t=await c.BeginTransactionAsync(ct);
        await using(var ensure=new NpgsqlCommand("INSERT INTO player_quest_runtime(player_id) VALUES($1) ON CONFLICT DO NOTHING",c,t)){ensure.Parameters.AddWithValue(playerId);await ensure.ExecuteNonQueryAsync(ct);}
        await using(var cmd=new NpgsqlCommand("UPDATE player_quest_runtime SET kidnapper=0,updated_at=now() WHERE player_id=$1 AND kidnapper>0 RETURNING kidnapper",c,t)){cmd.Parameters.AddWithValue(playerId);var result=await cmd.ExecuteScalarAsync(ct);if(result is null)throw new GameException("KIDNAPPER_ALREADY_DEFEATED","Kidnapper has already been defeated.",409);}
        await t.CommitAsync(ct);return new("kidnapper",0);
    }

    public async Task<QuestBranchView[]> GetBranchesAsync(long playerId,CancellationToken ct)
    {
        await using var c=await db.DataSource.OpenConnectionAsync(ct);
        bool unlocked,claimed,built;
        await using(var q=new NpgsqlCommand(@"SELECT b.claimed_at IS NOT NULL,EXISTS(SELECT 1 FROM player_prisons p WHERE p.player_id=b.player_id)
FROM player_quest_branches b WHERE b.player_id=$1 AND b.branch_id=$2",c))
        {
            q.Parameters.AddWithValue(playerId);q.Parameters.AddWithValue(LimboBranchId);
            await using var r=await q.ExecuteReaderAsync(ct);unlocked=await r.ReadAsync(ct);
            if(!unlocked)return[];claimed=r.GetBoolean(0);built=r.GetBoolean(1);
        }
        return[new(LimboBranchId,1,"Kiến Tạo Lao Phòng","","","Builded_Limbo",built,claimed,LimboRewards)];
    }

    public async Task<QuestBranchClaimResult> ClaimBranchAsync(long playerId,int branchId,CancellationToken ct)
    {
        if(branchId!=LimboBranchId)throw new GameException("QUEST_BRANCH_STATIC_MISSING",$"Legacy quest branch {branchId} is not available.",404);
        await using var c=await db.DataSource.OpenConnectionAsync(ct);await using var t=await c.BeginTransactionAsync(ct);
        bool claimed,built;
        await using(var q=new NpgsqlCommand(@"SELECT b.claimed_at IS NOT NULL,EXISTS(SELECT 1 FROM player_prisons p WHERE p.player_id=b.player_id)
FROM player_quest_branches b WHERE b.player_id=$1 AND b.branch_id=$2 FOR UPDATE",c,t))
        {
            q.Parameters.AddWithValue(playerId);q.Parameters.AddWithValue(branchId);await using var r=await q.ExecuteReaderAsync(ct);
            if(!await r.ReadAsync(ct))throw new GameException("QUEST_BRANCH_LOCKED","Quest branch is not unlocked.",403);
            claimed=r.GetBoolean(0);built=r.GetBoolean(1);
        }
        if(claimed)throw new GameException("QUEST_ALREADY_CLAIMED","Quest reward was already claimed.",409);
        if(!built)throw new GameException("QUEST_NOT_COMPLETE","Lao Phòng has not been built.",409);
        await using(var reward=new NpgsqlCommand("UPDATE player_resources SET copper=copper+2000 WHERE player_id=$1",c,t)){reward.Parameters.AddWithValue(playerId);await reward.ExecuteNonQueryAsync(ct);}
        await using(var done=new NpgsqlCommand("UPDATE player_quest_branches SET completed_at=COALESCE(completed_at,now()),claimed_at=now() WHERE player_id=$1 AND branch_id=$2 AND claimed_at IS NULL",c,t)){done.Parameters.AddWithValue(playerId);done.Parameters.AddWithValue(branchId);if(await done.ExecuteNonQueryAsync(ct)!=1)throw new GameException("QUEST_STATE_CHANGED","Quest branch state changed during claim.",409);}
        await t.CommitAsync(ct);return new(branchId,1,LimboRewards);
    }

    public static async Task MarkBuildedLimboAsync(NpgsqlConnection c,NpgsqlTransaction t,long playerId,CancellationToken ct)
    {
        await using var q=new NpgsqlCommand("UPDATE player_quest_branches SET completed_at=COALESCE(completed_at,now()) WHERE player_id=$1 AND branch_id=$2 AND claimed_at IS NULL",c,t);
        q.Parameters.AddWithValue(playerId);q.Parameters.AddWithValue(LimboBranchId);await q.ExecuteNonQueryAsync(ct);
    }

    TaskDefinition GetTask(int id)=>content.Tasks.TryGetValue(id,out var task)?task:throw new GameException("QUEST_STATIC_MISSING",$"Legacy task {id} is missing.",500);
    static async Task<int> CurrentTaskIdAsync(NpgsqlConnection c,NpgsqlTransaction? t,long player,bool update,CancellationToken ct)
    { await using var cmd=new NpgsqlCommand($"SELECT current_task_id FROM players WHERE id=$1{(update?" FOR UPDATE":"")}",c,t);cmd.Parameters.AddWithValue(player);var value=await cmd.ExecuteScalarAsync(ct);if(value is null)throw new GameException("PLAYER_NOT_FOUND","Player does not exist.",404);return Convert.ToInt32(value); }

    static int Int(JsonElement e)=>e.ValueKind==JsonValueKind.Number?e.GetInt32():int.Parse(e.GetString()!);
    static int[] Args(TargetDefinition target)=>target.Args.Select(e=>e.ValueKind==JsonValueKind.Number?e.GetInt32():int.TryParse(e.GetString(),out var n)?n:(int?)null).Where(x=>x.HasValue).Select(x=>x!.Value).ToArray();

    async Task<(bool complete,string? dependency)> EvaluateAsync(NpgsqlConnection c,NpgsqlTransaction? t,long player,TaskDefinition task,CancellationToken ct)
    {
        switch(task.Target.Kind)
        {
            case "chose_side":
                await using(var cmd=new NpgsqlCommand("SELECT force_id<>0 FROM players WHERE id=$1",c,t)){cmd.Parameters.AddWithValue(player);return (Convert.ToBoolean(await cmd.ExecuteScalarAsync(ct)),null);}
            case "building":
                var a=Args(task.Target);if(a.Length<2)return(false,"legacy-target-arguments");
                await using(var cmd=new NpgsqlCommand("SELECT COALESCE((SELECT level FROM player_buildings WHERE player_id=$1 AND building_id=$2),0)>=$3",c,t)){cmd.Parameters.AddWithValue(player);cmd.Parameters.AddWithValue(a[0]);cmd.Parameters.AddWithValue(a[1]);return(Convert.ToBoolean(await cmd.ExecuteScalarAsync(ct)),null);}
            case "change_name":
                await using(var cmd=new NpgsqlCommand("SELECT display_name IS NOT NULL FROM players WHERE id=$1",c,t)){cmd.Parameters.AddWithValue(player);return(Convert.ToBoolean(await cmd.ExecuteScalarAsync(ct)),null);}
            case "kill_kidnapper":
                await using(var cmd=new NpgsqlCommand("SELECT COALESCE((SELECT kidnapper FROM player_quest_runtime WHERE player_id=$1),3)<=0",c,t)){cmd.Parameters.AddWithValue(player);return(Convert.ToBoolean(await cmd.ExecuteScalarAsync(ct)),null);}
            case "chief_lv":
                var chief=Args(task.Target);await using(var cmd=new NpgsqlCommand("SELECT level>=$2 FROM players WHERE id=$1",c,t)){cmd.Parameters.AddWithValue(player);cmd.Parameters.AddWithValue(chief[0]);return(Convert.ToBoolean(await cmd.ExecuteScalarAsync(ct)),null);}
            case "general_min_lv":
                var min=Args(task.Target);await using(var cmd=new NpgsqlCommand("SELECT COALESCE(min(level),0)>=$2 FROM player_generals WHERE player_id=$1 AND general_type=2",c,t)){cmd.Parameters.AddWithValue(player);cmd.Parameters.AddWithValue(min[0]);return(Convert.ToBoolean(await cmd.ExecuteScalarAsync(ct)),null);}
            case "recruit_general":
                var general=Args(task.Target);await using(var cmd=new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM player_generals WHERE player_id=$1 AND general_type=$2 AND general_id=$3)",c,t)){cmd.Parameters.AddWithValue(player);cmd.Parameters.AddWithValue(general[0]);cmd.Parameters.AddWithValue(general[1]);return(Convert.ToBoolean(await cmd.ExecuteScalarAsync(ct)),null);}
            case "tech_research_begin":
            case "tech_research_done":
                var tech=Args(task.Target);var status=task.Target.Kind=="tech_research_done"?5:4;await using(var cmd=new NpgsqlCommand("SELECT COALESCE((SELECT status FROM player_technologies WHERE player_id=$1 AND technology_id=$2),0)>=$3",c,t)){cmd.Parameters.AddWithValue(player);cmd.Parameters.AddWithValue(tech[0]);cmd.Parameters.AddWithValue(status);return(Convert.ToBoolean(await cmd.ExecuteScalarAsync(ct)),null);}
            case "official":
                var official=Args(task.Target);await using(var cmd=new NpgsqlCommand("SELECT official_id<=$2 FROM players WHERE id=$1",c,t)){cmd.Parameters.AddWithValue(player);cmd.Parameters.AddWithValue(official[0]);return(Convert.ToBoolean(await cmd.ExecuteScalarAsync(ct)),null);}
            case "battle_win":
                var wins=Args(task.Target);await using(var cmd=new NpgsqlCommand("SELECT COALESCE((SELECT battle_wins FROM player_quest_runtime WHERE player_id=$1),0)>=$2",c,t)){cmd.Parameters.AddWithValue(player);cmd.Parameters.AddWithValue(wins[0]);return(Convert.ToBoolean(await cmd.ExecuteScalarAsync(ct)),null);}
            case "market_buy":
                var buys=Args(task.Target);var wanted=buys.Length==0?1:buys[^1];await using(var cmd=new NpgsqlCommand("SELECT COALESCE((SELECT market_buys FROM player_quest_runtime WHERE player_id=$1),0)>=$2",c,t)){cmd.Parameters.AddWithValue(player);cmd.Parameters.AddWithValue(wanted);return(Convert.ToBoolean(await cmd.ExecuteScalarAsync(ct)),null);}
            case "black_market_visit":
                await using(var cmd=new NpgsqlCommand("SELECT COALESCE((SELECT black_market_visits FROM player_quest_runtime WHERE player_id=$1),0)>0",c,t)){cmd.Parameters.AddWithValue(player);return(Convert.ToBoolean(await cmd.ExecuteScalarAsync(ct)),null);}
            case "event_daily":
                var daily=Args(task.Target);var dailyCount=daily.Length==0?1:daily[^1];await using(var cmd=new NpgsqlCommand("SELECT COALESCE((SELECT daily_events FROM player_quest_runtime WHERE player_id=$1),0)>=$2",c,t)){cmd.Parameters.AddWithValue(player);cmd.Parameters.AddWithValue(dailyCount);return(Convert.ToBoolean(await cmd.ExecuteScalarAsync(ct)),null);}
            case "tech_inject":
                var inject=Args(task.Target);var times=inject.Length>1?inject[1]:1;await using(var cmd=new NpgsqlCommand("SELECT COALESCE((SELECT injected_count FROM player_technologies WHERE player_id=$1 AND technology_id=$2),0)>=$3",c,t)){cmd.Parameters.AddWithValue(player);cmd.Parameters.AddWithValue(inject[0]);cmd.Parameters.AddWithValue(times);return(Convert.ToBoolean(await cmd.ExecuteScalarAsync(ct)),null);}
            case "equip":
                return await EvaluateEquipAsync(c,t,player,Args(task.Target),false,ct);
            case "equip_on":
                return await EvaluateEquipAsync(c,t,player,Args(task.Target),true,ct);
            case "arms_weapon_on":
                return await EvaluateWeaponAsync(c,t,player,Args(task.Target),ct);
            case "check_arms_weapon":
                await using(var cmd=new NpgsqlCommand("SELECT COALESCE((SELECT arms_weapon_views FROM player_quest_runtime WHERE player_id=$1),0)>0",c,t)){cmd.Parameters.AddWithValue(player);return(Convert.ToBoolean(await cmd.ExecuteScalarAsync(ct)),null);}
            case "weapon_make_done":
                await using(var cmd=new NpgsqlCommand("SELECT count(*)>=6 FROM player_weapons WHERE player_id=$1 AND weapon_id BETWEEN 1 AND 6 AND level>=1",c,t)){cmd.Parameters.AddWithValue(player);return(Convert.ToBoolean(await cmd.ExecuteScalarAsync(ct)),null);}
            case "and":
                return await EvaluateCompositeAsync(c,t,player,task.Target.Raw,true,ct);
            case "or":
                return await EvaluateCompositeAsync(c,t,player,task.Target.Raw,false,ct);
            case "world_move":
                var moves=Args(task.Target);await using(var cmd=new NpgsqlCommand("SELECT COALESCE((SELECT world_moves FROM player_quest_runtime WHERE player_id=$1),0)>=$2",c,t)){cmd.Parameters.AddWithValue(player);cmd.Parameters.AddWithValue(moves.Length==0?1:moves[0]);return(Convert.ToBoolean(await cmd.ExecuteScalarAsync(ct)),null);}
            case "get_salary":
                await using(var cmd=new NpgsqlCommand("SELECT salary_claimed_on IS NOT NULL FROM players WHERE id=$1",c,t)){cmd.Parameters.AddWithValue(player);return(Convert.ToBoolean(await cmd.ExecuteScalarAsync(ct)),null);}
            case "building_output":
                var output=Args(task.Target);if(output[0] is<1 or>4)return(false,$"building-output:{output[0]}");var resources=await production.AccrueAndGetAsync(player,ct,c,t);var rate=output[0] switch{1=>resources.CopperPerHour,2=>resources.WoodPerHour,3=>resources.FoodPerHour,4=>resources.IronPerHour,_=>0};return(rate>=output[1],null);
            default:return(false,$"quest-target:{task.Target.Kind}");
        }
    }

    static async Task<(bool complete,string? dependency)> EvaluateEquipAsync(NpgsqlConnection c,NpgsqlTransaction? t,long player,int[] equip,bool equipped,CancellationToken ct)
    {
        if(equip.Length<4)return(false,"legacy-target-arguments");
        var sql="SELECT COALESCE(sum(num),0)>=$5 FROM player_equipment WHERE player_id=$1 AND goods_type=$2 AND quality>=$3 AND level>=$4"+(equipped?" AND owner_general_id IS NOT NULL":"");
        await using var cmd=new NpgsqlCommand(sql,c,t);cmd.Parameters.AddWithValue(player);cmd.Parameters.AddWithValue(equip[0]);cmd.Parameters.AddWithValue(equip[1]);cmd.Parameters.AddWithValue(equip[2]);cmd.Parameters.AddWithValue(equip[3]);return(Convert.ToBoolean(await cmd.ExecuteScalarAsync(ct)),null);
    }

    static async Task<(bool complete,string? dependency)> EvaluateWeaponAsync(NpgsqlConnection c,NpgsqlTransaction? t,long player,int[] args,CancellationToken ct)
    {
        if(args.Length<2)return(false,"legacy-target-arguments");
        await using var cmd=new NpgsqlCommand("SELECT COALESCE((SELECT level FROM player_weapons WHERE player_id=$1 AND weapon_id=$2),-1)>=$3",c,t);cmd.Parameters.AddWithValue(player);cmd.Parameters.AddWithValue(args[0]);cmd.Parameters.AddWithValue(args[1]);return(Convert.ToBoolean(await cmd.ExecuteScalarAsync(ct)),null);
    }

    async Task<(bool complete,string? dependency)> EvaluateCompositeAsync(NpgsqlConnection c,NpgsqlTransaction? t,long player,string raw,bool requireAll,CancellationToken ct)
    {
        var groups=raw.Split(';',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
        if(groups.Length<2)return(false,"legacy-composite-target");
        var results=new List<bool>();
        foreach(var group in groups.Skip(1))
        {
            var p=group.Split(',',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
            if(p.Length==0)continue;
            var args=p.Skip(1).Select(x=>int.TryParse(x,out var n)?n:(int?)null).Where(x=>x.HasValue).Select(x=>x!.Value).ToArray();
            (bool complete,string? dependency) result=p[0].ToLowerInvariant() switch
            {
                "equip"=>await EvaluateEquipAsync(c,t,player,args,false,ct),
                "equip_on"=>await EvaluateEquipAsync(c,t,player,args,true,ct),
                "arms_weapon_on"=>await EvaluateWeaponAsync(c,t,player,args,ct),
                _=>(false,$"quest-target:{p[0]}")
            };
            if(result.dependency is not null)return result;
            results.Add(result.complete);
            if(requireAll&&!result.complete)return(false,null);
            if(!requireAll&&result.complete)return(true,null);
        }
        return(results.Count>0&&(requireAll?results.All(x=>x):results.Any(x=>x)),null);
    }

    static readonly HashSet<string> SupportedRewards=new(StringComparer.OrdinalIgnoreCase){"ChiefExp","copper","lumber","food","iron","new_building","functionId","new_construction","auto_construction_stop","construction_complete","arms_weapon","brunch","new_incense"};
    static void EnsureRewardsSupported(TaskDefinition task){var unsupported=task.Reward.FirstOrDefault(x=>!SupportedRewards.Contains(x.Kind));if(unsupported is not null)throw new GameException("QUEST_REWARD_DEPENDENCY",$"Legacy quest reward dependency is not available: {unsupported.Kind}.",409);}
    async Task ApplyRewardAsync(NpgsqlConnection c,NpgsqlTransaction t,long player,RewardDefinition reward,CancellationToken ct)
    {
        var value=reward.Args.Length==0?0:Int(reward.Args[0]);
        if(reward.Kind.Equals("ChiefExp",StringComparison.OrdinalIgnoreCase)){await experience.AddAsync(c,t,player,value,ct);return;}
        if(reward.Kind.Equals("new_building",StringComparison.OrdinalIgnoreCase)){await using var cmd=new NpgsqlCommand("INSERT INTO player_buildings(player_id,building_id,level) VALUES($1,$2,1) ON CONFLICT DO NOTHING",c,t);cmd.Parameters.AddWithValue(player);cmd.Parameters.AddWithValue(value);await cmd.ExecuteNonQueryAsync(ct);return;}
        if(reward.Kind.Equals("functionId",StringComparison.OrdinalIgnoreCase)){await using var cmd=new NpgsqlCommand("INSERT INTO player_functions(player_id,function_id) VALUES($1,$2) ON CONFLICT DO NOTHING",c,t);cmd.Parameters.AddWithValue(player);cmd.Parameters.AddWithValue(value);await cmd.ExecuteNonQueryAsync(ct);return;}
        if(reward.Kind.Equals("new_construction",StringComparison.OrdinalIgnoreCase)){await using var cmd=new NpgsqlCommand("UPDATE players SET construction_slots=construction_slots+1 WHERE id=$1",c,t);cmd.Parameters.AddWithValue(player);await cmd.ExecuteNonQueryAsync(ct);return;}
        if(reward.Kind.Equals("auto_construction_stop",StringComparison.OrdinalIgnoreCase))return;
        if(reward.Kind.Equals("construction_complete",StringComparison.OrdinalIgnoreCase)){await using var cmd=new NpgsqlCommand("UPDATE player_buildings SET level=level+1,state=0,upgrade_complete_at=NULL WHERE player_id=$1 AND state=1",c,t);cmd.Parameters.AddWithValue(player);await cmd.ExecuteNonQueryAsync(ct);return;}
        if(reward.Kind.Equals("arms_weapon",StringComparison.OrdinalIgnoreCase)){await WeaponService.AssignAsync(c,t,content,player,value,ct);return;}
        if(reward.Kind.Equals("brunch",StringComparison.OrdinalIgnoreCase)){await using var cmd=new NpgsqlCommand("INSERT INTO player_quest_branches(player_id,branch_id) VALUES($1,$2) ON CONFLICT DO NOTHING",c,t);cmd.Parameters.AddWithValue(player);cmd.Parameters.AddWithValue(value);await cmd.ExecuteNonQueryAsync(ct);return;}
        if(reward.Kind.Equals("new_incense",StringComparison.OrdinalIgnoreCase)){await using var cmd=new NpgsqlCommand("INSERT INTO player_incense_unlocks(player_id,incense_id) VALUES($1,$2) ON CONFLICT DO NOTHING",c,t);cmd.Parameters.AddWithValue(player);cmd.Parameters.AddWithValue(value);await cmd.ExecuteNonQueryAsync(ct);return;}
        var column=reward.Kind.ToLowerInvariant() switch{"copper"=>"copper","lumber"=>"wood","food"=>"food","iron"=>"iron",_=>throw new GameException("QUEST_REWARD_DEPENDENCY",$"Legacy quest reward dependency is not available: {reward.Kind}.",409)};
        await using(var resource=new NpgsqlCommand($"UPDATE player_resources SET {column}={column}+$2 WHERE player_id=$1",c,t)){resource.Parameters.AddWithValue(player);resource.Parameters.AddWithValue(value);await resource.ExecuteNonQueryAsync(ct);}
    }
    static object[] RewardViews(TaskDefinition task)=>task.Reward.Select(x=>(object)new{kind=x.Kind,args=x.Args.Select(Int).ToArray()}).ToArray();
    static QuestView View(TaskDefinition task,bool complete,string? dependency)=>new(task.Id,task.Name,task.IntroLong,task.IntroShort,task.Target.Kind,Args(task.Target),complete,dependency,RewardViews(task));
}
