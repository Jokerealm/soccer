using System;
using System.Collections.Generic;
using Soccer.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Soccer.Runtime
{
    public sealed class ActiveTacticalSignal
    {
        public TacticalSignal Signal;
        public float Expires;
        public Vec2 Position(MatchState state) => Signal.TargetId>=0 && Signal.TargetId<state.Players.Count
            ? state.Players[Signal.TargetId].Position : Signal.Position;
    }

    /// <summary>Local camera controls and a team-only signal tray; no commands can control a watched player.</summary>
    public sealed class MatchTacticsUI
    {
        private readonly AppController app;
        private readonly VisualElement root, layer;
        private readonly WheelCanvas wheel;
        private readonly Label center, observation, groupsHint;
        private readonly Label[] choices = new Label[5];
        private readonly List<Label> worldSignals = new List<Label>();
        private readonly List<ActiveTacticalSignal> signals = new List<ActiveTacticalSignal>();
        public IReadOnlyList<ActiveTacticalSignal> Signals => signals;
        public ObservationGroups Groups {get;} = new ObservationGroups();
        public bool WheelOpen {get;private set;}
        public bool BlocksGameplay {get;private set;}
        public int Selection {get;private set;} = -1;
        public int SelectedPlayerId {get;private set;} = -1;
        private MatchSimulation session;
        private Vector2 wheelOrigin, gestureStart;
        private Vec2 signalPosition;
        private int signalTarget=-1;
        private float nextSignal;
        private bool cancelledUntilRelease;
        private ArenaCamera Camera => app.WorldCamera.GetComponent<ArenaCamera>();
        internal IPlayerInputState InputOverride;
        private IPlayerInputState InputSource => InputOverride ?? UnityPlayerInputState.Instance;
        private static readonly Color[] SignalColors = {
            new Color(1,.57f,.30f),new Color(1,.85f,.39f),new Color(.35f,.90f,.79f),new Color(.39f,.70f,1),new Color(1,.43f,.39f)
        };
        public static Color SignalColor(SignalKind kind) => SignalColors[(int)kind];

        public MatchTacticsUI(AppController controller,VisualElement ui)
        {
            app=controller;root=ui;
            layer=new VisualElement {name="tactics-layer",pickingMode=PickingMode.Ignore};layer.AddToClassList("tactics-layer");root.Q("match-page").Add(layer);
            observation=new Label {name="observation-status",pickingMode=PickingMode.Ignore};observation.AddToClassList("observation-status");layer.Add(observation);
            groupsHint=new Label {name="observation-groups",pickingMode=PickingMode.Ignore};groupsHint.AddToClassList("observation-groups");layer.Add(groupsHint);
            wheel=new WheelCanvas {name="signal-wheel",pickingMode=PickingMode.Ignore};wheel.AddToClassList("signal-wheel");layer.Add(wheel);
            for(int i=0;i<5;i++)
            {
                var label=new Label(TacticalSignal.Label((SignalKind)i)){name="signal-choice-"+i,pickingMode=PickingMode.Ignore};
                label.AddToClassList("signal-choice");float angle=i*Mathf.PI*2/5;
                label.style.left=150+Mathf.Sin(angle)*96-51;label.style.top=150-Mathf.Cos(angle)*96-18;
                wheel.Add(label);choices[i]=label;
            }
            center=new Label("移动选择\n松开发送"){pickingMode=PickingMode.Ignore};center.AddToClassList("signal-center");wheel.Add(center);
            CancelWheel();
        }

        private Vector2 Pointer => RuntimePanelUtils.ScreenToPanel(root.panel,new Vector2(InputSource.MousePosition.x,Screen.height-InputSource.MousePosition.y));
        private void EnsureSession()
        {
            if(session==app.Simulation)return;
            session=app.Simulation;Groups.Reset();signals.Clear();SelectedPlayerId=-1;nextSignal=0;CancelWheel();Camera.FollowSelf();
        }
        public void CancelWheel(){WheelOpen=false;Selection=-1;wheel.style.display=DisplayStyle.None;}
        public void SuspendInput(){cancelledUntilRelease=true;CancelWheel();BlocksGameplay=true;}
        public void Tick()
        {
            EnsureSession();BlocksGameplay=false;
            if(app.Page!=AppPage.Match || app.Paused || session==null || !Application.isFocused)
            {SuspendInput();return;}
            var point=Pointer;
            bool held=app.Keys.Held(GameAction.SignalWheel,InputSource) || InputSource.Held(KeyCode.Mouse2);
            if(!held)cancelledUntilRelease=false;
            if(held && !WheelOpen && !cancelledUntilRelease)OpenWheel(point);
            if(WheelOpen)
            {
                BlocksGameplay=true;
                if(InputSource.Down(KeyCode.Escape) || app.Keys.Down(GameAction.Cancel,InputSource)) {cancelledUntilRelease=true;CancelWheel();return;}
                SelectWheel(point);
                if(!held)ReleaseWheel();
                return;
            }
            if(app.Keys.Down(GameAction.CameraSelf,InputSource)){Camera.FollowSelf();SelectedPlayerId=app.LocalPlayerId;}
            if(InputSource.MouseDown(0) && app.UI.MiniMap.Contains(point))
            {
                int id=app.UI.MiniMap.PlayerAt(point);
                if(id>=0)ObservePlayer(id);
                else if(app.UI.MiniMap.TryGroundAt(point,out var ground)){Camera.ObserveGround(ground);SelectedPlayerId=-1;}
                BlocksGameplay=true;
            }
            for(int group=1;group<=9;group++)
            {
                if(!InputSource.Down((KeyCode)((int)KeyCode.Alpha0+group)) && !InputSource.Down((KeyCode)((int)KeyCode.Keypad0+group)))continue;
                if(InputSource.Held(KeyCode.LeftControl) || InputSource.Held(KeyCode.RightControl))
                {
                    int id=PlayerAt(point);if(id<0)id=SelectedPlayerId;
                    if(id>=0){Groups.Bind(group,id);SelectedPlayerId=id;}
                }
                else
                {
                    SelectedPlayerId=Groups.Target(group);
                    int id=Groups.Press(group,Time.realtimeSinceStartup);if(id>=0)ObservePlayer(id);
                }
            }
        }
        public void OpenWheel(Vector2 point)
        {
            EnsureSession();
            gestureStart=point;signalTarget=PlayerAt(point);signalPosition=GroundAt(point);
            float left=Mathf.Clamp(point.x-150,4,Mathf.Max(4,root.resolvedStyle.width-304));
            float top=Mathf.Clamp(point.y-150,4,Mathf.Max(4,root.resolvedStyle.height-304));
            wheelOrigin=new Vector2(left+150,top+150);
            wheel.style.left=left;wheel.style.top=top;
            wheel.style.display=DisplayStyle.Flex;wheel.BringToFront();WheelOpen=true;Selection=-1;
            wheel.Selected=-1;center.text="移动选择\n中央取消";
        }
        public void SelectWheel(Vector2 point)
        {
            Selection=Vector2.Distance(point,gestureStart)<8 ? -1 : TacticalSignal.WheelSelection(point.x-wheelOrigin.x,point.y-wheelOrigin.y);wheel.Selected=Selection;
            for(int i=0;i<5;i++)choices[i].EnableInClassList("signal-selected",i==Selection);
            center.text=Selection<0 ? "移动选择\n中央取消" : TacticalSignal.Label((SignalKind)Selection)+"\n松开发送";
            wheel.MarkDirtyRepaint();
        }
        public void ReleaseWheel()
        {
            int selected=Selection;CancelWheel();
            if(selected>=0)Send((SignalKind)selected,signalPosition,signalTarget);
        }
        public bool Send(SignalKind kind,Vec2 position,int target=-1)
        {
            EnsureSession();if(session==null || app.Page!=AppPage.Match || app.Paused || Time.realtimeSinceStartup<nextSignal)return false;
            if(kind==SignalKind.PassToMe){target=app.LocalPlayerId;position=session.State.Players[target].Position;}
            var rules=session.Rules;
            position=new Vec2(Mathf.Clamp(position.X,-rules.FieldWidth/2,rules.FieldWidth/2),Mathf.Clamp(position.Y,-rules.FieldHeight/2,rules.FieldHeight/2));
            var signal=new TacticalSignal {PlayerId=app.LocalPlayerId,Kind=kind,Position=position,TargetId=target};
            if(!signal.Valid(rules.FieldWidth,rules.FieldHeight))return false;
            bool sent;
            if(app.IsServiceMatch)sent=app.ServiceMatch.SendSignal(kind,position,target);
            else if(app.IsNetworkMatch)sent=app.Room.SendSignal(kind,position,target);
            else {Receive(signal);sent=true;}
            if(sent)nextSignal=Time.realtimeSinceStartup+1;return sent;
        }
        public void Receive(TacticalSignal signal)
        {
            EnsureSession();if(session==null || !signal.Valid(session.Rules.FieldWidth,session.Rules.FieldHeight) || signal.PlayerId>=session.State.Players.Count)return;
            if(session.State.Players[signal.PlayerId].Team!=session.State.Players[app.LocalPlayerId].Team)return;
            signals.Add(new ActiveTacticalSignal {Signal=signal,Expires=Time.realtimeSinceStartup+4.5f});if(signals.Count>8)signals.RemoveAt(0);
            app.Audio.PlayClick();
        }
        public void ObservePlayer(int id)
        {
            if(app.Simulation==null || id<0 || id>=app.Simulation.State.Players.Count)return;
            SelectedPlayerId=id;Camera.ObservePlayer(id);
        }
        public int PlayerAt(Vector2 point)
        {
            int mini=app.UI.MiniMap.PlayerAt(point);if(mini>=0 || app.UI.MiniMap.Contains(point))return mini;
            if(app.UI.IsPointerOverControl())return -1;
            int id=-1;float nearest=float.MaxValue;
            foreach(var player in session.State.Players)
            {
                var foot=app.WorldCamera.WorldToScreenPoint(ArenaProjection.ToWorld(player.Position));
                var head=app.WorldCamera.WorldToScreenPoint(ArenaProjection.ToWorld(player.Position,1.9f));
                var a=RuntimePanelUtils.ScreenToPanel(root.panel,new Vector2(foot.x,Screen.height-foot.y));
                var b=RuntimePanelUtils.ScreenToPanel(root.panel,new Vector2(head.x,Screen.height-head.y));
                float d=Mathf.Abs(point.x-a.x);
                if(foot.z>0 && d<30 && point.y>=Mathf.Min(a.y,b.y)-10 && point.y<=Mathf.Max(a.y,b.y)+10 && d<nearest){id=player.Id;nearest=d;}
            }
            return id;
        }
        private Vec2 GroundAt(Vector2 point)
        {
            if(app.UI.MiniMap.TryGroundAt(point,out var ground))return ground;
            var ray=app.WorldCamera.ScreenPointToRay(InputSource.MousePosition);
            return new Plane(Vector3.up,Vector3.zero).Raycast(ray,out float distance) ? ArenaProjection.ToGround(ray.GetPoint(distance)) : session.State.Players[app.LocalPlayerId].Position;
        }
        public void Render()
        {
            EnsureSession();if(session==null || app.Page!=AppPage.Match){CancelWheel();return;}
            signals.RemoveAll(s=>s.Expires<=Time.realtimeSinceStartup);
            int watched=Camera.ObservedPlayerId;
            observation.text=watched>=0 && watched!=app.LocalPlayerId ? "观察 "+PlayerName(watched)+"  ·  "+app.Keys.Label(GameAction.CameraSelf)+" 回到自己"
                : "小地图观察 · 中键 / "+app.Keys.Label(GameAction.SignalWheel)+" 信号 · 滚轮缩放 · Ctrl+滚轮旋转";
            var names=new List<string>();for(int i=1;i<=9;i++)if(Groups.Target(i)>=0)names.Add(i+" "+PlayerName(Groups.Target(i)));
            groupsHint.text=names.Count==0 ? "鼠标指向球员 Ctrl+1–9 编队 · 双击数字观察" : string.Join("   ",names);
            while(worldSignals.Count<signals.Count)
            {var label=new Label {pickingMode=PickingMode.Ignore};label.AddToClassList("world-signal");layer.Add(label);worldSignals.Add(label);}
            for(int i=0;i<worldSignals.Count;i++)
            {
                var label=worldSignals[i];bool active=i<signals.Count;label.style.display=active ? DisplayStyle.Flex : DisplayStyle.None;if(!active)continue;
                var signal=signals[i];var screen=app.WorldCamera.WorldToScreenPoint(ArenaProjection.ToWorld(signal.Position(session.State),2.2f));
                var point=RuntimePanelUtils.ScreenToPanel(root.panel,new Vector2(screen.x,Screen.height-screen.y));
                bool visible=screen.z>0 && point.x>0 && point.x<root.resolvedStyle.width && point.y>0 && point.y<root.resolvedStyle.height;
                label.style.display=visible ? DisplayStyle.Flex : DisplayStyle.None;
                label.text=PlayerName(signal.Signal.PlayerId)+" · "+TacticalSignal.Label(signal.Signal.Kind);
                label.style.color=SignalColor(signal.Signal.Kind);label.style.left=point.x-72;label.style.top=point.y-15;
                label.style.opacity=Mathf.Clamp01(signal.Expires-Time.realtimeSinceStartup);
            }
            app.UI.MiniMap.RenderSignals(signals,session.State);
            if(WheelOpen)wheel.BringToFront();
        }
        private string PlayerName(int id) => id==app.LocalPlayerId ? "你" :
            (session.State.Players[id].Team==session.State.Players[app.LocalPlayerId].Team ? "友·" : "敌·")+app.DraftName(id).Replace(" · 你","");

        private sealed class WheelCanvas : VisualElement
        {
            public int Selected=-1;
            public WheelCanvas(){generateVisualContent+=Draw;}
            private void Draw(MeshGenerationContext context)
            {
                const int segments=18;
                for(int sector=0;sector<5;sector++)
                {
                    Color color=sector==Selected ? new Color(.15f,.44f,.43f,.97f) : new Color(.035f,.10f,.14f,.96f);
                    var mesh=context.Allocate((segments+1)*2,segments*6);
                    for(int n=0;n<=segments;n++)
                    {
                        float angle=-Mathf.PI/2+(sector-.5f+(float)n/segments)*Mathf.PI*2/5;
                        var dir=new Vector2(Mathf.Cos(angle),Mathf.Sin(angle));
                        mesh.SetNextVertex(new Vertex {position=new Vector3(150+dir.x*42,150+dir.y*42,Vertex.nearZ),tint=color});
                        mesh.SetNextVertex(new Vertex {position=new Vector3(150+dir.x*146,150+dir.y*146,Vertex.nearZ),tint=color});
                    }
                    for(int n=0;n<segments;n++)
                    {ushort a=(ushort)(n*2);mesh.SetNextIndex(a);mesh.SetNextIndex((ushort)(a+1));mesh.SetNextIndex((ushort)(a+2));
                        mesh.SetNextIndex((ushort)(a+2));mesh.SetNextIndex((ushort)(a+1));mesh.SetNextIndex((ushort)(a+3));}
                }
            }
        }
    }
}
