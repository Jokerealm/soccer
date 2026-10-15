using System;
using System.Linq;

namespace Soccer.Core
{
    [Serializable] public sealed class ChatMessage
    {
        public long Id, SentUnixSeconds;
        public string SenderId="",SenderName="",Channel="",TargetId="",Text="";
    }
    [Serializable] public sealed class ChatHistory {public ChatMessage[] Messages=new ChatMessage[0];public long Cursor;}
    public static class ChatText
    {
        public const int MaxLength=160;
        public static string Normalize(string value)
        {
            value=(value??"").Trim();
            if(value.Length<1 || value.Length>MaxLength || value.Any(c=>char.IsControl(c) && c!='\t') || value.Any(char.IsSurrogate) && !ValidUnicode(value))throw new ArgumentException("消息须为 1–160 字，不能包含控制字符。");
            // UI labels never interpret player messages as markup.
            return value.Replace('\t',' ');
        }
        private static bool ValidUnicode(string value)
        {for(int i=0;i<value.Length;i++){if(char.IsHighSurrogate(value[i])){if(++i>=value.Length || !char.IsLowSurrogate(value[i]))return false;}else if(char.IsLowSurrogate(value[i]))return false;}return true;}
    }
    [Serializable] public sealed class PartyMember
    {public string AccountId="",Name="";public bool Ready,Online;public int Rating=1000;}
    [Serializable] public sealed class PartyData
    {public string Id="",LeaderId="";public PartyMember[] Members=new PartyMember[0];public string Queue="";public long QueuedAt;}
    [Serializable] public sealed class CommunityInvite
    {public string Id="",FromId="",FromName="",Kind="",Code="";public long Expires;}
    [Serializable] public sealed class CommunityRoom
    {public string Code="",OwnerId="";public PartyMember[] Slots=new PartyMember[6];public int[] Bots=new int[0];public float Countdown;public BotDifficulty Difficulty=BotDifficulty.Player;}
    [Serializable] public sealed class MatchAssignment
    {public string Id="",Playlist="",Phase="";public int Slot,BotMask;public string[] Names=new string[0];public int Rating,RatingChange;}
    [Serializable] public sealed class CommunityState
    {
        public long Revision;public string AccountId="",Name="";public int Rating=1000,RankedGames;public PartyData Party;public CommunityRoom Room;public MatchAssignment Match;public CommunityInvite[] Invites=new CommunityInvite[0];public int QueuedPlayers;public string[] OnlineFriends=new string[0];public ChatMessage[] Inbox=new ChatMessage[0];
        public CommunityState Normalize()
        {
            // Unity's inline JSON classes can materialize JSON null as an empty object.
            if(string.IsNullOrEmpty(Party?.Id))Party=null;
            if(string.IsNullOrEmpty(Room?.Code))Room=null;
            if(string.IsNullOrEmpty(Match?.Id))Match=null;
            if(Party!=null && (Party.Members==null || Party.Members.Length>3))throw new ArgumentException("队伍信息无效");
            if(Room!=null)
            {
                if(Room.Slots==null || Room.Slots.Length!=6 || Room.Bots==null)throw new ArgumentException("房间信息无效");
                for(int i=0;i<6;i++)if(string.IsNullOrEmpty(Room.Slots[i]?.AccountId))Room.Slots[i]=null;
            }
            if(Match!=null && (Match.Slot<0 || Match.Slot>=6 || Match.Names==null || Match.Names.Length!=6))throw new ArgumentException("对局信息无效");
            return this;
        }
    }
    [Serializable] public sealed class CommunityAction
    {public string Action="",Target="",Id="",Mode="",Code="",Name="";public bool Ready;public int Slot;public BotDifficulty Difficulty=BotDifficulty.Player;}
    [Serializable] public sealed class ChatRequest
    {public string Channel="private",Target="",Text="";}
}
