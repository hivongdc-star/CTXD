using System.Text.Json;
using System.Text.Json.Serialization;

namespace CTXD.Server.Domain;

public sealed class BuildingDefinition
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Intro { get; set; } = "";
    public int OpenLevel { get; set; }
    public int Position { get; set; }
    public int AreaType { get; set; }
    public int OutputType { get; set; }
    public double OutputExponent { get; set; }
    public int OutputSeriesId { get; set; }
    public double OutputRelatedFactor { get; set; }
    public int[] OutputRelatedBuildings { get; set; } = [];
    public double TimeExponent { get; set; }
    public int TimeBase { get; set; }
    public int TimeSeriesId { get; set; }
    public int TimeRSeriesId { get; set; }
    public int TimeTSeriesId { get; set; }
    public double CopperExponent { get; set; }
    public int CopperSeriesId { get; set; }
    public double WoodExponent { get; set; }
    public int WoodSeriesId { get; set; }
    public int DrawingId { get; set; }
    public double ChiefExpExponent { get; set; }
    public int ChiefExpSeriesId { get; set; }
}
public sealed class TaskDefinition
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int NextTaskId { get; set; }
    public int Area { get; set; }
    public TargetDefinition Target { get; set; } = new();
    public RewardDefinition[] Reward { get; set; } = [];
    public string IntroLong { get; set; } = "";
    public string IntroShort { get; set; } = "";
    public string Plot { get; set; } = "";
}
public sealed class MarketProductDefinition { [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] public int Id { get; set; } [JsonPropertyName("item_type")] public string ItemType { get; set; }=""; [JsonPropertyName("cost_type")] public string CostType { get; set; }=""; [JsonPropertyName("item_num"),JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] public int ItemNum { get; set; } [JsonPropertyName("cost_num"),JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] public int CostNum { get; set; } [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] public int Degree { get; set; } [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] public int Quality { get; set; } [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] public double Prob { get; set; } }
public sealed class MarketDegreeDefinition { [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] public int Degree { get; set; } [JsonPropertyName("min_lv"),JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] public int MinLevel { get; set; } [JsonPropertyName("max_lv"),JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] public int MaxLevel { get; set; } [JsonPropertyName("q_list")] public string QualityList { get; set; }=""; [JsonPropertyName("iron_prob"),JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] public double IronProbability { get; set; } }
public sealed class MarketIronDefinition { [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] public int Id { get; set; } [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] public int Degree { get; set; } [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] public double Prob { get; set; } [JsonPropertyName("item_num"),JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] public int ItemNum { get; set; } [JsonPropertyName("cost_num"),JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] public int CostNum { get; set; } [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] public int Quality { get; set; } }
public sealed class ConstantDefinition { public string Value { get; set; }="0"; public int Id { get; set; } }
public sealed class WorldCitySpecialDefinition { [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] public int Key { get; set; } [JsonPropertyName("city_id"),JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] public int CityId { get; set; } [JsonPropertyName("par_1"),JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] public double Parameter1 { get; set; } [JsonPropertyName("par_2"),JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] public double Parameter2 { get; set; } }
public sealed class HourlyRewardDefinition { [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] public int Id { get; set; } [JsonPropertyName("reward_food"),JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] public int Food { get; set; } [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] public double Prob { get; set; } }
public sealed class TargetDefinition { public string Kind { get; set; } = ""; public JsonElement[] Args { get; set; } = []; public string Raw { get; set; } = ""; }
public sealed class RewardDefinition { public string Kind { get; set; } = ""; public JsonElement[] Args { get; set; } = []; }
public sealed class NameWord { public string Word { get; set; } = ""; public int Intonation { get; set; } }
public sealed class NameData
{
    public NameWord[] Male { get; set; } = [];
    public NameWord[] Female { get; set; } = [];
    public NameWord[] Last { get; set; } = [];
    public string[] UncommonLast { get; set; } = [];
    public NameWord[] Samples { get; set; } = [];
}

