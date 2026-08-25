using System.Text.RegularExpressions;
using CTXD.Server.Data;
using Npgsql;

namespace CTXD.Server.Services;

public sealed class LegacyNameService(CanonicalContent content,GameDb db,IConfiguration cfg)
{
    readonly int _max=cfg.GetValue("Game:PlayerNameMaxLength",7);
    readonly Random _rng=Random.Shared;
    static readonly Regex Allowed=new(@"^[a-zA-Z0-9\u0100-\uffff]+$",RegexOptions.Compiled);

    public bool IsFormatValid(string name) => !string.IsNullOrWhiteSpace(name) && name.Length<=_max && Allowed.IsMatch(name) && !name.Any(char.IsPunctuation) && !name.Any(char.IsWhiteSpace);
    public async Task<bool> ExistsAsync(string name,CancellationToken ct)
    { await using var conn=await db.DataSource.OpenConnectionAsync(ct); await using var cmd=new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM players WHERE display_name=$1)",conn); cmd.Parameters.AddWithValue(name); return (bool)(await cmd.ExecuteScalarAsync(ct))!; }

    public async Task<IReadOnlyList<string>> GenerateAsync(bool male,int count,CancellationToken ct)
    {
        var list=new List<string>();
        for(var tries=0;tries<1000 && list.Count<count;tries++) {
            var n=GenerateOne(male);
            if(!IsFormatValid(n) || list.Contains(n) || await ExistsAsync(n,ct)) continue;
            list.Add(n);
        }
        return list;
    }
    string GenerateOne(bool male)
    {
        var first=male?content.Names.Male:content.Names.Female;
        if(first.Length==0) return "MM";
        if(_rng.NextDouble()<0.4 && content.Names.UncommonLast.Length>0)
            return content.Names.UncommonLast[_rng.Next(content.Names.UncommonLast.Length)] + first[_rng.Next(first.Length)].Word;
        if(content.Names.Last.Length==0) return first[_rng.Next(first.Length)].Word;
        for(var retry=0;retry<10;retry++) {
            var last=content.Names.Last[_rng.Next(content.Names.Last.Length)];
            var f1=first[_rng.Next(first.Length)];
            var name=last.Word+f1.Word; var tones=$"{last.Intonation}{f1.Intonation}";
            if(_rng.NextDouble()>0.75 && f1.Word.Length!=2) {
                var f2=first[_rng.Next(first.Length)];
                if(f2.Word.Length==1) { name+=f2.Word; tones+=f2.Intonation; }
            }
            if(!content.Names.Samples.Any(s=>s.Intonation.ToString()==tones)) return name;
        }
        var l=content.Names.Last[_rng.Next(content.Names.Last.Length)]; return l.Word+first[_rng.Next(first.Length)].Word;
    }
}
