using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Soccer.Runtime
{
    public sealed partial class GameUI
    {
        private readonly string[] playButtons={"play-online","play-ai","play-tutorial","play-training"};
        private int navigationFrame=-1,navigationDirection,submitFrame=-1;
        internal IPlayerInputState MenuInputOverride;
        private readonly System.Collections.Generic.HashSet<NavigationSubmitEvent> forwardedSubmits=new System.Collections.Generic.HashSet<NavigationSubmitEvent>();
        private string[] NavigationButtons => app.Page==AppPage.Home ? homeButtons : app.Page==AppPage.PlayMenu ? playButtons : null;
        private void BindMenuNavigation()
        {
            root.RegisterCallback<KeyDownEvent>(evt=>
            {
                if(evt.keyCode==KeyCode.UpArrow || evt.keyCode==KeyCode.DownArrow)
                {if(MoveMenu(evt.keyCode==KeyCode.UpArrow ? -1 : 1)){evt.StopImmediatePropagation();evt.PreventDefault();}}
                else if(evt.keyCode==KeyCode.Return || evt.keyCode==KeyCode.KeypadEnter)
                {if(SubmitMenu()){evt.StopImmediatePropagation();evt.PreventDefault();}}
            },TrickleDown.TrickleDown);
            root.RegisterCallback<NavigationMoveEvent>(evt=>
            {
                int direction=evt.direction==NavigationMoveEvent.Direction.Up ? -1 : evt.direction==NavigationMoveEvent.Direction.Down ? 1 : 0;
                if(direction!=0 && MoveMenu(direction)){evt.StopImmediatePropagation();evt.PreventDefault();}
            },TrickleDown.TrickleDown);
            root.RegisterCallback<NavigationSubmitEvent>(evt=>
            {
                if(forwardedSubmits.Remove(evt))return;
                var button=evt.target as Button;
                if(button!=null && submitFrame==Time.frameCount && (Array.IndexOf(homeButtons,button.name)>=0 || Array.IndexOf(playButtons,button.name)>=0))
                {evt.StopImmediatePropagation();evt.PreventDefault();return;}
                var names=NavigationButtons;if(names==null)return;
                if(button!=null && Array.IndexOf(names,button.name)>=0)
                {
                    if(submitFrame==Time.frameCount){evt.StopImmediatePropagation();evt.PreventDefault();}
                    else submitFrame=Time.frameCount;
                }
                else if(SubmitMenu()){evt.StopImmediatePropagation();evt.PreventDefault();}
            },TrickleDown.TrickleDown);
            foreach(string name in homeButtons)
                root.Q<Button>(name).RegisterCallback<FocusInEvent>(_=>SelectHome(name));
        }
        private void SelectHome(string name)
        {foreach(string id in homeButtons)root.Q<Button>(id).EnableInClassList("home-selected",id==name);}
        private bool MoveMenu(int direction)
        {
            var names=NavigationButtons;if(names==null)return false;
            if(navigationFrame==Time.frameCount && navigationDirection==direction)return true;
            navigationFrame=Time.frameCount;navigationDirection=direction;
            var focused=root.panel.focusController.focusedElement as VisualElement;int index=focused==null ? -1 : Array.IndexOf(names,focused.name);
            index=index<0 ? (direction>0 ? 0 : names.Length-1) : (index+direction+names.Length)%names.Length;
            root.Q<Button>(names[index]).Focus();return true;
        }
        private bool SubmitMenu(VisualElement target=null)
        {
            var names=NavigationButtons;if(names==null)return false;
            if(submitFrame==Time.frameCount)return true;
            var button=target as Button ?? root.panel.focusController.focusedElement as Button;
            if(button==null || Array.IndexOf(names,button.name)<0)return false;
            submitFrame=Time.frameCount;
            using(var evt=NavigationSubmitEvent.GetPooled())
            {forwardedSubmits.Add(evt);evt.target=button;button.SendEvent(evt);}
            return true;
        }
        private void Update()
        {
            if(NavigationButtons==null || MenuInputOverride==null && !Application.isFocused)return;
            var input=MenuInputOverride ?? UnityPlayerInputState.Instance;
            if(input.Down(KeyCode.UpArrow))MoveMenu(-1);else if(input.Down(KeyCode.DownArrow))MoveMenu(1);
            if(input.Down(KeyCode.Return) || input.Down(KeyCode.KeypadEnter))SubmitMenu();
        }
    }
}
