using System;
using System.Collections.Generic;
using System.Linq;

namespace Soccer.Core
{
    public enum MatchSource { AI, LAN, Internet }
    public enum MatchResult { Victory, Defeat, Draw }
    [Serializable] public sealed class MatchRecord
    {
        public string MatchId;
        public long FinishedUnixSeconds;
        public MatchSource Source;
        public MatchResult Result;
        public CharacterId Character;
        public int OwnScore,OpponentScore,Goals,Steals,Kicks,Passes,Knockouts,Deaths,Coins;
        public float DurationSeconds,DamageDealt,HealingDone;
        public static MatchRecord FromFinishedMatch(MatchState state,int localPlayer,string id,MatchSource source,long finished,int coins)
        {
            if(state==null || state.Mode!=MatchMode.BotMatch || state.Phase!=MatchPhase.Finished || localPlayer<0 || localPlayer>=state.Players.Count)
                throw new ArgumentException("仅记录已完成的正式对局");
            var p=state.Players[localPlayer];int own=p.Team==Team.Cyan ? state.CyanScore : state.AmberScore,opponent=p.Team==Team.Cyan ? state.AmberScore : state.CyanScore;
            var record=new MatchRecord {MatchId=id,FinishedUnixSeconds=finished,Source=source,Character=p.Stats.Id,OwnScore=own,OpponentScore=opponent,
                Result=own>opponent ? MatchResult.Victory : own<opponent ? MatchResult.Defeat : MatchResult.Draw,DurationSeconds=state.ElapsedSeconds,
                Goals=p.Goals,Steals=p.Steals,Kicks=p.Kicks,Passes=p.Passes,Knockouts=p.Knockouts,Deaths=p.Deaths,DamageDealt=p.DamageDealt,HealingDone=p.HealingDone,Coins=coins};
            Validate(record);return record;
        }
        public static void Validate(MatchRecord value)
        {
            if(value==null || string.IsNullOrWhiteSpace(value.MatchId) || value.MatchId.Length>128 || value.FinishedUnixSeconds<1 || value.FinishedUnixSeconds>253402300799L ||
                !Enum.IsDefined(typeof(MatchSource),value.Source) || value.Character!=CharacterId.Marine && value.Character!=CharacterId.Thor ||
                value.Result!=(value.OwnScore>value.OpponentScore ? MatchResult.Victory : value.OwnScore<value.OpponentScore ? MatchResult.Defeat : MatchResult.Draw))throw new ArgumentException("对局记录无效");
            foreach(int number in new[]{value.OwnScore,value.OpponentScore,value.Goals,value.Steals,value.Kicks,value.Passes,value.Knockouts,value.Deaths,value.Coins})if(number<0)throw new ArgumentException("对局统计无效");
            foreach(float number in new[]{value.DurationSeconds,value.DamageDealt,value.HealingDone})if(!Geometry.IsFinite(number) || number<0)throw new ArgumentException("对局统计无效");
        }
    }
    [Serializable] public sealed class MatchHistoryData
    {public int Version=2;public List<MatchRecord> Matches=new List<MatchRecord>();public HeroRecord[] Heroes=HeroRecord.Empty();}
    public interface IMatchHistoryStore {MatchHistoryData Load();void Save(MatchHistoryData data);}
    public sealed class MatchHistory
    {
        public const int Limit=50;
        private readonly IMatchHistoryStore store;private MatchHistoryData data;
        public IReadOnlyList<MatchRecord> Matches=>data.Matches;
        public HeroRecord[] Heroes=>data.Heroes.Select(h=>h.Copy()).ToArray();
        public MatchHistory(IMatchHistoryStore storage)
        {store=storage ?? throw new ArgumentNullException(nameof(storage));data=store.Load() ?? new MatchHistoryData();Upgrade(data);Validate(data);data.Matches=data.Matches.OrderByDescending(m=>m.FinishedUnixSeconds).ToList();}
        public static void Upgrade(MatchHistoryData value)
        {
            if(value?.Version!=1)return;
            Validate(value);value.Heroes=HeroRecord.Empty();foreach(var match in value.Matches)value.Heroes.Single(h=>h.Character==match.Character).Add(match);value.Version=2;
        }
        public static void Validate(MatchHistoryData value)
        {
            if(value==null || value.Version!=1 && value.Version!=2 || value.Matches==null || value.Matches.Count>Limit)throw new ArgumentException("对局记录存档无效");
            var ids=new HashSet<string>(StringComparer.Ordinal);
            foreach(var item in value.Matches){MatchRecord.Validate(item);if(!ids.Add(item.MatchId))throw new ArgumentException("对局记录重复");}
            if(value.Version==2)HeroRecord.Validate(value.Heroes);
        }
        public bool Add(MatchRecord record)
        {
            MatchRecord.Validate(record);if(data.Matches.Any(m=>m.MatchId==record.MatchId))return false;
            var next=new MatchHistoryData {Heroes=Heroes,Matches=new[]{record}.Concat(data.Matches).OrderByDescending(m=>m.FinishedUnixSeconds).Take(Limit).ToList()};
            next.Heroes.Single(h=>h.Character==record.Character).Add(record);
            store.Save(next);data=next;return true;
        }
    }
}
