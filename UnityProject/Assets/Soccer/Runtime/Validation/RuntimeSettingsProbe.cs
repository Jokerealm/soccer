using System;
using System.Collections;
using System.Collections.Generic;
using Soccer.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Soccer.Runtime
{
    public sealed partial class RuntimeSmokeProbe
    {
        private sealed class ProbeInput : IPlayerInputState
        {
            public KeyCode Pressed, HeldKey, Released; public bool Right;
            public Vector3 Position;
            public bool Down(KeyCode key) => key == Pressed;
            public bool Held(KeyCode key) => key == HeldKey;
            public bool Up(KeyCode key) => key == Released;
            public bool MouseDown(int button) => button == 1 && Right;
            public Vector3 MousePosition => Position;
            public void Clear() { Pressed = HeldKey = Released = KeyCode.None; Right = false; }
        }
        private void BindingKey(KeyCode key)
        { using (var evt = KeyDownEvent.GetPooled('\0',key,EventModifiers.None)) app.UI.Root.SendEvent(evt); }
        private IEnumerator CaptureBindingsAndModels()
        {
            var saved = new Dictionary<string,int?>();
            foreach (var definition in GameKeyBindings.Definitions)
            { string key = "soccer.keybinding." + definition.Id; saved[key] = PlayerPrefs.HasKey(key) ? (int?)PlayerPrefs.GetInt(key) : null; }
            app.Keys.RestoreDefaults();
            try
            {
                app.OpenSettings(); yield return new WaitForSecondsRealtime(.35f);
                yield return Click("settings-keys-tab"); yield return new WaitForSecondsRealtime(.35f);
                Check(PicksButton("binding-kick") && PicksButton("keys-reset") && PicksButton("settings-back"),"bindings tab exposes editable actions, reset and back");
                yield return Click("binding-kick"); BindingKey(KeyCode.J); yield return null;
                Check(app.Keys.Key(GameAction.Kick)==KeyCode.J && !app.UI.KeyBindings.Capturing && app.UI.Root.Q<Button>("binding-kick").text=="J", "pressing a new key changes binding and exits capture");
                Check(new GameKeyBindings().Key(GameAction.Kick)==KeyCode.J,"new binding survives loading preferences again");
                yield return Click("binding-push"); BindingKey(KeyCode.J); yield return null;
                Check(app.Keys.Key(GameAction.Push)==KeyCode.Space && app.UI.KeyBindings.Capturing && app.UI.Root.Q<Label>("keys-status").text.Contains("已用于"),"duplicate binding is rejected without replacing an existing action");
                BindingKey(KeyCode.Escape); yield return null;
                Check(!app.UI.KeyBindings.Capturing && app.Page==AppPage.Settings,"Escape cancels capture while staying on settings");
                yield return Click("binding-push"); BindingKey(KeyCode.Tab); yield return null;
                Check(app.UI.KeyBindings.Capturing && app.Keys.Key(GameAction.Push)==KeyCode.Space,"reserved UI keys cannot replace gameplay actions");
                BindingKey(KeyCode.K); yield return null;
                Check(app.Keys.Key(GameAction.Push)==KeyCode.K,"capture remains usable after a rejected key");
                yield return Capture("keys-custom-bindings");
                yield return Click("settings-back");
                app.OpenSelection(MatchMode.Training); yield return new WaitForSecondsRealtime(.4f);
                Check(app.UI.Root.Q<Label>("selected-stats").text.Contains("J 键") && app.UI.Root.Q<Label>("selected-stats").text.Contains("K"), "character action hints follow custom kick and push keys");
                app.StartMatch(); yield return new WaitForSecondsRealtime(3.4f);
                Check(!app.UI.CharacterPreview.Visible && Camera.allCamerasCount==1,"gameplay disables the model-preview camera");
                Check(app.UI.Root.Q("kick-tile").Q<Label>(className:"ability-key").text=="J" && app.UI.Root.Q("tackle-tile").Q<Label>(className:"ability-key").text=="K", "actual match HUD displays configured bindings");
                app.SetPaused(true); app.ResetTraining(true);
                var input = new ProbeInput { Position=app.WorldCamera.WorldToScreenPoint(ArenaProjection.ToWorld(new Vec2(3,1))) };
                var sampler = new KeyboardMouseInput(app.WorldCamera,app.Keys,input); var state=app.Simulation.State;
                input.Pressed=input.HeldKey=KeyCode.J; sampler.Sample(state,app.Simulation.Rules,false); var command=sampler.ReadCommand(state,0);
                Check(command.KickPressed && command.KickHeld,"remapped kick key starts and holds the real command sampler gesture");
                input.Clear();input.Released=KeyCode.J; sampler.Sample(state,app.Simulation.Rules,false); command=sampler.ReadCommand(state,0);
                Check(command.KickReleased && !command.KickHeld,"remapped kick release completes the gesture");
                input.Clear();input.Pressed=input.HeldKey=KeyCode.V; sampler.Reset();sampler.Sample(state,app.Simulation.Rules,false);command=sampler.ReadCommand(state,0);
                Check(!command.KickPressed && !command.KickHeld,"old kick key no longer fires the command");
                input.Clear();input.Pressed=KeyCode.K;sampler.Sample(state,app.Simulation.Rules,false);command=sampler.ReadCommand(state,0);
                Check(command.TacklePressed,"remapped push key reaches the gameplay command");
                foreach (var pair in new[] { (GameAction.SkillQ,KeyCode.U), (GameAction.SkillE,KeyCode.I), (GameAction.Special,KeyCode.O), (GameAction.Cancel,KeyCode.L), (GameAction.Stop,KeyCode.N), (GameAction.Queue,KeyCode.LeftControl) })
                    Check(app.Keys.TryAssign(pair.Item1,pair.Item2,out _),"configure sampler action: "+pair.Item1);
                input.Clear();input.Pressed=KeyCode.U;sampler.Sample(state,app.Simulation.Rules,false);command=sampler.ReadCommand(state,0);Check(command.QPressed,"remapped skill 1 command");
                input.Clear();input.Pressed=KeyCode.I;sampler.Sample(state,app.Simulation.Rules,false);command=sampler.ReadCommand(state,0);Check(command.EPressed,"remapped skill 2 command");
                input.Clear();input.Pressed=KeyCode.O;sampler.Sample(state,app.Simulation.Rules,false);command=sampler.ReadCommand(state,0);Check(command.SpecialPressed,"remapped detonation command");
                input.Clear();input.Pressed=KeyCode.L;sampler.Sample(state,app.Simulation.Rules,false);command=sampler.ReadCommand(state,0);Check(command.Cancel,"remapped cancel command");
                input.Clear();input.Pressed=KeyCode.N;sampler.Sample(state,app.Simulation.Rules,false);command=sampler.ReadCommand(state,0);Check(command.StopPressed,"remapped stop command");
                input.Clear();input.HeldKey=KeyCode.RightControl;input.Right=true;sampler.Sample(state,app.Simulation.Rules,false);command=sampler.ReadCommand(state,0);Check(command.OrderPressed && command.QueueOrder,"right modifier also queues the actual contextual order after Ctrl binding");
                app.GoHome(); yield return Click("settings-button");yield return Click("settings-keys-tab");yield return Click("keys-reset");
                Check(app.Keys.Key(GameAction.Kick)==KeyCode.V && new GameKeyBindings().Key(GameAction.Push)==KeyCode.Space,"restore-default button restores and persists all action bindings");
                foreach (var size in new[] { new Vector2Int(1024,768),new Vector2Int(1280,720) })
                {
                    Screen.SetResolution(size.x,size.y,false);yield return new WaitForSecondsRealtime(.55f);
                    app.UI.Root.Q<ScrollView>("keys-scroll").scrollOffset=Vector2.zero;yield return null;
                    Check(PicksButton("binding-kick") && PicksButton("keys-reset") && PicksButton("settings-back"),size+" key settings fit and receive pointer");
                    app.UI.Root.Q<ScrollView>("keys-scroll").ScrollTo(app.UI.Root.Q("binding-free-kick"));yield return null;
                    Check(PicksButton("binding-free-kick"),size+" final training binding is reachable by scrolling");
                    yield return Capture("keys-settings-"+size.x+"x"+size.y);
                    app.OpenSelection(MatchMode.Training);yield return new WaitForSecondsRealtime(.4f);
                    var bounds=app.UI.Root.Q("character-preview").worldBound;
                    Check(bounds.width>180 && bounds.height>180 && app.UI.Root.worldBound.Contains(bounds.min) && app.UI.Root.worldBound.Contains(bounds.max) && PicksButton("preview-auto") && PicksButton("start-button"),size+" model viewer and selection actions fit the window");
                    foreach (int index in new[] {0,1})
                    {
                        yield return Click("character-"+index);yield return new WaitForSecondsRealtime(.4f);
                        var viewer=app.UI.CharacterPreview;
                        Check(viewer.Visible && viewer.Character==app.Selected && viewer.Texture!=null && viewer.Texture.IsCreated() && app.UI.Root.Q("character-preview").resolvedStyle.backgroundImage.renderTexture==viewer.Texture,"selected character "+index+" uses a live render texture in the selection panel");
                        yield return Capture("model-"+index+"-"+size.x+"x"+size.y);
                    }
                    app.GoHome();app.OpenSettings();yield return Click("settings-keys-tab");
                }
                app.OpenSelection(MatchMode.Training);app.SelectCharacter(CharacterId.Marine);yield return new WaitForSecondsRealtime(.4f);
                var preview=app.UI.CharacterPreview; if(!preview.AutoRotate) yield return Click("preview-auto");
                float initialYaw=preview.OrbitYaw;yield return new WaitForSecondsRealtime(.4f);Check(Mathf.Abs(preview.OrbitYaw-initialYaw)>2,"automatic rotation advances the real model camera");
                var element=app.UI.Root.Q("character-preview");var center=element.worldBound.center;float before=preview.OrbitYaw;
                using(var evt=PointerDownEvent.GetPooled(new Event{type=EventType.MouseDown,button=0,mousePosition=center}))element.SendEvent(evt);
                using(var evt=PointerMoveEvent.GetPooled(new Event{type=EventType.MouseDrag,button=0,mousePosition=center+new Vector2(140,24)}))element.SendEvent(evt);
                using(var evt=PointerUpEvent.GetPooled(new Event{type=EventType.MouseUp,button=0,mousePosition=center+new Vector2(140,24)}))element.SendEvent(evt);
                Check(!preview.AutoRotate && Mathf.Abs(before-preview.OrbitYaw)>40,"dragging inside the real viewport orbits and pauses auto rotation");
                float beforeDistance=preview.Distance;
                using(var evt=WheelEvent.GetPooled(new Event{type=EventType.ScrollWheel,delta=new Vector2(0,3),mousePosition=center}))element.SendEvent(evt);
                Check(preview.Distance>beforeDistance,"mouse wheel changes the preview camera distance");
                yield return Click("preview-reset");Check(Mathf.Abs(preview.OrbitYaw-12)<.1f && Mathf.Abs(preview.Distance-4.4f)<.1f,"reset restores framing after orbit and zoom");
                preview.Orbit(new Vector2(-400,0));yield return new WaitForSecondsRealtime(.3f);yield return Capture("model-marine-back");
                yield return Click("preview-reset");yield return Click("preview-auto");
                Screen.SetResolution(1920,1080,false);yield return new WaitForSecondsRealtime(.5f);
                yield return Capture("model-marine-1920x1080");
                app.SelectCharacter(CharacterId.Thor);yield return new WaitForSecondsRealtime(.4f);yield return Capture("model-thor-1920x1080");
                app.GoHome();yield return new WaitForSecondsRealtime(.35f);Check(!preview.Visible && Camera.allCamerasCount==1,"leaving selection releases viewport capture and disables preview rendering");
            }
            finally
            {
                // Restore the user's existing preferences rather than leaving probe key changes behind.
                foreach(var entry in saved) { if(entry.Value.HasValue) PlayerPrefs.SetInt(entry.Key,entry.Value.Value);else PlayerPrefs.DeleteKey(entry.Key); }
                PlayerPrefs.Save();app.Keys.ReloadSaved();
            }
        }
    }
}
