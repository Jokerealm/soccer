using Soccer.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Soccer.Runtime
{
    public sealed class DraftUI
    {
        private readonly AppController app;private readonly VisualElement root;
        private readonly DraftPitch pitch;
        private readonly Label[] members=new Label[6];
        private readonly Button[] points=new Button[OpeningFormation.Count];
        public DraftUI(AppController controller,VisualElement ui)
        {
            app=controller;root=ui;pitch=new DraftPitch(app);root.Q("draft-map").Add(pitch);
            for(int i=0;i<6;i++) {members[i]=new Label {name="draft-member-"+i};members[i].AddToClassList("draft-member");root.Q("draft-members").Add(members[i]);}
            for(int i=0;i<points.Length;i++){int index=i;points[i]=new Button(()=>app.SelectDraftPoint(index)){name="draft-point-"+i};points[i].AddToClassList("draft-point");root.Q("draft-slots").Add(points[i]);}
        }
        public void Refresh()
        {
            bool active=app.InDraft;
            root.Q("draft-roster").EnableInClassList("hidden",!active);root.Q("draft-placement").EnableInClassList("hidden",!active);
            if(!active)return;
            var draft=app.ActiveDraft;int local=app.DraftLocalSlot;bool locked=draft.Picks[local].Locked;
            root.Q<Label>("select-title").text="对局准备 · 选人布阵";
            root.Q<Label>("select-subtitle").text=(app.Room==null && app.ServiceMatch==null ? "AI 队伍已就绪" : "玩家已就绪")+" · 两队轮流选择 · 当前角色允许重复";
            root.Q<Label>("draft-turn").text=draft.Complete ? "阵容已确定" : app.DraftName(draft.CurrentSlot)+" 正在选择 · "+Mathf.CeilToInt(draft.RemainingSeconds)+" 秒";
            int selected=OpeningFormation.IndexAt(draft.Rules,local,app.DraftPosition);
            root.Q<Label>("draft-position-note").text=app.DraftStatus.Length>0 ? app.DraftStatus : (locked ? "已锁定：" : "当前：")+(selected<0 ? "请选择点位" : OpeningFormation.Names[selected])+" · 每队 6 个固定点，队友不能重叠";
            for(int i=0;i<points.Length;i++)
            {
                bool free=draft.CanPlace(local,OpeningFormation.Position(draft.Rules,local,i),out _);
                points[i].text=(i+1)+" "+OpeningFormation.Names[i]+(free ? "" : " · 已占");
                points[i].SetEnabled(!locked && free);points[i].EnableInClassList("draft-point-selected",selected==i);
            }
            for(int i=0;i<6;i++)
            {
                var pick=draft.Picks[i];members[i].text=(i<3 ? "青" : "橙")+(i%3+1)+"  "+app.DraftName(i)+"\n"+
                    (pick.Locked ? (pick.Character==CharacterId.Marine ? "枪兵" : "雷神")+" · 已锁定" : i==draft.CurrentSlot ? "正在选择" : "等待选择");
                members[i].EnableInClassList("draft-member-current",i==draft.CurrentSlot);members[i].EnableInClassList("draft-member-locked",pick.Locked);
            }
            root.Q<Button>("select-back").text=app.Room==null ? "‹ 返回模式选择" : "‹ 返回房间";
            var confirm=root.Q<Button>("start-button");confirm.text=locked ? "已锁定" : app.MyDraftTurn ? "锁定角色与位置" : "等待选择";
            confirm.SetEnabled(app.MyDraftTurn && draft.CanPlace(local,app.DraftPosition,out _));
            for(int i=0;i<app.Characters.Length;i++)root.Q<Button>("character-"+i).SetEnabled(!locked);
            pitch.Refresh(draft,local,app.DraftPosition);
        }
        private sealed class DraftPitch : VisualElement
        {
            private readonly AppController app;private MatchDraft draft;private int local;private Vec2 candidate;
            public DraftPitch(AppController controller)
            {
                app=controller;name="draft-pitch";style.flexGrow=1;style.minHeight=140;
                generateVisualContent+=Draw;
                RegisterCallback<PointerDownEvent>(evt=>
                {
                    if(evt.button!=0 || draft==null || draft.Picks[local].Locked)return;
                    var rect=Field();Vector2 point=evt.localPosition;
                    if(!rect.Contains(point))return;
                    for(int i=0;i<OpeningFormation.Count;i++)
                        if(Vector2.Distance(point,Project(OpeningFormation.Position(draft.Rules,local,i),rect))<=16){app.SelectDraftPoint(i);evt.StopPropagation();break;}
                });
            }
            public void Refresh(MatchDraft state,int slot,Vec2 point) {draft=state;local=slot;candidate=point;MarkDirtyRepaint();}
            private Rect Field()
            {
                if(draft==null)return default;
                float width=Mathf.Max(1,contentRect.width-24),height=Mathf.Max(1,contentRect.height-20);
                float scale=Mathf.Min(width/draft.Rules.FieldWidth,height/draft.Rules.FieldHeight);
                return new Rect((contentRect.width-draft.Rules.FieldWidth*scale)*.5f,(contentRect.height-draft.Rules.FieldHeight*scale)*.5f,draft.Rules.FieldWidth*scale,draft.Rules.FieldHeight*scale);
            }
            private Vector2 Project(Vec2 point,Rect field) => field.center+new Vector2(point.X/draft.Rules.FieldWidth*field.width,-point.Y/draft.Rules.FieldHeight*field.height);
            private void Draw(MeshGenerationContext context)
            {
                if(draft==null)return;var field=Field();var painter=context.painter2D;
                painter.fillColor=new Color(.06f,.13f,.18f);painter.BeginPath();painter.MoveTo(field.min);painter.LineTo(new Vector2(field.xMax,field.yMin));painter.LineTo(field.max);painter.LineTo(new Vector2(field.xMin,field.yMax));painter.ClosePath();painter.Fill();
                float half=field.width*.5f;var own=new Rect(local<3 ? field.xMin : field.center.x,field.yMin,half,field.height);
                painter.fillColor=new Color(.10f,.28f,.30f,.55f);painter.BeginPath();painter.MoveTo(own.min);painter.LineTo(new Vector2(own.xMax,own.yMin));painter.LineTo(own.max);painter.LineTo(new Vector2(own.xMin,own.yMax));painter.ClosePath();painter.Fill();
                painter.strokeColor=new Color(.50f,.66f,.72f);painter.lineWidth=1.5f;painter.BeginPath();painter.MoveTo(field.min);painter.LineTo(new Vector2(field.xMax,field.yMin));painter.LineTo(field.max);painter.LineTo(new Vector2(field.xMin,field.yMax));painter.ClosePath();painter.Stroke();
                painter.BeginPath();painter.MoveTo(new Vector2(field.center.x,field.yMin));painter.LineTo(new Vector2(field.center.x,field.yMax));painter.Stroke();
                painter.BeginPath();painter.Arc(field.center,draft.Rules.CenterCircleRadius/draft.Rules.FieldWidth*field.width,0,360);painter.Stroke();
                for(int slot=0;slot<=3;slot+=3)for(int i=0;i<OpeningFormation.Count;i++)
                {
                    var anchor=OpeningFormation.Position(draft.Rules,slot,i);
                    painter.strokeColor=slot/3==local/3 ? new Color(.50f,.88f,.79f) : new Color(.45f,.51f,.55f,.5f);
                    painter.lineWidth=2;painter.BeginPath();painter.Arc(Project(anchor,field),9,0,360);painter.Stroke();
                }
                foreach(var pick in draft.Picks)
                {
                    if(!pick.Locked && pick.Slot!=local)continue;
                    Vector2 point=Project(pick.Slot==local && !pick.Locked ? candidate : pick.Position,field);
                    painter.fillColor=pick.Slot==local ? draft.CanPlace(local,candidate,out _) ? new Color(1,.76f,.32f) : new Color(1,.32f,.25f) : pick.Slot<3 ? new Color(.3f,.8f,.85f) : new Color(.95f,.5f,.25f);
                    painter.BeginPath();painter.Arc(point,pick.Slot==local ? 7 : 5,0,360);painter.Fill();
                }
            }
        }
    }
}
