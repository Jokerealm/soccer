using System.Collections.Generic;
using Soccer.Core;
using UnityEngine;

namespace Soccer.Runtime
{
    /// <summary>击球事件驱动的接触爆发、蓄力圈和有历史路径的尾迹，不修改球速或碰撞。</summary>
    public sealed class KickEffectsView : MonoBehaviour
    {
        private const int MaxSamples=32;
        private readonly List<Vector3> points=new List<Vector3>(MaxSamples);
        private readonly List<float> times=new List<float>(MaxSamples);
        private readonly List<Burst> bursts=new List<Burst>(8);
        private LineRenderer trail,core,chargeBase,chargeArc;
        private float clock;
        private Color tint;
        public float LastKickStrength { get; private set; }
        public float ChargeFraction { get; private set; }
        public int TrailSamples => points.Count;
        public int ActiveBursts => bursts.FindAll(b=>b.Age<b.Lifetime).Count;
        public Color TrailTint => tint;
        public float PresentationTime => clock;
        private sealed class Burst
        {
            public LineRenderer Ring;
            public readonly LineRenderer[] Streaks=new LineRenderer[5];
            public Vector3 Center,Forward;
            public Color Color;
            public float Age=1,Lifetime=.32f,Power;
        }
        public void Initialize()
        {
            trail=ViewLines.Create(transform,"Soft football wake",.3f,Color.white,690);
            core=ViewLines.Create(transform,"Football speed filament",.12f,Color.white,691);
            var taper=new AnimationCurve(new Keyframe(0,0),new Keyframe(.75f,.70f),new Keyframe(1,1));
            trail.widthCurve=core.widthCurve=taper;
            trail.numCapVertices=core.numCapVertices=4;
            chargeBase=ViewLines.Create(transform,"Quiet charge halo",.018f,Color.white,40); chargeBase.positionCount=49;
            chargeArc=ViewLines.Create(transform,"Charge energy arc",.043f,Color.white,41); chargeArc.positionCount=49;
            for(int i=0;i<8;i++)
            {
                var b=new Burst {Ring=ViewLines.Create(transform,"Kick contact wave",.048f,Color.white,42)};
                b.Ring.positionCount=49;
                b.Ring.widthCurve=AnimationCurve.Linear(0,1,1,1);
                for(int k=0;k<5;k++) {b.Streaks[k]=ViewLines.Create(transform,"Kick directional stroke",.055f,Color.white,705);b.Streaks[k].widthCurve=AnimationCurve.Linear(0,1,1,1);}
                bursts.Add(b);
            }
            Clear();
        }
        public void Kick(MatchEvent e,MatchState state)
        {
            if(e.ActorId<0 || e.ActorId>=state.Players.Count) return;
            var p=state.Players[e.ActorId];
            LastKickStrength=Mathf.Clamp01(e.Strength);
            tint=Color.Lerp(p.Team==Team.Cyan ? new Color(.36f,.96f,.84f) : new Color(1,.60f,.28f),
                new Color(1,.82f,.35f),LastKickStrength*.85f);
            if(e.Shot==ShotPreparation.Power) tint=new Color(1,.69f,.25f);
            if(e.Shot==ShotPreparation.Curve) tint=Color.Lerp(tint,new Color(.70f,.58f,1),.48f);
            points.Clear(); times.Clear();
            var b=bursts.Find(item=>item.Age>=item.Lifetime) ?? bursts[0];
            b.Center=ArenaProjection.ToWorld(state.Ball.Position,.04f);
            b.Forward=ArenaProjection.ToWorld(p.Facing).normalized;
            b.Power=LastKickStrength; b.Color=tint; b.Age=0;
            b.Lifetime=.28f+LastKickStrength*.10f;
            RenderBurst(b);
        }
        public void Render(MatchState state,MatchRules rules,Vector3 ballPosition,bool animate,bool enabled)
        {
            if(!enabled) {Clear();return;}
            float dt=animate ? Mathf.Min(Time.unscaledDeltaTime,.1f) : 0;
            clock+=dt;
            foreach(var b in bursts) { b.Age+=dt; RenderBurst(b); }
            var p=state.Players[0];
            bool charging=p.Charging && !p.IsDead && state.Ball.OwnerId==0 && state.Phase==MatchPhase.Playing;
            ChargeFraction=charging ? Mathf.Clamp01(p.ChargeTime/(p.Preparation==ShotPreparation.Power ? p.Stats.PowerChargeSeconds : rules.ChargeSeconds)) : 0;
            chargeBase.enabled=chargeArc.enabled=charging;
            if(charging)
            {
                var ground=new Vector3(ballPosition.x,.038f,ballPosition.z);
                float r=.44f+ChargeFraction*.09f+Mathf.Sin(clock*13)*.016f*ChargeFraction;
                var c=Color.Lerp(new Color(.38f,.94f,.84f),new Color(1,.81f,.35f),ChargeFraction);
                SetColor(chargeBase,new Color(c.r,c.g,c.b,.22f));
                SetColor(chargeArc,new Color(c.r,c.g,c.b,.78f));
                Circle(chargeBase,ground,r,1,clock*.6f);
                Circle(chargeArc,ground,r,Mathf.Max(.012f,ChargeFraction),clock*.6f);
            }
            bool flying=state.Ball.OwnerId<0 && state.Ball.Velocity.Length>5 && state.Phase==MatchPhase.Playing;
            if(!flying) {points.Clear();times.Clear();}
            else if(animate)
            {
                while(times.Count>0 && clock-times[0]>.20f) {times.RemoveAt(0);points.RemoveAt(0);}
                if(points.Count>0 && Vector3.Distance(points[points.Count-1],ballPosition)>4) {points.Clear();times.Clear();}
                if(points.Count==0 || Vector3.Distance(points[points.Count-1],ballPosition)>.035f)
                {
                    if(points.Count==MaxSamples) {points.RemoveAt(0);times.RemoveAt(0);}
                    points.Add(ballPosition);times.Add(clock);
                }
            }
            trail.enabled=core.enabled=points.Count>=2;
            if(trail.enabled)
            {
                trail.positionCount=core.positionCount=points.Count;
                for(int i=0;i<points.Count;i++) {trail.SetPosition(i,points[i]);core.SetPosition(i,points[i]);}
                float speed=Mathf.Clamp01(state.Ball.Velocity.Length/18);
                trail.widthMultiplier=Mathf.Lerp(.17f,.40f,LastKickStrength)*Mathf.Lerp(.55f,1,speed);
                core.widthMultiplier=trail.widthMultiplier*.33f;
                trail.startColor=new Color(tint.r,tint.g,tint.b,0);trail.endColor=new Color(tint.r,tint.g,tint.b,.28f);
                core.startColor=new Color(1,.97f,.84f,0);core.endColor=new Color(1,.97f,.84f,.85f);
            }
        }
        private void RenderBurst(Burst b)
        {
            bool live=b.Age<b.Lifetime;
            b.Ring.enabled=live; foreach(var stroke in b.Streaks) stroke.enabled=live;
            if(!live) return;
            float t=b.Age/b.Lifetime,fade=(1-t)*(1-t);
            Circle(b.Ring,b.Center,Mathf.Lerp(.24f,1.05f+b.Power*.55f,1-(1-t)*(1-t)),1,0);
            SetColor(b.Ring,new Color(b.Color.r,b.Color.g,b.Color.b,fade*.72f));
            b.Ring.widthMultiplier=.055f*(1-t*.65f);
            for(int k=0;k<5;k++)
            {
                var direction=Quaternion.AngleAxis((k-2)*17,Vector3.up)*b.Forward;
                float spread=.25f+t*(.80f+b.Power*.7f);
                var stroke=b.Streaks[k];
                stroke.SetPosition(0,b.Center+Vector3.up*.20f+direction*spread);
                stroke.SetPosition(1,b.Center+Vector3.up*.20f+direction*(spread+.24f+b.Power*.25f)*(1-t*.13f));
                stroke.startColor=new Color(b.Color.r,b.Color.g,b.Color.b,0);
                stroke.endColor=new Color(1,.96f,.78f,fade);
                stroke.widthMultiplier=.035f+b.Power*.028f;
            }
        }
        private static void Circle(LineRenderer renderer,Vector3 center,float radius,float portion,float angle)
        {for(int i=0;i<49;i++) {float a=angle+i/48f*Mathf.PI*2*portion;renderer.SetPosition(i,center+new Vector3(Mathf.Cos(a)*radius,0,Mathf.Sin(a)*radius));}}
        private static void SetColor(LineRenderer renderer,Color color) {renderer.startColor=renderer.endColor=color;}
        public void Clear()
        {
            points.Clear();times.Clear();LastKickStrength=ChargeFraction=0;clock=0;tint=new Color(.36f,.96f,.84f);
            if(trail==null) return;
            trail.enabled=core.enabled=chargeBase.enabled=chargeArc.enabled=false;
            foreach(var b in bursts) {b.Age=b.Lifetime;b.Ring.enabled=false;foreach(var stroke in b.Streaks) stroke.enabled=false;}
        }
    }
}