public sealed class GeneralDefinition
{
    public int Id { get; set; } public string Name { get; set; }=""; public int Type { get; set; } public string Pic { get; set; }="";
    public int Quality { get; set; } public int Leader { get; set; } public int Strength { get; set; } public int Intel { get; set; }
    public int Politics { get; set; } public int TroopId { get; set; } public int TacticId { get; set; } public int StratagemId { get; set; }
    public int UpgradeExpSeriesId { get; set; } public double UpgradeExpExponent { get; set; } public string Intro { get; set; }=""; public int Broadcast { get; set; }
}
public sealed class GeneralRecruitDefinition
{
    public int Id { get; set; } public int GeneralId { get; set; } public int Type { get; set; } public int PowerId { get; set; }
    public int NpcId { get; set; } public int DropIndex { get; set; } public int CopperMin { get; set; } public int CopperMax { get; set; }
    public int GoldMin { get; set; } public int GoldMax { get; set; } public double GoldProb { get; set; } public int MinRefreshTime { get; set; } public string Intro { get; set; }="";
}
public sealed class EquipmentDefinition
{
    public int Id { get; set; } public string Name { get; set; }=""; public int Type { get; set; } public string Pic { get; set; }="";
    public int Quality { get; set; } public int Level { get; set; } public int DefaultLevel { get; set; } public int MaxLevel { get; set; }
    public int Attribute { get; set; } public int CopperBuy { get; set; } public int CopperSold { get; set; } public int SkillType { get; set; }
    public int SkillNum { get; set; } public int SkillLevelDefault { get; set; } public int SkillLevelMax { get; set; }
    public double ProbBase { get; set; } public double ProbIntimacy { get; set; } public int IntimacyGroup { get; set; }
    public double IntimacyGroupProb { get; set; } public string Intro { get; set; }="";
}
public sealed class ItemDefinition
{
    public int Id { get; set; } public string Name { get; set; }=""; public int Type { get; set; } public int Index { get; set; }
    public int Quality { get; set; } public string Pic { get; set; }=""; public int Copper { get; set; } public string Effect { get; set; }="";
    public string Intro { get; set; }=""; public int ChangeItemId { get; set; } public int ChangeNum { get; set; }
}
public sealed class TechnologyDefinition
{
    public int Id { get; set; } public int Key { get; set; } public string KeyString { get; set; }=""; public string Name { get; set; }="";
    public string Pic { get; set; }=""; public string Intro { get; set; }=""; public int ResearchTime { get; set; } public string Resource { get; set; }="";
    public int ResourceTimes { get; set; } public int DropIndex { get; set; } public double[] Parameters { get; set; }=[]; public string[] ParameterIntros { get; set; }=[];
}
public sealed class TroopDefinition
{
    public int Id { get; set; } public string Name { get; set; }=""; public int Type { get; set; } public int Quality { get; set; }
    public int Level { get; set; } public int Serial { get; set; } public int Attack { get; set; } public int Defense { get; set; }
    public int Speed { get; set; } public int OpenLevel { get; set; } public string TerrainSpec { get; set; }="";
    public string TerrainStrategy { get; set; }=""; public string TerrainStrategyDefense { get; set; }=""; public string Drop { get; set; }="";
}
public sealed class TacticDefinition
{
    public int Id { get; set; } public string Name { get; set; }=""; public int DisplayId { get; set; } public string Pic { get; set; }="";
    public string BasicPic { get; set; }=""; public int Range { get; set; } public int PlayerTime { get; set; } public double DamageExponent { get; set; }
    public string SpecialEffect { get; set; }=""; public string Intro { get; set; }="";
}
public sealed class ArmyDefinition
{
    [JsonPropertyName("general_id")] public int GeneralId { get; set; }
    [JsonPropertyName("troop_id")] public int TroopId { get; set; }
    [JsonPropertyName("general_lv")] public int GeneralLevel { get; set; }
    [JsonPropertyName("army_hp")] public int ArmyHp { get; set; }
    [JsonPropertyName("troop_hp")] public int TroopHp { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("effect")] public string Effect { get; set; } = "";
}
public sealed class FightStrategyDefinition { public int Id { get; set; } public string Name { get; set; }=""; [JsonPropertyName("base_damage")] public int BaseDamage { get; set; } public string Pic { get; set; }=""; }
public sealed class FightStrategyCoefficientDefinition { public int Id { get; set; } [JsonPropertyName("att_strategy")] public int AttackerStrategy { get; set; } [JsonPropertyName("def_strategy")] public int DefenderStrategy { get; set; } [JsonPropertyName("winer_side")] public int WinnerSide { get; set; } [JsonPropertyName("att_lost")] public double AttackerLost { get; set; } [JsonPropertyName("def_lost")] public double DefenderLost { get; set; } }
public sealed class FightRewardCoefficientDefinition { public int Id { get; set; } public double C { get; set; } public double E { get; set; } public double M { get; set; } [JsonPropertyName("lv_coe")] public int LevelCoefficient { get; set; } public int Delta { get; set; } }
public sealed class TroopConscriptDefinition { [JsonPropertyName("troop_id")] public int TroopId { get; set; } public double Food { get; set; } }
public sealed class KingdomLevelDefinition { [JsonPropertyName("lv")] public int Level { get; set; } [JsonPropertyName("exp_upgrade")] public int UpgradeExp { get; set; } [JsonPropertyName("exp_per_task")] public int ExpPerTask { get; set; } [JsonPropertyName("reward_chief_exp")] public int RewardChiefExp { get; set; } [JsonPropertyName("reward_iron")] public int RewardIron { get; set; } [JsonPropertyName("barbarain_lv")] public int BarbarianLevel { get; set; } }
public sealed class OfficialDefinition { public int Id { get; set; } public string Name { get; set; }=""; [JsonPropertyName("name_short")] public string ShortName { get; set; }=""; public int Output { get; set; } [JsonPropertyName("world_output_e1")] public double WorldOutputEffect { get; set; } }
public sealed class OfficerAffairDefinition { public int Id { get; set; } public string Name { get; set; }=""; [JsonPropertyName("max_level")] public int MaxLevel { get; set; } [JsonPropertyName("upgrade_interval")] public int UpgradeInterval { get; set; } [JsonPropertyName("open_lv")] public int OpenLevel { get; set; } [JsonPropertyName("resource_output")] public int ResourceOutput { get; set; } [JsonPropertyName("upgrade_output_increase")] public int UpgradeOutputIncrease { get; set; } [JsonPropertyName("resource_output_type")] public int ResourceOutputType { get; set; } public int Time { get; set; } [JsonPropertyName("officer_exp_output")] public int OfficerExpOutput { get; set; } }
public sealed class OfficerSpecialtyDefinition { public int Id { get; set; } public int Type { get; set; } public double Magnification { get; set; } }
public sealed class PoliticsEventDefinition { public int Id { get; set; } public int Type { get; set; } public string Name { get; set; }=""; public string Disc { get; set; }=""; public string Pic { get; set; }=""; [JsonPropertyName("disc_1")] public string Option1 { get; set; }=""; [JsonPropertyName("disc_2")] public string Option2 { get; set; }=""; [JsonPropertyName("reward_1")] public string Reward1 { get; set; }=""; [JsonPropertyName("reward_2")] public string Reward2 { get; set; }=""; [JsonPropertyName("gold_consume_1")] public int Gold1 { get; set; } [JsonPropertyName("gold_consume_2")] public int Gold2 { get; set; } }
public sealed class HallDefinition { public int Id { get; set; } public int Pri { get; set; } public int Degree { get; set; } [JsonPropertyName("official_id")] public int OfficialId { get; set; } public int Quality { get; set; } [JsonPropertyName("name_list")] public string Name { get; set; }=""; public string Pic { get; set; }=""; [JsonPropertyName("pic_1")] public string OccupiedPic { get; set; }=""; public int Order { get; set; } public int Output { get; set; } public int Chief { get; set; } public string Npcs { get; set; }=""; }
public sealed class CdExamDefinition
{
    public int Id { get; set; } [JsonPropertyName("name_2")] public string DisplayName { get; set; }=""; public string Name { get; set; }="";
    [JsonPropertyName("kd_lv")] public int KingdomLevel { get; set; } [JsonPropertyName("kd_exp")] public int KingdomExp { get; set; }
    [JsonPropertyName("g_num_0")] public int General0 { get; set; } [JsonPropertyName("g_num_1")] public int General1 { get; set; } [JsonPropertyName("g_num_2")] public int General2 { get; set; } [JsonPropertyName("g_num_3")] public int General3 { get; set; }
    [JsonPropertyName("open_kg_1")] public int Open1 { get; set; } [JsonPropertyName("open_kg_2")] public int Open2 { get; set; } [JsonPropertyName("open_kg_3")] public int Open3 { get; set; }
    [JsonPropertyName("win_r_exp")] public int WinExp { get; set; } [JsonPropertyName("win_r_iron")] public int WinIron { get; set; }
    [JsonPropertyName("ranking_base_exp")] public int RankingExp { get; set; } [JsonPropertyName("ranking_base_iron")] public int RankingIron { get; set; }
    [JsonPropertyName("rk_base_p_exp")] public int ProtectRankingExp { get; set; } [JsonPropertyName("rk_base_p_iron")] public int ProtectRankingIron { get; set; }
    [JsonPropertyName("win_con_p")] public int ProtectWinKills { get; set; }
    [JsonPropertyName("wei_cities_1")] public string WeiCities1 { get; set; }=""; [JsonPropertyName("wei_cities_2")] public string WeiCities2 { get; set; }=""; [JsonPropertyName("wei_cities_3")] public string WeiCities3 { get; set; }="";
    [JsonPropertyName("shu_cities_1")] public string ShuCities1 { get; set; }=""; [JsonPropertyName("shu_cities_2")] public string ShuCities2 { get; set; }=""; [JsonPropertyName("shu_cities_3")] public string ShuCities3 { get; set; }="";
    [JsonPropertyName("wu_cities_1")] public string WuCities1 { get; set; }=""; [JsonPropertyName("wu_cities_2")] public string WuCities2 { get; set; }=""; [JsonPropertyName("wu_cities_3")] public string WuCities3 { get; set; }="";
    [JsonPropertyName("wei_armies_0")] public string WeiArmies0 { get; set; }=""; [JsonPropertyName("wei_armies_1")] public string WeiArmies1 { get; set; }=""; [JsonPropertyName("wei_armies_2")] public string WeiArmies2 { get; set; }=""; [JsonPropertyName("wei_armies_3")] public string WeiArmies3 { get; set; }="";
    [JsonPropertyName("shu_armies_0")] public string ShuArmies0 { get; set; }=""; [JsonPropertyName("shu_armies_1")] public string ShuArmies1 { get; set; }=""; [JsonPropertyName("shu_armies_2")] public string ShuArmies2 { get; set; }=""; [JsonPropertyName("shu_armies_3")] public string ShuArmies3 { get; set; }="";
    [JsonPropertyName("wu_armies_0")] public string WuArmies0 { get; set; }=""; [JsonPropertyName("wu_armies_1")] public string WuArmies1 { get; set; }=""; [JsonPropertyName("wu_armies_2")] public string WuArmies2 { get; set; }=""; [JsonPropertyName("wu_armies_3")] public string WuArmies3 { get; set; }="";
}
public sealed class CdExamRankingDefinition { public int Id { get; set; } [JsonPropertyName("high_lv")] public int High { get; set; } [JsonPropertyName("low_lv")] public int Low { get; set; } public double E { get; set; } }
public sealed class InvestmentEventDefinition { public int Id { get; set; } public int S { get; set; } public int I { get; set; } public int T { get; set; } public int Cc { get; set; } public int Er { get; set; } public int Cd { get; set; } public long Cm { get; set; } [JsonPropertyName("cd_max")] public int CdMax { get; set; } }
public sealed class BarbarianDefinition { public int Id { get; set; } public int Degree { get; set; } public int Target { get; set; } public int Num { get; set; } [JsonPropertyName("lv")] public int Level { get; set; } public string Reward { get; set; }=""; [JsonPropertyName("wei_armies")] public string WeiArmies { get; set; }=""; [JsonPropertyName("shu_armies")] public string ShuArmies { get; set; }=""; [JsonPropertyName("wu_armies")] public string WuArmies { get; set; }=""; [JsonPropertyName("wei_i_armies")] public string WeiInvadeArmies { get; set; }=""; [JsonPropertyName("shu_i_armies")] public string ShuInvadeArmies { get; set; }=""; [JsonPropertyName("wu_i_armies")] public string WuInvadeArmies { get; set; }=""; }
public sealed class BarbarianRankingDefinition { public int Id { get; set; } [JsonPropertyName("barbarain_lv")] public int BarbarianLevel { get; set; } [JsonPropertyName("high_lv")] public int High { get; set; } [JsonPropertyName("low_lv")] public int Low { get; set; } [JsonPropertyName("reward_exp")] public int Exp { get; set; } [JsonPropertyName("reward_iron")] public int Iron { get; set; } }
public sealed class NationTaskInitDefinition { public int Id { get; set; } [JsonPropertyName("kt_type")] public int TaskType { get; set; } }
public sealed class NationTaskTypeDefinition { public int Id { get; set; } public string Name { get; set; }=""; public string Intro { get; set; }=""; public double Prob { get; set; } public int J1 { get; set; } public int J2 { get; set; } public int J3 { get; set; } }
public sealed class NationExpansionTargetDefinition { [JsonPropertyName("lv")] public int Level { get; set; } public int Win { get; set; } public int Tc { get; set; } [JsonPropertyName("exp_c")] public int ExpCoefficient { get; set; } [JsonPropertyName("re_t")] public double RewardTime { get; set; } [JsonPropertyName("re_r")] public double RewardRate { get; set; } }
public sealed class NationExpansionPeriodDefinition { public int Id { get; set; } public int T { get; set; } [JsonPropertyName("re_t")] public double RewardTime { get; set; } [JsonPropertyName("re_r")] public double RewardRate { get; set; } }
public sealed class NationBarbarianWaveDefinition { public int Id { get; set; } public int T { get; set; } public int N { get; set; } public string Wei { get; set; }=""; public string Shu { get; set; }=""; public string Wu { get; set; }=""; }
public sealed class NationSweepWaveDefinition { public int Id { get; set; } public int Index { get; set; } [JsonPropertyName("kindom_lv")] public int KingdomLevel { get; set; } public int N { get; set; } public string Wei { get; set; }=""; public string Shu { get; set; }=""; public string Wu { get; set; }=""; [JsonPropertyName("wei_armies")] public string WeiArmies { get; set; }=""; [JsonPropertyName("shu_armies")] public string ShuArmies { get; set; }=""; [JsonPropertyName("wu_armies")] public string WuArmies { get; set; }=""; }
public sealed class NationBorderWaveDefinition { public int Id { get; set; } public int Index { get; set; } [JsonPropertyName("kindom_lv")] public int KingdomLevel { get; set; } public int N { get; set; } public int T { get; set; } public int Td { get; set; } public int Tb { get; set; } public string Wei { get; set; }=""; public string Shu { get; set; }=""; public string Wu { get; set; }=""; }
public sealed class NationYellowTurbanNpcDefinition { public int Id { get; set; } [JsonPropertyName("kindom_lv")] public int KingdomLevel { get; set; } public int Type { get; set; } [JsonPropertyName("army_id")] public int ArmyId { get; set; } [JsonPropertyName("city_id")] public string CityIds { get; set; }=""; public int Num { get; set; } }
public sealed class NationCompetitionRoadDefinition { public int Id { get; set; } public string Cities { get; set; }=""; public int Type=>Id/100; }
public sealed class NationTaskRankingRewardDefinition { public int Id { get; set; } [JsonPropertyName("kindom_lv")] public int KingdomLevel { get; set; } [JsonPropertyName("high_lv")] public int HighRank { get; set; } [JsonPropertyName("low_lv")] public int LowRank { get; set; } [JsonPropertyName("reward_iron")] public int Iron { get; set; } [JsonPropertyName("reward_exp")] public int Exp { get; set; } [JsonPropertyName("task_r")] public int TaskOrder { get; set; } public int Period { get; set; } }
public sealed class WorldCityDefinition
{
    public int Id { get; set; } public string Name { get; set; }=""; public int Type { get; set; } public int Terrain { get; set; }
    public int TerrainEffectType { get; set; } public int Output { get; set; } public int Chief { get; set; } public int[] Npcs { get; set; }=[];
    public int WeiDistance { get; set; } public int ShuDistance { get; set; } public int WuDistance { get; set; }
    public int WeiArea { get; set; } public int ShuArea { get; set; } public int WuArea { get; set; }
    public int WeiMask { get; set; } public int ShuMask { get; set; } public int WuMask { get; set; } public int ShowMask { get; set; }
    public string Pic { get; set; }=""; public string Intro { get; set; }="";
    public int X { get; set; } public int Y { get; set; } public string Model { get; set; }="";
}
public sealed class WorldRoadDefinition
{
    public int Id { get; set; } public int Start { get; set; } public int End { get; set; } public int Length { get; set; }
    public string Trace { get; set; }=""; public string WeiReward { get; set; }=""; public string ShuReward { get; set; }=""; public string WuReward { get; set; }="";
}
public sealed class KfgzWorldCityDefinition
{
    public string Name { get; set; }=""; public string Food { get; set; }="0"; public string Terrain { get; set; }="0";
    [JsonPropertyName("world_id")] public string WorldId { get; set; }="0"; public string Iron { get; set; }="0"; public string Exp { get; set; }="0";
    [JsonPropertyName("force_id")] public string ForceId { get; set; }="0"; public string Type { get; set; }="0"; public string Id { get; set; }="0";
    public int CityId=>int.Parse(Id); public int World=>int.Parse(WorldId); public int InitialForce=>int.Parse(ForceId); public int TerrainId=>int.Parse(Terrain); public int CityType=>int.Parse(Type);
}
public sealed class KfgzWorldRoadDefinition
{
    public string End { get; set; }="0"; [JsonPropertyName("connect_minutes")] public string ConnectMinutes { get; set; }="0";
    public string Start { get; set; }="0"; public string Length { get; set; }="0"; [JsonPropertyName("world_id")] public string WorldId { get; set; }="0";
    public string Type { get; set; }="0"; public string Id { get; set; }="0"; [JsonPropertyName("disconnect_minutes")] public string DisconnectMinutes { get; set; }="0";
    public int RoadId=>int.Parse(Id); public int From=>int.Parse(Start); public int To=>int.Parse(End); public int Distance=>int.Parse(Length); public int World=>int.Parse(WorldId); public int RoadType=>int.Parse(Type); public int Connect=>int.Parse(ConnectMinutes); public int Disconnect=>int.Parse(DisconnectMinutes);
}

