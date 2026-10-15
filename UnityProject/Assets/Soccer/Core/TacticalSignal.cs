using System;

namespace Soccer.Core
{
    public enum SignalKind { Attack, Shoot, PassToMe, Defend, Attention }
    public sealed class TacticalSignal
    {
        public int PlayerId, TargetId = -1;
        public SignalKind Kind;
        public Vec2 Position;
        private static readonly string[] Labels = { "进攻", "射门", "给我传球", "防御", "注意" };
        public static string Label(SignalKind kind) => Labels[(int)kind];
        public static int WheelSelection(float x, float y)
        {
            if (x*x+y*y < 30*30) return -1;
            double angle = Math.Atan2(x,-y);
            if (angle < 0) angle += Math.PI*2;
            return (int)Math.Floor((angle+Math.PI/5)/(Math.PI*2/5))%5;
        }
        public bool Valid(float width, float height) => Enum.IsDefined(typeof(SignalKind),Kind) && PlayerId >= 0 && PlayerId < 6 && TargetId >= -1 && TargetId < 6 &&
            Geometry.IsFinite(Position.X) && Geometry.IsFinite(Position.Y) && Math.Abs(Position.X)<=width/2 && Math.Abs(Position.Y)<=height/2;
    }

    /// <summary>Groups only select a camera target; they never transfer player control.</summary>
    public sealed class ObservationGroups
    {
        private readonly int[] targets = new int[10];
        private int lastGroup = -1;
        private double lastPress = -10;
        public ObservationGroups() { Reset(); }
        public void Reset() { for(int i=0;i<targets.Length;i++)targets[i]=-1; lastGroup=-1;lastPress=-10; }
        public int Target(int group) => group>=1 && group<=9 ? targets[group] : -1;
        public void Bind(int group,int playerId)
        { if(group<1 || group>9 || playerId<0 || playerId>=6)throw new ArgumentOutOfRangeException();targets[group]=playerId;lastGroup=-1; }
        public int Press(int group,double now)
        {
            bool twice=lastGroup==group && now-lastPress>=0 && now-lastPress<=.32;
            lastGroup=group;lastPress=now;
            if(!twice)return -1;
            lastGroup=-1;return Target(group);
        }
    }
}
