using UnityEngine;

namespace Soccer.Runtime
{
    [RequireComponent(typeof(Camera))]
    [DefaultExecutionOrder(100)]
    public sealed class ArenaCamera : MonoBehaviour
    {
        private Camera viewCamera;
        private AppController app;
        private Vector2 focus;
        private int followedRound=-1;
        private bool menuMode;
        private float zoom=1,yaw;
        private Soccer.Core.MatchSimulation framedSession;
        internal IPlayerInputState InputOverride;
        public float Zoom => zoom;
        public float OrbitYaw => yaw;
        public Vector2 FocusGround => focus;
        public int ObservedPlayerId {get;private set;} = -1;
        private bool groundObservation;
        private Vector2 observedGround;
        public void ObservePlayer(int id) { ObservedPlayerId=id;groundObservation=false;followedRound=-1; }
        public void ObserveGround(Soccer.Core.Vec2 point) { ObservedPlayerId=-1;groundObservation=true;observedGround=new Vector2(point.X,point.Y);followedRound=-1; }
        public void FollowSelf() { ObservedPlayerId=-1;groundObservation=false;followedRound=-1; }
        public bool RevealingGoal { get; private set; }
        public Vector2 VisibleGroundSize
        {
            get
            {
                float x=viewCamera.orthographicSize*2*viewCamera.aspect,z=viewCamera.orthographicSize*2/ArenaProjection.GroundYScale;
                float c=Mathf.Abs(Mathf.Cos(yaw*Mathf.Deg2Rad)),s=Mathf.Abs(Mathf.Sin(yaw*Mathf.Deg2Rad));
                return new Vector2(c*x+s*z,s*x+c*z);
            }
        }
        private void Awake()
        {
            viewCamera = GetComponent<Camera>();
            transform.rotation = Quaternion.Euler(55, 0, 0);
            transform.position = -transform.forward * 25;
            viewCamera.farClipPlane = 80;
            // 两种 HUD 都叠在全窗口球场上，不再为底栏裁切场景视口。
            viewCamera.rect = new Rect(0, 0, 1, 1);
        }
        private void LateUpdate()
        {
            if (menuMode) { FrameMenu(); return; }
            if(app==null) app=FindObjectOfType<AppController>();
            if(app==null || app.Page!=AppPage.Match || app.Simulation==null || app.Paused) return;
            var state=app.Simulation.State;
            var input=InputOverride ?? UnityPlayerInputState.Instance;
            if((InputOverride!=null || Application.isFocused) && !app.UI.BlocksKeyNavigation && !app.UI.Tactics.BlocksGameplay && !app.UI.IsPointerOverControl(input.MousePosition))
            {
                float scroll=input.ScrollDelta;
                if(!float.IsNaN(scroll) && !float.IsInfinity(scroll) && Mathf.Abs(scroll)>.0001f)
                {
                    scroll=Mathf.Clamp(scroll,-20,20);
                    if(input.Held(KeyCode.LeftControl) || input.Held(KeyCode.RightControl))yaw=Mathf.Repeat(yaw+scroll*12,360);
                    else zoom=Mathf.Clamp(zoom*Mathf.Exp(-scroll*.09f),.6f,1.7f);
                }
            }
            transform.rotation=Quaternion.Euler(55,yaw,0);
            var player=state.Players[ObservedPlayerId>=0 && ObservedPlayerId<state.Players.Count ? ObservedPlayerId : app.LocalPlayerId];
            // 超宽屏也只看局部，不把整张地图压成缩略图。
            RevealingGoal=state.Phase==Soccer.Core.MatchPhase.GoalCelebration || app.CelebratingFinalGoal;
            float targetSize=(RevealingGoal ? Mathf.Min(5.1f,12/viewCamera.aspect) : Mathf.Min(6.8f,15/viewCamera.aspect))*zoom;
            float maximum=MaximumViewSize();targetSize=Mathf.Min(targetSize,maximum);
            viewCamera.orthographicSize=followedRound!=state.Round ? targetSize : Mathf.Lerp(viewCamera.orthographicSize,targetSize,
                1-Mathf.Exp(-Mathf.Min(Time.unscaledDeltaTime,.1f)/.22f));
            viewCamera.orthographicSize=Mathf.Min(viewCamera.orthographicSize,maximum);
            Vector2 target=new Vector2(player.Position.X,player.Position.Y);
            Vector2 lead=new Vector2(player.Velocity.X,player.Velocity.Y)*.16f;
            if(RevealingGoal) target=new Vector2((state.NextKickoff==Soccer.Core.Team.Amber ? 1 : -1)*(app.Simulation.Rules.FieldWidth*.5f-2),0);
            else if(state.Phase==Soccer.Core.MatchPhase.SetPiece) target=new Vector2(state.RestartPosition.X,state.RestartPosition.Y);
            else target+=Vector2.ClampMagnitude(lead,.65f);
            if(!RevealingGoal && (ObservedPlayerId>=0 || groundObservation))
                target=groundObservation ? observedGround : new Vector2(player.Position.X,player.Position.Y)+Vector2.ClampMagnitude(lead,.65f);
            target=ClampFocus(target);
            if(followedRound!=state.Round) { focus=target; followedRound=state.Round; }
            else focus=Vector2.Lerp(focus,target,1-Mathf.Exp(-Mathf.Min(Time.unscaledDeltaTime,.1f)/.16f));
            focus=ClampFocus(focus);PositionCamera();
        }
        public void SetMenuMode(bool enabled)
        {
            menuMode = enabled;
            if (enabled) FrameMenu();
            else transform.rotation = Quaternion.Euler(55, yaw, 0);
        }
        private void FrameMenu()
        {
            transform.rotation = Quaternion.Euler(18, 195, 0);
            viewCamera.orthographicSize = Mathf.Max(3.25f, 4.9f / viewCamera.aspect);
            var center = new Vector3(4.8f, 1.55f, 0) - transform.right * (viewCamera.orthographicSize * viewCamera.aspect * .37f);
            transform.position = center - transform.forward * 25;
        }
        public void ResetFraming()
        {
            FollowSelf();
            if(app==null) app=FindObjectOfType<AppController>();
            if(app?.Simulation==null) return;
            if(framedSession!=app.Simulation){framedSession=app.Simulation;zoom=1;yaw=0;}
            var p=app.Simulation.State.Players[app.LocalPlayerId];
            menuMode = false; transform.rotation = Quaternion.Euler(55, yaw, 0);
            RevealingGoal=false;
            viewCamera.orthographicSize=Mathf.Min(Mathf.Min(6.8f,15/viewCamera.aspect)*zoom,MaximumViewSize());
            var state=app.Simulation.State;
            focus=ClampFocus(state.Phase==Soccer.Core.MatchPhase.SetPiece ? new Vector2(state.RestartPosition.X,state.RestartPosition.Y) : new Vector2(p.Position.X,p.Position.Y));
            followedRound=app.Simulation.State.Round;
            PositionCamera();
        }
        private Vector2 ClampFocus(Vector2 target)
        {
            Vector2 visible=VisibleGroundSize;
            var rules=app.Simulation.Rules;
            float x=Mathf.Max(0,rules.FieldWidth*.5f+3-visible.x*.5f);
            float y=Mathf.Max(0,rules.FieldHeight*.5f+3-visible.y*.5f);
            return new Vector2(Mathf.Clamp(target.x,-x,x),Mathf.Clamp(target.y,-y,y));
        }
        private float MaximumViewSize()
        {
            float c=Mathf.Abs(Mathf.Cos(yaw*Mathf.Deg2Rad)),s=Mathf.Abs(Mathf.Sin(yaw*Mathf.Deg2Rad));
            var rules=app.Simulation.Rules;
            return Mathf.Min((rules.FieldWidth+6)/2/(c*viewCamera.aspect+s/ArenaProjection.GroundYScale),
                (rules.FieldHeight+6)/2/(s*viewCamera.aspect+c/ArenaProjection.GroundYScale));
        }
        private void PositionCamera()
        {
            transform.position=new Vector3(focus.x,0,focus.y)-transform.forward*25;
        }
    }
}
