using System;

namespace Soccer.Core
{
    /// <summary>Mirrored six-point opening formation. Coordinates are shared by UI, host and clients.</summary>
    public static class OpeningFormation
    {
        public const int Count=6;
        public static readonly string[] Names={"门前","左后","右后","中前","左前","右前"};
        public static Vec2 Position(MatchRules rules,int slot,int index)
        {
            if(slot<0 || slot>5 || index<0 || index>=Count)throw new ArgumentOutOfRangeException();
            float half=rules.FieldWidth*.5f, height=rules.FieldHeight*.5f;
            float x=index==0 ? half*.84f : index<3 ? half*.61f : index==3 ? half*.36f : half*.32f;
            float y=index==1 ? -height*.50f : index==2 ? height*.50f : index==4 ? -height*.54f : index==5 ? height*.54f : 0;
            // Keep every point in its own half and outside the opening centre-circle shield.
            x=Math.Min(half-MatchDraft.PlacementMargin,Math.Max(x,rules.CenterCircleRadius+MatchDraft.PlacementMargin+.05f));
            float side=slot<3 ? -1 : 1;
            return new Vec2(side*x,side<0 ? y : -y);
        }
        public static int IndexAt(MatchRules rules,int slot,Vec2 point)
        {
            if(slot<0 || slot>5 || !Geometry.IsFinite(point.X) || !Geometry.IsFinite(point.Y))return -1;
            for(int i=0;i<Count;i++)if(Vec2.Distance(point,Position(rules,slot,i))<=.025f)return i;
            return -1;
        }
    }
}
