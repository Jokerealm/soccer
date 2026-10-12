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
        public Vector2 FocusGround => focus;
        public bool RevealingGoal { get; private set; }
        public Vector2 VisibleGroundSize => new Vector2(viewCamera.orthographicSize*2*viewCamera.aspect,
            viewCamera.orthographicSize*2/ArenaProjection.GroundYScale);
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
            if(app==null) app=FindObjectOfType<AppController>();
            if(app==null || app.Page!=AppPage.Match || app.Simulation==null || app.Paused) return;
            var state=app.Simulation.State;
            var player=state.Players[0];
            // 超宽屏也只看局部，不把整张地图压成缩略图。
            RevealingGoal=state.Phase==Soccer.Core.MatchPhase.GoalCelebration || app.CelebratingFinalGoal;
            float targetSize=RevealingGoal ? Mathf.Min(5.1f,12/viewCamera.aspect) : Mathf.Min(6.8f,15/viewCamera.aspect);
            viewCamera.orthographicSize=followedRound!=state.Round ? targetSize : Mathf.Lerp(viewCamera.orthographicSize,targetSize,
                1-Mathf.Exp(-Mathf.Min(Time.unscaledDeltaTime,.1f)/.22f));
            Vector2 target=new Vector2(player.Position.X,player.Position.Y);
            Vector2 lead=new Vector2(player.Velocity.X,player.Velocity.Y)*.16f;
            if(RevealingGoal) target=new Vector2((state.NextKickoff==Soccer.Core.Team.Amber ? 1 : -1)*(app.Simulation.Rules.FieldWidth*.5f-2),0);
            else if(state.Phase==Soccer.Core.MatchPhase.SetPiece) target=new Vector2(state.RestartPosition.X,state.RestartPosition.Y);
            else target+=Vector2.ClampMagnitude(lead,.65f);
            target=ClampFocus(target);
            if(followedRound!=state.Round) { focus=target; followedRound=state.Round; }
            else focus=Vector2.Lerp(focus,target,1-Mathf.Exp(-Mathf.Min(Time.unscaledDeltaTime,.1f)/.16f));
            PositionCamera();
        }
        public void ResetFraming()
        {
            if(app==null) app=FindObjectOfType<AppController>();
            if(app?.Simulation==null) return;
            var p=app.Simulation.State.Players[0];
            RevealingGoal=false;
            viewCamera.orthographicSize=Mathf.Min(6.8f,15/viewCamera.aspect);
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
        private void PositionCamera()
        {
            transform.position=new Vector3(focus.x,0,focus.y)-transform.forward*25;
        }
    }
}
