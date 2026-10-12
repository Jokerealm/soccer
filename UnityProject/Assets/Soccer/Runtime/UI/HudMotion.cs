using System.Collections.Generic;
using Soccer.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Soccer.Runtime
{
    /// <summary>只负责展示的动画与反馈；生命、能量和冷却仍读取 Core 的真实数值。</summary>
    public sealed class HudMotion
    {
        private readonly VisualElement root, dock, fullPortrait, compactPortrait, compactCard, healthGhost, energyGhost, healthFill;
        private VisualElement portrait => fullHud ? fullPortrait : compactPortrait;
        private readonly Label toast, energyDelta;
        private readonly HudSparks sparks;
        private readonly LowHealthVignette lowHealthVignette;
        private readonly CountdownPresentation countdown;
        public int CountdownBeatCount => countdown.BeatCount;
        public float CountdownFraction => countdown.Fraction;
        public bool CountdownVisible => countdown.Visible;
        public const float LowHealthPercent = 25;
        private readonly Dictionary<string, Tile> tiles = new Dictionary<string, Tile>();
        private VisualElement page;
        private float pageAge=1, dockAge=1, healthTarget=100, energyTarget=100, healthTrail=100, energyTrail=100;
        private float previousHealth=100, previousEnergy=100, healthDelay, energyDelay, damageAge=1, deltaAge=1, toastAge=2, toastLife=1.6f;
        private float portraitAge=1, phaseAge=1;
        private float lowHealthOpacity;
        private bool lowHealth;
        private bool initialized, enabled=true, fullHud=true;
        private string phaseText="";
        private Color portraitColor, toastColor;
        private readonly Color mint=new Color(.48f,.94f,.78f), gold=new Color(1,.78f,.36f), red=new Color(1,.36f,.25f);
        public float HealthTrailPercent => healthTrail;
        public int ActiveParticles => sparks.Count;
        public int ReadyFeedbackCount { get; private set; }
        public int DamageFeedbackCount { get; private set; }
        public int ResourceFeedbackCount { get; private set; }
        public bool TransitionRunning => pageAge < .28f || dockAge < .48f;

        private sealed class Tile
        {
            public VisualElement Element, Frame, Gleam, Icon;
            public CooldownRing Dial;
            public Texture2D ColorIcon, GrayIcon;
            public bool Grayscale;
            public float CompleteAge=1;
            public float Previous, PulseAge=1, RejectAge=1, Hover;
            public bool WasBlocked;
            public Color Color;
        }

        public HudMotion(VisualElement root)
        {
            this.root=root; dock=root.Q("battle-dock"); fullPortrait=root.Q("hero-portrait-frame");
            compactPortrait=root.Q("compact-portrait-frame"); compactCard=root.Q("compact-player-hud");
            healthGhost=DecorateBar(root.Q<ProgressBar>("health-progress"),"health-trail");
            energyGhost=DecorateBar(root.Q<ProgressBar>("energy-progress"),"energy-trail");
            healthFill=root.Q<ProgressBar>("health-progress").Q(className:"unity-progress-bar__progress");
            toast=root.Q<Label>("combat-toast"); energyDelta=root.Q<Label>("energy-delta");
            energyDelta.BringToFront();
            sparks=new HudSparks(); root.Q("match-page").Add(sparks);
            lowHealthVignette=new LowHealthVignette(); root.Q("match-page").Insert(0,lowHealthVignette);
            countdown=new CountdownPresentation(root);
            foreach(var id in new[]{"q","e","g","kick","tackle","weapon"})
            {
                var tile=new Tile {Element=root.Q(id+"-tile"),Color=mint};
                tile.Frame=tile.Element.Q(className:"ability-frame");
                tile.Icon=root.Q(id+"-symbol");
                tile.ColorIcon=tile.Icon.resolvedStyle.backgroundImage.texture;
                if(tile.ColorIcon!=null) tile.GrayIcon=Resources.Load<Texture2D>("FrontEnd/"+tile.ColorIcon.name+"-gray");
                var mask=root.Q(id+"-mask");
                tile.Dial=new CooldownRing(); mask.Add(tile.Dial);
                tile.Gleam=new VisualElement { pickingMode=PickingMode.Ignore }; tile.Gleam.AddToClassList("ability-gleam");
                tile.Frame.Add(tile.Gleam);
                tile.Element.RegisterCallback<PointerEnterEvent>(_=>tile.Hover=1);
                tile.Element.RegisterCallback<PointerLeaveEvent>(_=>tile.Hover=0);
                tiles.Add(id,tile);
            }
        }

        private static VisualElement DecorateBar(ProgressBar bar, string name)
        {
            var background=bar.Q(className:"unity-progress-bar__background");
            var trail=new VisualElement {name=name,pickingMode=PickingMode.Ignore}; trail.AddToClassList("resource-trail");
            background.Insert(0,trail);
            var ticks=new VisualElement {pickingMode=PickingMode.Ignore}; ticks.AddToClassList("resource-ticks");
            for(int i=1;i<10;i++)
            { var tick=new VisualElement(); tick.AddToClassList("resource-tick"); tick.style.left=Length.Percent(i*10); ticks.Add(tick); }
            background.Add(ticks);
            return trail;
        }

        public void Enter(VisualElement next, bool match)
        {
            if(page!=null) { page.style.opacity=1; Move(page,0,0); }
            page=next; pageAge=0; next.style.opacity=0; Move(next,0,14);
            countdown.Reset();
            if(match) { dockAge=0; dock.style.opacity=0; Move(dock,0,44); initialized=false; sparks.ClearParticles(); toastAge=2; phaseText=""; }
        }
        public void SetEnabled(bool value)
        {
            enabled=value;
            countdown.SetEffects(value);
            if(!value) { sparks.ClearParticles(); toastAge=2; damageAge=1; foreach(var tile in tiles.Values) tile.PulseAge=tile.RejectAge=1; }
        }
        public void SetFullHud(bool value)
        {
            if (fullHud == value) return;
            fullHud=value; dockAge=0; sparks.ClearParticles(); toast.text=""; toastAge=2; deltaAge=1; portraitAge=1;
            fullPortrait.style.scale=compactPortrait.style.scale=new Scale(Vector3.one);
            foreach(var tile in tiles.Values) tile.PulseAge=tile.RejectAge=1;
        }
        public void Reset(PlayerState p)
        {
            healthTrail=healthTarget=previousHealth=p.Health/p.Stats.MaxHealth*100;
            energyTrail=energyTarget=previousEnergy=p.Energy/p.Stats.MaxEnergy*100;
            healthDelay=energyDelay=0; initialized=true; sparks.ClearParticles(); damageAge=deltaAge=1;
            lowHealth=!p.IsDead && healthTarget<LowHealthPercent;
            lowHealthOpacity=0; lowHealthVignette.style.opacity=0;
            foreach(var tile in tiles.Values) {tile.Previous=0; tile.PulseAge=tile.RejectAge=tile.CompleteAge=1; tile.Dial.Visible=false;}
        }
        public void Observe(PlayerState p, bool hasBall, string phase)
        {
            if(!initialized) Reset(p);
            healthTarget=p.Health/p.Stats.MaxHealth*100; energyTarget=p.Energy/p.Stats.MaxEnergy*100;
            if(enabled && healthTarget<previousHealth-.2f)
            {
                healthTrail=Mathf.Max(healthTrail,previousHealth); healthDelay=.38f;
                portraitAge=0; portraitColor=damageAge<.3f ? red : gold; ResourceFeedbackCount++;
            }
            if(enabled && energyTarget<previousEnergy-1)
            {
                energyTrail=Mathf.Max(energyTrail,previousEnergy); energyDelay=.20f;
                energyDelta.text=$"−{previousEnergy-energyTarget:0}"; deltaAge=0; ResourceFeedbackCount++;
            }
            previousHealth=healthTarget; previousEnergy=energyTarget;
            lowHealth=!p.IsDead && healthTarget<LowHealthPercent;
            dock.EnableInClassList("low-health",lowHealth);
            fullPortrait.EnableInClassList("portrait-down",p.IsDead);
            compactPortrait.EnableInClassList("portrait-down",p.IsDead);
            fullPortrait.EnableInClassList("portrait-possession",hasBall && !p.IsDead);
            compactPortrait.EnableInClassList("portrait-possession",hasBall && !p.IsDead);
            healthFill.style.unityBackgroundImageTintColor=lowHealth ? red : new Color(.28f,.83f,.59f);
            if(phase!=phaseText) { phaseText=phase; phaseAge=0; }
        }
        public void ObserveCountdown(MatchState state) => countdown.Observe(state);
        public void PositionCountdown(Camera camera,MatchState state) => countdown.PositionOverBall(camera,state);

        public void Cooldown(string id, float remaining, float total, bool blocked)
        {
            var tile=tiles[id]; tile.Dial.Fraction=Mathf.Clamp01(remaining/Mathf.Max(.01f,total));
            if(!blocked && !tile.WasBlocked && tile.Previous>0 && remaining<=0)
            {
                tile.CompleteAge=0;
                if(enabled) { Pulse(id,gold,8); ReadyFeedbackCount++; }
            }
            tile.Dial.Visible=remaining>0 || tile.CompleteAge<.35f;
            SetGrayscale(tile,remaining>0 || blocked);
            tile.Previous=remaining; tile.WasBlocked=blocked;
        }
        public float CooldownFraction(string id) => tiles[id].Dial.Fraction;
        public float CooldownProgress(string id) => 1-tiles[id].Dial.Fraction;
        public bool CooldownRingVisible(string id) => tiles[id].Dial.Visible;
        public bool IconIsGrayscale(string id) => tiles[id].Grayscale;
        public void BlockIcon(string id,bool blocked) => SetGrayscale(tiles[id],blocked);
        public void BindIcon(string id,Texture2D color,Texture2D gray)
        {
            if(!tiles.TryGetValue(id,out var tile)) return;
            tile.ColorIcon=color; tile.GrayIcon=gray;
            tile.Icon.style.backgroundImage=new StyleBackground(tile.Grayscale && gray!=null ? gray : color);
        }
        private static void SetGrayscale(Tile tile,bool gray)
        {
            if(tile.Grayscale==gray) return;
            tile.Grayscale=gray;
            if(tile.ColorIcon!=null) tile.Icon.style.backgroundImage=new StyleBackground(gray && tile.GrayIcon!=null ? tile.GrayIcon : tile.ColorIcon);
        }
        public void Pulse(string id, Color color, int particles=8, bool rejected=false)
        {
            if(!enabled || !fullHud) return;
            var tile=tiles[id]; tile.Color=color; tile.PulseAge=0;
            if(rejected) tile.RejectAge=0;
            else Emit(tile.Frame.worldBound.center,color,particles);
        }
        public void Event(MatchEvent e, PlayerState p)
        {
            if(!enabled) return;
            if(e.Kind==MatchEventKind.Damage && e.TargetId==0)
            { damageAge=0; DamageFeedbackCount++; }
            else if(e.Kind==MatchEventKind.Stunned && e.TargetId==0) Message("被震晕",gold);
            else if(e.Kind==MatchEventKind.Knockout) Message(e.TargetId==0 ? "被击倒 · 原地恢复" : "击倒对手",e.TargetId==0 ? red : gold);
            else if(e.Kind==MatchEventKind.Respawn && e.ActorId==0)
            { portraitAge=0; portraitColor=mint; Emit(portrait.worldBound.center,mint,20); Message("恢复就绪",mint); }
            else if(e.Kind==MatchEventKind.Catch && e.ActorId==0)
            { portraitAge=0; portraitColor=gold; Emit(portrait.worldBound.center,gold,6); }
            else if(e.Kind==MatchEventKind.Goal)
            { Message(e.TargetId==(int)Team.Cyan ? "青队进球！" : "橙队进球！",gold,2); Emit(root.Q("cyan-score").worldBound.center,gold,24); }
        }
        private void Message(string text,Color color,float life=1.6f)
        {if(!fullHud) return; toast.text=text; toastColor=color; toastAge=0; toastLife=life;}
        private void Emit(Vector2 center,Color color,int count)
        { sparks.Burst(center-sparks.worldBound.position,color,count); }

        public void Tick(float dt, bool inMatch, bool paused=false)
        {
            dt=Mathf.Min(dt,.08f); pageAge+=dt;
            if(page!=null && pageAge<.4f)
            {float t=Mathf.Clamp01(pageAge/.28f); page.style.opacity=1-Mathf.Pow(1-t,3); Move(page,0,14*Mathf.Pow(1-t,3));}
            if(!inMatch) {sparks.ClearParticles(); lowHealthOpacity=0; lowHealthVignette.style.opacity=0; return;}
            countdown.Tick(dt,paused);
            // 低血量是持续状态信息，独立于命中反馈开关和完整／精简界面。
            lowHealthOpacity=Mathf.MoveTowards(lowHealthOpacity,lowHealth ? .42f : 0,dt*(.42f/.25f));
            lowHealthVignette.style.opacity=lowHealthOpacity;
            dockAge+=dt; float entry=1-Mathf.Pow(1-Mathf.Clamp01(dockAge/.48f),4);
            dock.style.opacity=entry; Move(dock,0,(1-entry)*44);
            compactCard.style.opacity=entry; Move(compactCard,(1-entry)*-6,0);
            healthDelay-=dt; energyDelay-=dt;
            if(healthTarget>=healthTrail || healthDelay<=0) healthTrail=Mathf.Lerp(healthTrail,healthTarget,1-Mathf.Exp(-9*dt));
            if(energyTarget>=energyTrail || energyDelay<=0) energyTrail=Mathf.Lerp(energyTrail,energyTarget,1-Mathf.Exp(-12*dt));
            healthGhost.style.width=Length.Percent(healthTrail); energyGhost.style.width=Length.Percent(energyTrail);
            deltaAge+=dt; float deltaOpacity=Mathf.Clamp01((.9f-deltaAge)/.3f);
            energyDelta.style.opacity=deltaOpacity;
            Move(energyDelta,0,-Mathf.Min(deltaAge,.9f)*11);
            damageAge+=dt; root.Q("damage-vignette").style.opacity=enabled ? Mathf.Clamp01(1-damageAge/.28f)*.55f : 0;
            portraitAge+=dt; float pulse=Mathf.Clamp01(1-portraitAge/.4f);
            portrait.style.scale=new Scale(new Vector3(1+pulse*.035f,1+pulse*.035f,1));
            Border(portrait,Color.Lerp(new Color(.56f,.75f,.70f),portraitColor,pulse));
            if(enabled && dock.ClassListContains("low-health")) Border(portrait,Color.Lerp(new Color(.55f,.27f,.22f),red,.5f+.5f*Mathf.Sin(Time.unscaledTime*5)));
            foreach(var tile in tiles.Values)
            {
                tile.PulseAge+=dt; tile.RejectAge+=dt;
                tile.CompleteAge+=dt;
                if(tile.Previous<=0 && tile.CompleteAge>=.35f) tile.Dial.Visible=false;
                float p1=Mathf.Clamp01(1-tile.PulseAge/.42f);
                tile.Frame.style.scale=new Scale(new Vector3(1+p1*.07f+tile.Hover*.025f,1+p1*.07f+tile.Hover*.025f,1));
                Border(tile.Frame,Color.Lerp(tile.Element.ClassListContains("armed") ? gold*.75f : new Color(.36f,.47f,.48f),tile.Color,Mathf.Max(p1,tile.Hover*.5f)));
                Move(tile.Element,tile.RejectAge<.24f ? Mathf.Sin(tile.RejectAge*90)*(1-tile.RejectAge/.24f)*3 : 0,0);
                float sweep=Mathf.Clamp01(tile.PulseAge/.45f);
                tile.Gleam.style.opacity=tile.PulseAge<.45f ? Mathf.Sin(sweep*Mathf.PI)*.40f : 0;
                Move(tile.Gleam,Mathf.Lerp(-90,100,sweep),0);
            }
            toastAge+=dt; float appear=Mathf.Clamp01(toastAge/.1f),fade=Mathf.Clamp01((toastLife-toastAge)/.3f);
            toast.style.opacity=Mathf.Min(appear,fade); toast.style.color=toastColor; Move(toast,0,(1-appear)*8-(1-fade)*8);
            phaseAge+=dt; float phasePulse=Mathf.Clamp01(1-phaseAge/.22f);
            root.Q("phase-label").style.scale=new Scale(new Vector3(1+phasePulse*.14f,1+phasePulse*.14f,1));
            sparks.Tick(dt);
        }
        private static void Move(VisualElement e,float x,float y) => e.style.translate=new Translate(x,y,0);
        private static void Border(VisualElement e,Color color)
        { e.style.borderTopColor=e.style.borderBottomColor=e.style.borderLeftColor=e.style.borderRightColor=color; }
    }

    /// <summary>由屏幕四边向内渐隐的窄红色边缘，中央透明；不遮挡 UI 点击。</summary>
    internal sealed class LowHealthVignette : VisualElement
    {
        public LowHealthVignette()
        {
            name="low-health-vignette"; pickingMode=PickingMode.Ignore;
            style.position=Position.Absolute; style.left=style.top=style.right=style.bottom=0; style.opacity=0;
            generateVisualContent+=Draw;
        }
        private void Draw(MeshGenerationContext context)
        {
            float w=contentRect.width,h=contentRect.height;
            if(w<=0 || h<=0) return;
            float inset=Mathf.Min(w,h)*.055f;
            var mesh=context.Allocate(8,24);
            var corners=new[]{new Vector2(0,0),new Vector2(w,0),new Vector2(w,h),new Vector2(0,h),
                new Vector2(inset,inset),new Vector2(w-inset,inset),new Vector2(w-inset,h-inset),new Vector2(inset,h-inset)};
            for(int i=0;i<8;i++) mesh.SetNextVertex(new Vertex {
                position=new Vector3(corners[i].x,corners[i].y,Vertex.nearZ),tint=new Color(1,.12f,.10f,i<4 ? 1 : 0)});
            for(int i=0;i<4;i++)
            {
                int next=(i+1)%4;
                mesh.SetNextIndex((ushort)i); mesh.SetNextIndex((ushort)next); mesh.SetNextIndex((ushort)(next+4));
                mesh.SetNextIndex((ushort)i); mesh.SetNextIndex((ushort)(next+4)); mesh.SetNextIndex((ushort)(i+4));
            }
        }
    }

    internal sealed class CooldownRing : VisualElement
    {
        private float fraction;
        private bool ringVisible;
        public bool Visible { get=>ringVisible; set { if(ringVisible==value) return; ringVisible=value; MarkDirtyRepaint(); } }
        public float Fraction { get=>fraction; set { if(Mathf.Abs(fraction-value)<.0005f) return; fraction=value; MarkDirtyRepaint(); } }
        public CooldownRing()
        {
            pickingMode=PickingMode.Ignore; style.position=Position.Absolute; style.left=style.top=style.right=style.bottom=0;
            generateVisualContent+=Draw;
        }
        private void Draw(MeshGenerationContext context)
        {
            if(!ringVisible || contentRect.width<=0) return;
            const int segments=80;
            float progress=1-fraction;
            int count=Mathf.CeilToInt(segments*progress);
            var mesh=context.Allocate((segments+count)*4,(segments+count)*6);
            Vector2 center=contentRect.center;
            float radius=Mathf.Min(contentRect.width,contentRect.height)*.5f-2, inner=radius-2.3f;
            int index=0;
            for(int pass=0;pass<2;pass++)
            {
                int steps=pass==0 ? segments : count;
                float sweep=pass==0 ? 1 : progress;
                Color color=pass==0 ? new Color(.08f,.15f,.17f,.85f) : progress>=1 ? new Color(1,.82f,.43f) : new Color(.69f,.89f,.84f,.95f);
                for(int i=0;i<steps;i++)
                {
                    float a=-Mathf.PI/2+i/(float)steps*sweep*Mathf.PI*2;
                    float b=-Mathf.PI/2+(i+1)/(float)steps*sweep*Mathf.PI*2;
                    var p=new Vector2(Mathf.Cos(a),Mathf.Sin(a)); var q=new Vector2(Mathf.Cos(b),Mathf.Sin(b));
                    RingVertex(mesh,center+p*inner,color); RingVertex(mesh,center+p*radius,color);
                    RingVertex(mesh,center+q*radius,color); RingVertex(mesh,center+q*inner,color);
                    mesh.SetNextIndex((ushort)index); mesh.SetNextIndex((ushort)(index+1)); mesh.SetNextIndex((ushort)(index+2));
                    mesh.SetNextIndex((ushort)index); mesh.SetNextIndex((ushort)(index+2)); mesh.SetNextIndex((ushort)(index+3)); index+=4;
                }
            }
        }
        private static void RingVertex(MeshWriteData mesh,Vector2 p,Color color)
            => mesh.SetNextVertex(new Vertex {position=new Vector3(p.x,p.y,Vertex.nearZ),tint=color});
    }

    internal sealed class HudSparks : VisualElement
    {
        private struct Spark { public Vector2 Position,Velocity; public float Age,Life,Size; public Color Color; }
        private readonly Spark[] items=new Spark[80]; private int cursor;
        public int Count {get {int count=0; foreach(var item in items) if(item.Life>item.Age) count++; return count;}}
        public HudSparks()
        {
            pickingMode=PickingMode.Ignore; style.position=Position.Absolute; style.left=style.top=style.right=style.bottom=0;
            generateVisualContent+=Draw;
        }
        public void ClearParticles() { for(int i=0;i<items.Length;i++) items[i].Life=0; MarkDirtyRepaint(); }
        public void Burst(Vector2 center,Color color,int count)
        {
            for(int i=0;i<count;i++)
            {
                float angle=i*Mathf.PI*2/count+.35f;
                items[cursor++%items.Length]=new Spark { Position=center,Velocity=new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*Random.Range(40,105),Life=Random.Range(.35f,.65f),Size=Random.Range(1.5f,3.4f),Color=color};
            }
            MarkDirtyRepaint();
        }
        public void Tick(float dt)
        {
            bool dirty=false;
            for(int i=0;i<items.Length;i++)
            {
                var item=items[i]; if(item.Life<=item.Age) continue;
                item.Age+=dt; item.Velocity+=Vector2.down*(35*dt); item.Position+=item.Velocity*dt; items[i]=item; dirty=true;
            }
            if(dirty) MarkDirtyRepaint();
        }
        private void Draw(MeshGenerationContext context)
        {
            int count=Count; if(count==0) return; var mesh=context.Allocate(count*4,count*6); int index=0;
            foreach(var item in items)
            {
                if(item.Life<=item.Age) continue; Color color=item.Color; color.a=Mathf.Clamp01((item.Life-item.Age)/.2f);
                float size=item.Size*(1-item.Age/item.Life*.5f); Vector2 p=item.Position;
                foreach(var point in new[]{new Vector2(p.x,p.y-size*1.7f),new Vector2(p.x+size,p.y),new Vector2(p.x,p.y+size*1.7f),new Vector2(p.x-size,p.y)})
                    mesh.SetNextVertex(new Vertex {position=new Vector3(point.x,point.y,Vertex.nearZ),tint=color});
                mesh.SetNextIndex((ushort)index); mesh.SetNextIndex((ushort)(index+1)); mesh.SetNextIndex((ushort)(index+2));
                mesh.SetNextIndex((ushort)index); mesh.SetNextIndex((ushort)(index+2)); mesh.SetNextIndex((ushort)(index+3)); index+=4;
            }
        }
    }
}
