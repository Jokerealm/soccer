using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Soccer.Runtime
{
    internal struct SkillHelp { public string Title,Body,Meta,State; }

    /// <summary>Player 可用的技能浮层，随鼠标进入／离开显示，布局限制在窗口内。</summary>
    internal sealed class SkillTooltip
    {
        private readonly VisualElement root,panel;
        private readonly Label title,body,meta,state;
        private readonly Func<string,SkillHelp> describe;
        private VisualElement anchor;
        private string active;
        private float opacity;
        public bool Visible => active!=null;
        public SkillTooltip(VisualElement root,Func<string,SkillHelp> describe)
        {
            this.root=root; this.describe=describe;
            panel=new VisualElement {name="skill-tooltip",pickingMode=PickingMode.Ignore}; panel.AddToClassList("skill-tooltip"); panel.AddToClassList("hidden");
            title=Text("skill-tooltip-title","skill-tooltip-title"); body=Text("skill-tooltip-body","skill-tooltip-body");
            meta=Text("skill-tooltip-meta","skill-tooltip-meta"); state=Text("skill-tooltip-state","skill-tooltip-state");
            panel.Add(title); panel.Add(body); panel.Add(meta); panel.Add(state); root.Q("match-page").Add(panel);
            panel.RegisterCallback<GeometryChangedEvent>(_=>Position());
            foreach(string id in new[]{"q","e","g","kick","tackle","weapon"})
            {
                string key=id; var tile=root.Q(id+"-tile");
                tile.RegisterCallback<PointerEnterEvent>(_=>Show(key,tile));
                tile.RegisterCallback<PointerLeaveEvent>(_=> {if(active==key) Hide();});
                tile.RegisterCallback<MouseEnterEvent>(_=>Show(key,tile));
                tile.RegisterCallback<MouseLeaveEvent>(_=> {if(active==key) Hide();});
            }
        }
        private static Label Text(string name,string css)
        {var label=new Label {name=name,pickingMode=PickingMode.Ignore}; label.AddToClassList(css); return label;}
        private void Show(string id,VisualElement tile)
        {
            if(active==id && anchor==tile) return;
            if(root.Q("battle-dock").resolvedStyle.display==DisplayStyle.None) return;
            active=id; anchor=tile; opacity=0; panel.style.opacity=0;
            panel.RemoveFromClassList("hidden"); panel.BringToFront(); Refresh(); Position();
        }
        public void Refresh()
        {
            if(active==null) return;
            SkillHelp help=describe(active);
            title.text=help.Title; body.text=help.Body; meta.text=help.Meta; state.text=help.State;
            Position();
        }
        public void Hide()
        {active=null; anchor=null; opacity=0; panel.style.opacity=0; panel.AddToClassList("hidden");}
        public void Tick(float dt)
        {
            if(active==null) return;
            opacity=Mathf.MoveTowards(opacity,1,dt/.12f); panel.style.opacity=opacity;
            panel.style.translate=new Translate(0,(1-opacity)*5,0);
        }
        private void Position()
        {
            if(anchor==null || root.panel==null) return;
            float width=panel.resolvedStyle.width,height=panel.resolvedStyle.height;
            if(float.IsNaN(width) || width<=0) width=280;
            if(float.IsNaN(height) || height<=0) height=170;
            var bounds=anchor.worldBound; var page=panel.parent.worldBound;
            panel.style.left=Mathf.Clamp(bounds.center.x-page.x-width*.5f,12,Mathf.Max(12,page.width-width-12));
            panel.style.top=Mathf.Clamp(bounds.yMin-page.y-height-12,12,Mathf.Max(12,page.height-height-12));
        }
    }
}
