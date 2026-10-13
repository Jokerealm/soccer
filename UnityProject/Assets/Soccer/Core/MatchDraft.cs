using System;
using System.Linq;

namespace Soccer.Core
{
    public sealed class DraftPick
    {
        public int Slot;
        public CharacterId Character;
        public Vec2 Position;
        public bool Locked, Automatic;
    }
    /// <summary>Alternating six-slot selection. Only the active player can lock a valid home-half opening position.</summary>
    public sealed class MatchDraft
    {
        public static readonly int[] Order = { 0,3,1,4,2,5 };
        public const float TurnSeconds = 30, PlacementMargin = .85f, Separation = 1.8f;
        public MatchRules Rules { get; }
        public int Epoch { get; }
        public int TurnIndex { get; private set; }
        public int CurrentSlot => Complete ? -1 : Order[TurnIndex];
        public bool Complete => TurnIndex == 6;
        public float RemainingSeconds { get; private set; } = TurnSeconds;
        public DraftPick[] Picks { get; private set; }
        public bool[] Humans { get; }
        private float aiAge;
        public MatchDraft(MatchRules rules, bool[] humans, int epoch=1)
        {
            rules.Validate(); if (humans == null || humans.Length != 6 || epoch < 1) throw new ArgumentException("选人配置无效");
            Rules = rules; Humans = (bool[])humans.Clone(); Epoch = epoch;
            Picks = new DraftPick[6];
            for (int slot=0;slot<6;slot++) Picks[slot]=new DraftPick { Slot=slot,Character=slot%3==0 ? CharacterId.Marine : CharacterId.Thor,Position=DefaultPosition(rules,slot) };
        }
        public static Vec2 DefaultPosition(MatchRules rules,int slot)
        {
            int lane=slot%3; float side=slot<3 ? -1 : 1;
            float x=lane==0 ? Math.Max(rules.CenterCircleRadius+1.1f,rules.FieldWidth*.19f) : rules.FieldWidth*(lane==1 ? .30f : .39f);
            x=Math.Min(x,rules.FieldWidth*.5f-PlacementMargin);
            return new Vec2(side*x,lane==1 ? -rules.FieldHeight*.20f : lane==2 ? rules.FieldHeight*.20f : 0);
        }
        public bool CanPlace(int slot,Vec2 position,out string error)
        {
            if (slot<0 || slot>5 || !ValidFieldPoint(Rules,slot,position)) { error="请在我方半场内选择位置，并避开中圈保护区。";return false; }
            foreach(var pick in Picks)
                if(pick.Slot!=slot && pick.Locked && Vec2.Distance(position,pick.Position)<Separation)
                {error="该位置离已选球员太近，请换一个位置。";return false;}
            error="";return true;
        }
        public static bool ValidFieldPoint(MatchRules rules,int slot,Vec2 point)
        {
            if(slot<0 || slot>5 || !Geometry.IsFinite(point.X) || !Geometry.IsFinite(point.Y)) return false;
            float sign=slot<3 ? -1 : 1;
            return point.X*sign>=PlacementMargin && point.X*sign<=rules.FieldWidth*.5f-PlacementMargin &&
                Math.Abs(point.Y)<=rules.FieldHeight*.5f-PlacementMargin && point.Length>=rules.CenterCircleRadius+PlacementMargin;
        }
        public bool TryLock(int slot,CharacterId character,Vec2 position,out string error,bool automatic=false)
        {
            if(Complete || slot!=CurrentSlot) {error="请等待轮到你选择。";return false;}
            if(character!=CharacterId.Marine && character!=CharacterId.Thor) {error="角色尚未开放。";return false;}
            if(!CanPlace(slot,position,out error)) return false;
            var pick=Picks[slot];pick.Character=character;pick.Position=position;pick.Locked=true;pick.Automatic=automatic;
            TurnIndex++;RemainingSeconds=Complete ? 0 : TurnSeconds;aiAge=0;return true;
        }
        public void SetHuman(int slot,bool human) { if(slot>=0 && slot<6) Humans[slot]=human; }
        public bool Tick(float seconds)
        {
            if(Complete || !Geometry.IsFinite(seconds) || seconds<0) return false;
            RemainingSeconds=Math.Max(0,RemainingSeconds-seconds);aiAge+=seconds;
            if(Humans[CurrentSlot] && RemainingSeconds>0 || !Humans[CurrentSlot] && aiAge<.35f) return false;
            int slot=CurrentSlot;var pick=Picks[slot];Vec2 position=pick.Position;
            if(!CanPlace(slot,position,out _))
            {
                bool found=false;float best=float.PositiveInfinity;
                for(int x=0;x<16;x++) for(int y=0;y<16;y++)
                {
                    float sign=slot<3 ? -1 : 1;
                    var p=new Vec2(sign*(PlacementMargin+(Rules.FieldWidth*.5f-2*PlacementMargin)*x/15),
                        -Rules.FieldHeight*.5f+PlacementMargin+(Rules.FieldHeight-2*PlacementMargin)*y/15);
                    if(CanPlace(slot,p,out _) && Vec2.Distance(p,pick.Position)<best) {position=p;best=Vec2.Distance(p,pick.Position);found=true;}
                }
                if(!found) throw new InvalidOperationException("无法为球员安排开局位置");
            }
            return TryLock(slot,pick.Character,position,out _,true);
        }
        public CharacterId[] Lineup() => Picks.Select(p=>p.Character).ToArray();
        public Vec2[] Positions() => Picks.Select(p=>p.Position).ToArray();
        internal void Restore(int turn,float remaining,DraftPick[] picks)
        {
            if(turn<0 || turn>6 || !Geometry.IsFinite(remaining) || remaining<0 || remaining>TurnSeconds || picks==null || picks.Length!=6) throw new ArgumentException("选人状态无效");
            for(int i=0;i<6;i++)
            {
                var p=picks[i];
                if(p==null || p.Slot!=i || p.Locked!=(Array.IndexOf(Order,i)<turn) ||
                    p.Character!=CharacterId.Marine && p.Character!=CharacterId.Thor || !ValidFieldPoint(Rules,i,p.Position)) throw new ArgumentException("选人状态无效");
                if(p.Locked) for(int j=0;j<i;j++) if(picks[j].Locked && Vec2.Distance(p.Position,picks[j].Position)<Separation) throw new ArgumentException("开局位置重叠");
            }
            Picks=picks;TurnIndex=turn;RemainingSeconds=remaining;
        }
        public static void ValidateOpening(MatchRules rules,Vec2[] positions)
        {
            if(positions==null || positions.Length!=6) throw new ArgumentException("开局位置需要六个球员槽位");
            for(int i=0;i<6;i++)
            {
                if(!ValidFieldPoint(rules,i,positions[i])) throw new ArgumentException("开局位置超出合法区域");
                for(int j=0;j<i;j++) if(Vec2.Distance(positions[i],positions[j])<Separation) throw new ArgumentException("开局位置重叠");
            }
        }
    }
}
