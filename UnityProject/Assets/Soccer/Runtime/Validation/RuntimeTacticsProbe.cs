using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Soccer.Core;
using Soccer.Core.Networking;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;

namespace Soccer.Runtime
{
    /// <summary>Opt-in rendered-player verification, with controlled input through the same sampling interface.</summary>
    public sealed class RuntimeTacticsProbe : MonoBehaviour
    {
        [Serializable] private sealed class Report {public bool passed;public string[] checks,errors;}
        private AppController app;
        private string output;
        private readonly List<string> checks=new List<string>(),errors=new List<string>();
        private void Check(bool ok,string name){if(ok)checks.Add(name);else errors.Add(name);Debug.Log((ok ? "PASS " : "FAIL ")+name);}
        private void Error(string message,string trace,LogType type){if(type==LogType.Exception || type==LogType.Error)errors.Add(message);}
        private IEnumerator Capture(string name)
        {yield return new WaitForEndOfFrame();var image=ScreenCapture.CaptureScreenshotAsTexture();File.WriteAllBytes(Path.Combine(output,name+".png"),image.EncodeToPNG());Destroy(image);}
        private IEnumerator Start()
        {
            app=GetComponent<AppController>();var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"--soccer-report-dir");output=i>=0 ? args[i+1] : Path.Combine(Application.persistentDataPath,"TacticsProbe");Directory.CreateDirectory(output);
            Application.logMessageReceived+=Error;float deadline=Time.realtimeSinceStartup+15;while(!app.StartupReady && Time.realtimeSinceStartup<deadline)yield return null;
            Check(app.StartupReady,"startup completes");Check(!app.Keys.TryAssign(GameAction.Kick,KeyCode.Alpha1,out _) && !app.Keys.TryAssign(GameAction.Queue,KeyCode.LeftControl,out _),"digits and Ctrl are reserved for observation groups");app.EnterOffline();app.OpenPlayMenu();yield return null;
            if(Array.IndexOf(args,"--soccer-local-server-smoke")>=0)
            {
                Check(app.LoginServerAddress=="http://192.168.31.29:27862","packaged deployment configuration selects this Mac's LAN service");
                using(var request=UnityWebRequest.Get(app.LoginServerAddress+"/health"))
                {request.timeout=8;yield return request.SendWebRequest();Check(request.responseCode==200 && request.downloadHandler.text.Contains("0.30.0"),"native game client reaches the running local server through its LAN address");}
            }
            var root=app.UI.Root;var drop=root.Q<DropdownField>("ai-difficulty");
            Check(drop.choices.Count==4,"four named AI difficulties appear in the real UI");
            foreach(var difficulty in new[]{BotDifficulty.BallBoy,BotDifficulty.Player,BotDifficulty.God,BotDifficulty.Cheater})
            {drop.value=BotDifficulties.Names[(int)difficulty];Check(app.SelectedBotDifficulty==difficulty,"AI difficulty control selects "+difficulty);}
            yield return Capture("01-ai-difficulty");
            app.OpenSelection(MatchMode.BotMatch);app.StartMatch();yield return new WaitForSecondsRealtime(.25f);
            Check(app.Simulation.Rules.BotDifficulty==BotDifficulty.Cheater,"selected difficulty reaches simulation");
            Check(app.Simulation.State.Players[0].Stats.MaxHealth==100 && app.Simulation.State.Players[1].Stats.MaxHealth==125,"cheating bonus affects robots and preserves human stats");
            Check(app.View.OpeningShield.Visible,"opening shield and pads render during warmup");
            int pads=0;foreach(var line in app.View.GetComponentsInChildren<LineRenderer>())if(line.name.StartsWith("Opening hexagon") && line.enabled && line.positionCount==7)pads++;
            Check(pads==6,"six initial player hexagons render as closed six-sided lines");
            yield return Capture("02-opening-hexagons");
            app.enabled=false;var tactics=app.UI.Tactics;var input=new ProbeInput();tactics.InputOverride=input;
            var camera=app.WorldCamera.GetComponent<ArenaCamera>();
            Vector2 MapPoint(int id)=>root.Q("minimap-player-"+id).worldBound.center;
            void Point(Vector2 point){input.MousePosition=new Vector3(point.x/root.resolvedStyle.width*Screen.width,Screen.height-point.y/root.resolvedStyle.height*Screen.height,0);}
            Point(MapPoint(1));input.Left=true;tactics.Tick();input.Left=false;
            Check(camera.ObservedPlayerId==1 && app.LocalPlayerId==0,"minimap click observes teammate without changing player control");
            input.HeldKeys.Add(KeyCode.LeftControl);input.DownKeys.Add(KeyCode.Alpha1);tactics.Tick();input.DownKeys.Clear();input.HeldKeys.Clear();
            Check(tactics.Groups.Target(1)==1,"Ctrl+1 binds the teammate under the minimap cursor");
            Point(MapPoint(4));input.HeldKeys.Add(KeyCode.LeftControl);input.DownKeys.Add(KeyCode.Alpha2);tactics.Tick();input.DownKeys.Clear();input.HeldKeys.Clear();
            Check(tactics.Groups.Target(2)==4,"Ctrl+2 also binds an enemy");
            input.DownKeys.Add(KeyCode.Alpha2);tactics.Tick();input.DownKeys.Clear();
            Check(camera.ObservedPlayerId==1,"single number selects group without moving view");
            input.DownKeys.Add(KeyCode.Alpha2);tactics.Tick();input.DownKeys.Clear();yield return null;
            Check(camera.ObservedPlayerId==4 && app.LocalPlayerId==0,"double number follows enemy without transferring control");
            Check(app.UI.MiniMap.CameraBounds.width>0,"minimap camera rectangle stays valid while observing enemy");
            yield return Capture("03-observation-groups");
            input.DownKeys.Add(app.Keys.Key(GameAction.CameraSelf));tactics.Tick();input.DownKeys.Clear();yield return null;
            Check(camera.ObservedPlayerId==-1,"camera return key follows local player");
            camera.InputOverride=input;Point(new Vector2(root.resolvedStyle.width*.5f,root.resolvedStyle.height*.42f));
            float originalSize=app.WorldCamera.orthographicSize;input.ScrollDelta=3;yield return new WaitForEndOfFrame();input.ScrollDelta=0;yield return new WaitForSecondsRealtime(.4f);
            float userZoom=camera.Zoom;Check(userZoom<1 && app.WorldCamera.orthographicSize<originalSize,"ordinary wheel zooms into the actual match camera");
            input.HeldKeys.Add(KeyCode.LeftControl);input.ScrollDelta=2;yield return new WaitForEndOfFrame();input.ScrollDelta=0;input.HeldKeys.Clear();
            Check(Mathf.Abs(camera.OrbitYaw-24)<.01f && camera.Zoom==userZoom,"Ctrl wheel rotates without changing zoom");
            input.HeldKeys.Add(KeyCode.RightControl);input.ScrollDelta=1;yield return new WaitForEndOfFrame();input.ScrollDelta=0;input.HeldKeys.Clear();
            Check(Mathf.Abs(camera.OrbitYaw-36)<.01f,"right Ctrl also rotates the match camera");
            Check(Quaternion.Angle(app.View.PlayerViews[0].ResourcesUI.transform.rotation,app.WorldCamera.transform.rotation)<.1f,"world health and energy bars face the rotated camera");
            camera.ObservePlayer(4);camera.ResetFraming();yield return null;
            Check(camera.OrbitYaw==36 && camera.Zoom==userZoom,"following and same-match reframing preserve user orbit and zoom");
            yield return Capture("08-orbit-zoom");
            app.SetPaused(true);input.ScrollDelta=4;yield return new WaitForEndOfFrame();input.ScrollDelta=0;
            Check(camera.Zoom==userZoom && camera.OrbitYaw==36,"paused camera ignores wheel input");app.SetPaused(false);tactics.Tick();
            Point(root.Q("minimap").worldBound.center);input.ScrollDelta=2;yield return new WaitForEndOfFrame();input.ScrollDelta=0;
            Check(camera.Zoom==userZoom,"wheel over the minimap does not change world zoom");
            app.UI.Chat.Open("party");yield return null;Point(new Vector2(root.resolvedStyle.width*.5f,root.resolvedStyle.height*.42f));input.ScrollDelta=2;yield return new WaitForEndOfFrame();input.ScrollDelta=0;
            Check(camera.Zoom==userZoom,"chat focus suppresses world-camera scrolling");app.UI.Chat.Close();
            input.ScrollDelta=-20;yield return new WaitForEndOfFrame();input.ScrollDelta=0;yield return new WaitForSecondsRealtime(.5f);
            Check(camera.Zoom==1.7f,"zoom out has a bounded overview distance");
            input.HeldKeys.Add(KeyCode.LeftControl);input.ScrollDelta=20;yield return new WaitForEndOfFrame();input.ScrollDelta=0;input.HeldKeys.Clear();yield return null;
            var visible=camera.VisibleGroundSize*.5f;var focus=camera.FocusGround;
            Check(Mathf.Abs(focus.x)+visible.x<=app.Simulation.Rules.FieldWidth/2+3.01f && Mathf.Abs(focus.y)+visible.y<=app.Simulation.Rules.FieldHeight/2+3.01f,"rotated maximum zoom stays within the extended pitch");
            yield return Capture("09-rotated-overview");
            input.ScrollDelta=20;yield return new WaitForEndOfFrame();input.ScrollDelta=0;Check(camera.Zoom==.6f,"zoom in has a bounded close distance");
            Point(MapPoint(0));input.HeldKeys.Add(app.Keys.Key(GameAction.SignalWheel));tactics.Tick();
            Check(tactics.WheelOpen && tactics.BlocksGameplay,"held Alt opens tray and blocks gameplay input");
            input.ScrollDelta=-1;yield return new WaitForEndOfFrame();input.ScrollDelta=0;Check(camera.Zoom==.6f,"signal gesture suppresses camera scrolling");
            var origin=root.Q("signal-wheel").worldBound.center;Point(origin+new Vector2(0,-100));tactics.Tick();
            Check(tactics.Selection==0,"upward mouse direction selects Attack");yield return Capture("04-alt-tray");
            input.HeldKeys.Clear();tactics.Tick();yield return null;
            Check(!tactics.WheelOpen && tactics.Signals.Count==1 && tactics.BlocksGameplay,"Alt release sends once and consumes release frame");
            yield return new WaitForEndOfFrame();Check(root.Q("minimap-signal-0")?.resolvedStyle.display==DisplayStyle.Flex,"team signal is visible on minimap");
            Check(!tactics.Send(SignalKind.Shoot,Vec2.Zero),"local signal cooldown prevents flooding");
            yield return Capture("05-team-signal");
            Point(new Vector2(root.resolvedStyle.width/2,root.resolvedStyle.height/2));input.HeldKeys.Add(app.Keys.Key(GameAction.SignalWheel));tactics.Tick();input.HeldKeys.Clear();tactics.Tick();
            Check(tactics.Signals.Count==1,"release inside tray dead zone cancels without sending");
            yield return new WaitForSecondsRealtime(1.05f);Point(MapPoint(0));input.HeldKeys.Add(KeyCode.Mouse2);tactics.Tick();
            Check(tactics.WheelOpen && tactics.BlocksGameplay,"middle mouse hold opens the same signal tray");
            origin=root.Q("signal-wheel").worldBound.center;Point(origin+new Vector2(0,-100));tactics.Tick();
            input.HeldKeys.Add(app.Keys.Key(GameAction.SignalWheel));input.HeldKeys.Remove(KeyCode.Mouse2);tactics.Tick();
            Check(tactics.WheelOpen && tactics.Signals.Count==1,"overlapping middle and Alt holds keep one tray until both release");
            input.HeldKeys.Clear();tactics.Tick();Check(!tactics.WheelOpen && tactics.Signals.Count==2,"middle mouse gesture sends exactly one team signal");
            input.HeldKeys.Add(KeyCode.Mouse2);tactics.Tick();input.DownKeys.Add(KeyCode.Escape);tactics.Tick();input.DownKeys.Clear();tactics.Tick();
            Check(!tactics.WheelOpen && tactics.Signals.Count==2,"Escape cancels middle-mouse ping until the button is released");input.HeldKeys.Clear();tactics.Tick();
            yield return new WaitForSecondsRealtime(1.05f);Check(tactics.Send(SignalKind.PassToMe,Vec2.Zero),"pass-to-me signal sends after cooldown");
            Check(tactics.Signals[tactics.Signals.Count-1].Signal.TargetId==app.LocalPlayerId,"pass request tracks the sender");yield return null;
            yield return Capture("06-pass-request");
            foreach(var size in new[]{new Vector2Int(1024,768),new Vector2Int(1280,720),new Vector2Int(1920,1080)})
            {
                Screen.SetResolution(size.x,size.y,false);yield return new WaitForSecondsRealtime(.35f);
                Point(new Vector2(8,root.resolvedStyle.height-8));input.HeldKeys.Add(app.Keys.Key(GameAction.SignalWheel));tactics.Tick();yield return null;
                var bounds=root.Q("signal-wheel").worldBound;Check(root.worldBound.Contains(bounds.min) && root.worldBound.Contains(bounds.max),size+" tray stays inside viewport near edge");
                yield return Capture("tray-"+size.x+"x"+size.y);input.HeldKeys.Clear();tactics.CancelWheel();
            }
            yield return new WaitForSecondsRealtime(4.6f);tactics.Render();Check(tactics.Signals.Count==0,"expired signals leave both maps");
            app.OpenSelection(MatchMode.BotMatch);app.StartMatch();tactics.Tick();
            Check(tactics.Groups.Target(1)==-1 && tactics.Signals.Count==0 && camera.ObservedPlayerId==-1,"new match resets groups, signals and observation");
            yield return null;Check(camera.Zoom==1 && camera.OrbitYaw==0,"new match restores the default camera view");camera.InputOverride=null;
            tactics.InputOverride=null;
            app.GoHome();app.OpenMode(GamePlaylist.Normal);app.SelectConnection(ConnectionRoute.LAN);app.OpenNetwork(true);
            app.CreateNetworkRoom(new RoomOptions {PlayerName="验证房主",Name="战术信号验证"});
            using(var guest=NetworkRoom.Join("127.0.0.1:"+app.Room.Port,new RoomOptions {Route=NetworkRoute.LAN,PlayerName="验证队友",Team=Team.Cyan}))
            {
                deadline=Time.realtimeSinceStartup+8;
                while(!guest.Connected && Time.realtimeSinceStartup<deadline){app.Room.Pump(Time.realtimeSinceStartup);guest.Pump(Time.realtimeSinceStartup);yield return null;}
                Check(guest.Connected,"native player accepts a real TCP teammate");
                app.Room.FillBots();root.Q<DropdownField>("lobby-ai-difficulty").value=BotDifficulties.Names[3];
                Check(app.Room.Lobby.BotDifficulty==BotDifficulty.Cheater,"host lobby difficulty widget configures real room");
                app.Room.SetReady(true);guest.SetReady(true);
                deadline=Time.realtimeSinceStartup+8;while(!app.Room.CanStart && Time.realtimeSinceStartup<deadline){app.Room.Pump(Time.realtimeSinceStartup);guest.Pump(Time.realtimeSinceStartup);yield return null;}
                app.Room.StartMatch(new MatchRules());deadline=Time.realtimeSinceStartup+8;
                while(!guest.Running && Time.realtimeSinceStartup<deadline){app.Room.Pump(Time.realtimeSinceStartup);guest.Pump(Time.realtimeSinceStartup);yield return null;}
                Check(app.Simulation.State.Players[2].Health==125 && app.Simulation.State.Players[1].Health==100,"native network match boosts bots while preserving teammate stats");
                guest.SendSignal(SignalKind.Attention,new Vec2(-6,1));app.Room.Pump(Time.realtimeSinceStartup);guest.Pump(Time.realtimeSinceStartup);
                deadline=Time.realtimeSinceStartup+5;while(tactics.Signals.Count==0 && Time.realtimeSinceStartup<deadline){app.Room.Pump(Time.realtimeSinceStartup);guest.Pump(Time.realtimeSinceStartup);yield return null;}
                Check(tactics.Signals.Count==1 && tactics.Signals[0].Signal.PlayerId==1,"real TCP teammate signal reaches application and tactical UI");
                int received=0;guest.SignalReceived+=signal=>{if(signal.PlayerId==app.LocalPlayerId && signal.Kind==SignalKind.PassToMe)received++;};
                Check(tactics.Send(SignalKind.PassToMe,Vec2.Zero),"native tactical tray sends through network room");
                deadline=Time.realtimeSinceStartup+5;while(received==0 && Time.realtimeSinceStartup<deadline){app.Room.Pump(Time.realtimeSinceStartup);guest.Pump(Time.realtimeSinceStartup);yield return null;}
                Check(received==1,"real TCP teammate receives local tactical UI signal");yield return Capture("07-network-team-signal");
            }
            app.enabled=true;app.GoHome();app.OpenPlayMenu();
            foreach(var size in new[]{new Vector2Int(1024,768),new Vector2Int(1280,720)})
            {
                Screen.SetResolution(size.x,size.y,false);yield return new WaitForSecondsRealtime(.35f);var bounds=root.Q("ai-difficulty").worldBound;
                Check(root.worldBound.Contains(bounds.min) && root.worldBound.Contains(bounds.max),size+" difficulty control fits viewport");yield return Capture("difficulty-"+size.x+"x"+size.y);
            }
            Application.logMessageReceived-=Error;File.WriteAllText(Path.Combine(output,"tactics-smoke.json"),JsonUtility.ToJson(new Report{passed=errors.Count==0,checks=checks.ToArray(),errors=errors.ToArray()},true));Application.Quit(errors.Count==0 ? 0 : 1);
        }
        private sealed class ProbeInput : IPlayerInputState
        {
            public HashSet<KeyCode> HeldKeys=new HashSet<KeyCode>(),DownKeys=new HashSet<KeyCode>();public bool Left;
            public Vector3 MousePosition {get;set;}
            public float ScrollDelta {get;set;}
            public bool Down(KeyCode key)=>DownKeys.Contains(key);public bool Held(KeyCode key)=>HeldKeys.Contains(key);public bool Up(KeyCode key)=>false;public bool MouseDown(int button)=>button==0 && Left;
        }
    }
}
