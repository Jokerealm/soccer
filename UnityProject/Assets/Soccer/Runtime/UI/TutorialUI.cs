using UnityEngine;
using UnityEngine.UIElements;
using Soccer.Core;

namespace Soccer.Runtime
{
    public sealed class TutorialUI
    {
        private readonly AppController app;private readonly VisualElement root,panel,marker;
        public TutorialUI(AppController controller,VisualElement ui) {app=controller;root=ui;panel=root.Q("tutorial-panel");marker=root.Q("tutorial-marker");}
        private void Text(string name,string text) => root.Q<Label>(name).text=app.Keys.FormatHint(text);
        public void Refresh()
        {
            var session=app.Tutorial;bool active=session!=null && app.Page==AppPage.Match;
            panel.EnableInClassList("hidden",!active);if(!active){marker.AddToClassList("hidden");return;}
            Text("tutorial-progress","新手教程  "+(session.Index+1)+" / "+TutorialSession.Steps.Length);
            Text("tutorial-title",session.Step.Title);Text("tutorial-body",session.Step.Body);Text("tutorial-objective",session.Step.Objective);
            Text("tutorial-status",session.Complete ? "已完成，可以继续" : "完成当前操作，或跳过稍后回看");
            root.Q<Button>("tutorial-next").text=session.Index==TutorialSession.Steps.Length-1 ? "完成教程" : "下一步";
            root.Q<Button>("tutorial-next").SetEnabled(session.Complete);root.Q<Button>("tutorial-previous").SetEnabled(session.Index>0);
            bool visible=session.Step.Lesson==TrainingLesson.Move;marker.EnableInClassList("hidden",!visible);
            if(visible)
            {
                Vector3 screen=app.WorldCamera.WorldToScreenPoint(ArenaProjection.ToWorld(session.Target));
                Vector2 point=RuntimePanelUtils.ScreenToPanel(root.panel,new Vector2(screen.x,Screen.height-screen.y));
                marker.style.left=point.x-22;marker.style.top=point.y-22;
            }
        }
    }
}
