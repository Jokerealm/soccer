using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Soccer.Runtime
{
    public sealed partial class GameUI
    {
        private readonly string[] playButtons={"play-online","play-ai","play-tutorial","play-training"};
        private int navigationFrame=-1,navigationDirection;
        internal IPlayerInputState MenuInputOverride;
        private string[] NavigationButtons => app.Page==AppPage.Home ? homeButtons : app.Page==AppPage.PlayMenu ? playButtons : null;
        private void BindMenuNavigation()
        {
            root.RegisterCallback<KeyDownEvent>(evt=>
            {
                if(evt.keyCode==KeyCode.UpArrow || evt.keyCode==KeyCode.DownArrow)
                {if(MoveMenu(evt.keyCode==KeyCode.UpArrow ? -1 : 1)){evt.StopImmediatePropagation();evt.PreventDefault();}}
                else if(evt.keyCode==KeyCode.Return || evt.keyCode==KeyCode.KeypadEnter)
                {if(Chat.HandleEnter(evt.shiftKey)){evt.StopImmediatePropagation();evt.PreventDefault();}}
            },TrickleDown.TrickleDown);
            root.RegisterCallback<NavigationMoveEvent>(evt=>
            {
                int direction=evt.direction==NavigationMoveEvent.Direction.Up ? -1 : evt.direction==NavigationMoveEvent.Direction.Down ? 1 : 0;
                if(direction!=0 && MoveMenu(direction)){evt.StopImmediatePropagation();evt.PreventDefault();}
            },TrickleDown.TrickleDown);
            root.RegisterCallback<NavigationSubmitEvent>(evt=>
            {
                // Enter belongs to chat. Pointer clicks and Space can still activate menu buttons.
                if(Chat.IsOpen && evt.target is Button button &&
                    (Array.IndexOf(homeButtons,button.name)>=0 || Array.IndexOf(playButtons,button.name)>=0))
                {evt.StopImmediatePropagation();evt.PreventDefault();}
            },TrickleDown.TrickleDown);
            foreach(string name in homeButtons)
                root.Q<Button>(name).RegisterCallback<FocusInEvent>(_=>SelectHome(name));
        }
        private void SelectHome(string name)
        {foreach(string id in homeButtons)root.Q<Button>(id).EnableInClassList("home-selected",id==name);}
        private bool MoveMenu(int direction)
        {
            var names=NavigationButtons;if(names==null || app.UI.BlocksKeyNavigation)return false;
            if(navigationFrame==Time.frameCount && navigationDirection==direction)return true;
            navigationFrame=Time.frameCount;navigationDirection=direction;
            var focused=root.panel.focusController.focusedElement as VisualElement;int index=focused==null ? -1 : Array.IndexOf(names,focused.name);
            index=index<0 ? (direction>0 ? 0 : names.Length-1) : (index+direction+names.Length)%names.Length;
            root.Q<Button>(names[index]).Focus();return true;
        }
        private void Update()
        {
            if(NavigationButtons==null || MenuInputOverride==null && !Application.isFocused)return;
            var input=MenuInputOverride ?? UnityPlayerInputState.Instance;
            if(input.Down(KeyCode.Return) || input.Down(KeyCode.KeypadEnter))Chat.HandleEnter(false);
            if(app.UI.BlocksKeyNavigation)return;
            if(input.Down(KeyCode.UpArrow))MoveMenu(-1);else if(input.Down(KeyCode.DownArrow))MoveMenu(1);
        }
    }
}
