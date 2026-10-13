using System;
using System.Linq;

namespace Soccer.Core
{
    [Serializable] public sealed class HeroRecord
    {
        public CharacterId Character;
        public int Matches,Wins,Losses,Draws,Goals,Knockouts;
        public float WinRate => Matches==0 ? 0 : 100f*Wins/Matches;
        public void Add(MatchRecord match)
        {
            checked {Matches++;if(match.Result==MatchResult.Victory)Wins++;else if(match.Result==MatchResult.Defeat)Losses++;else Draws++;
            Goals+=match.Goals;Knockouts+=match.Knockouts;}
        }
        public HeroRecord Copy()=>new HeroRecord {Character=Character,Matches=Matches,Wins=Wins,Losses=Losses,Draws=Draws,Goals=Goals,Knockouts=Knockouts};
        public static HeroRecord[] Empty()=>new[]{new HeroRecord {Character=CharacterId.Marine},new HeroRecord {Character=CharacterId.Thor}};
        public static void Validate(HeroRecord[] records)
        {
            if(records==null || records.Length!=2 || records.Select(r=>r?.Character).Distinct().Count()!=2)throw new ArgumentException("角色战绩无效");
            foreach(var r in records)
                if(r==null || r.Character!=CharacterId.Marine && r.Character!=CharacterId.Thor || r.Matches<0 || r.Wins<0 || r.Losses<0 || r.Draws<0 || r.Goals<0 || r.Knockouts<0 || (long)r.Wins+r.Losses+r.Draws!=r.Matches)throw new ArgumentException("角色战绩无效");
        }
    }
    [Serializable] public sealed class PlayerProfileData
    {
        public string AccountId,DisplayName;
        public HeroRecord[] Heroes=HeroRecord.Empty();
        public MatchRecord[] Recent=new MatchRecord[0];
    }
    [Serializable] public sealed class SocialPerson {public string AccountId,DisplayName;}
    [Serializable] public sealed class FriendsData
    {
        public SocialPerson[] Friends=new SocialPerson[0],Incoming=new SocialPerson[0],Outgoing=new SocialPerson[0];
    }
}