public sealed class GeneralPositionDefinition
{
    public int Id { get; set; }
    public int Type { get; set; }
    public int OpenLevel { get; set; }
    public string OpenTips { get; set; } = "";
    public string OpenIntro { get; set; } = "";
}
public sealed class TavernStateTransitionDefinition
{
    public int PreState { get; set; }
    public int NextState { get; set; }
    public double Probability { get; set; }
}
public sealed class ChargeItemDefinition
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Explain { get; set; } = "";
    public string Pic { get; set; } = "";
    public int IfShow { get; set; }
    public int Param { get; set; }
    public int Alert { get; set; }
    public int Level { get; set; }
    public int Cost { get; set; }
    public string Intro { get; set; } = "";
}
public sealed class StringConstantDefinition
{
    public int Id { get; set; }
    public string Value { get; set; } = "";
    public string Intro { get; set; } = "";
    public string Param { get; set; } = "";
    public string System { get; set; } = "";
}

public sealed class EquipSuitDefinition
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int MinChiefLevel { get; set; }
    public int Type { get; set; }
    public int MaxIntimacyLevel { get; set; }
    public int[] EquipmentIds { get; set; } = [];
    public int Quality { get; set; }
}
public sealed class StoreItemDefinition
{
    public int Id { get; set; }
    public int ItemId { get; set; }
    public int Copper { get; set; }
    public int Gold { get; set; }
    public double GoldProbability { get; set; }
    public int MinRefreshTime { get; set; }
}
public sealed class StoreStateTransitionDefinition
{
    public int PreState { get; set; }
    public int NextState { get; set; }
    public double Probability { get; set; }
}
