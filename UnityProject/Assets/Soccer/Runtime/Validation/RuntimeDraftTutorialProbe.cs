using System;
using System.Collections;
using Soccer.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Soccer.Runtime
{
    public sealed partial class RuntimeSmokeProbe
    {
        private void DraftMapClick(Vec2 position)
        {
            var pitch=app.UI.Root.Q("draft-pitch");var rules=app.ActiveDraft.Rules;var bounds=pitch.contentRect;
            float scale=Mathf.Min((bounds.width-24)/rules.FieldWidth,(bounds.height-20)/rules.FieldHeight);
            Vector2 local=bounds.center+new Vector2(position.X*scale,-position.Y*scale);
            Vector2 point=pitch.LocalToWorld(local);
            using(var evt=PointerDownEvent.GetPooled(new Event {type=EventType.MouseDown,button=0,mousePosition=point}))
            {evt.target=pitch;pitch.SendEvent(evt);}
        }
        private void LessonTick(PlayerCommand command,int ticks=1)
        {
            for(int i=0;i<ticks;i++)
            {
                var commands=new PlayerCommand[app.Simulation.State.Players.Count];commands[0]=command;
                app.Tutorial.ObserveCommand(command,app.Simulation.State);app.Simulation.Step(commands);
                foreach(var value in app.Simulation.State.Events)app.HandleEvent(value);
                app.Tutorial.Tick(app.Simulation.State,MatchSimulation.TickSeconds);
                command=command.WithoutEdges();
            }
            app.View.Render(app.Simulation.State,app.Simulation.Rules,1,false,false);app.UI.RefreshMatch();
        }
        private PlayerCommand LessonSample(KeyboardMouseInput sampler,ProbeInput input)
        {sampler.Sample(app.Simulation.State,app.Simulation.Rules,false);return sampler.ReadCommand(app.Simulation.State,0);}
        private IEnumerator CaptureDraftAndTutorial()
        {
            int width=Screen.width,height=Screen.height;var screenMode=Screen.fullScreenMode;
            app.GoHome();yield return new WaitForSecondsRealtime(.35f);yield return Click("bots-button");
            Check(app.Page==AppPage.PlayMenu && app.Simulation==null,"start game opens mode selection without choosing a character or creating simulation");
            foreach(var size in new[]{new Vector2Int(1024,768),new Vector2Int(1280,720),new Vector2Int(1920,1080)})
            {
                Screen.SetResolution(size.x,size.y,false);yield return new WaitForSecondsRealtime(.5f);
                foreach(string name in new[]{"play-online","play-ai","play-tutorial","play-training","play-back"})
                    Check(PicksButton(name),size+" mode action receives pointer: "+name);
                yield return Capture("modes-"+size.x+"x"+size.y);
            }
            yield return Click("play-online");Check(app.Page==AppPage.NetworkMenu && app.Simulation==null,"normal mode connects players before selecting characters");
            yield return Click("network-back");Check(app.Page==AppPage.PlayMenu,"normal-mode back returns to the preceding mode selection");
            yield return Click("play-training");Check(app.Page==AppPage.CharacterSelect && !app.InDraft,"free training keeps its direct practice character choice");
            yield return Click("select-back");Check(app.Page==AppPage.PlayMenu,"free training back returns to mode selection");
            yield return Click("play-ai");
            Check(app.InDraft && app.MyDraftTurn && app.ActiveDraft.TurnIndex==0 && app.Simulation==null,"AI game starts with the human's first draft turn and no running game");
            foreach(var size in new[]{new Vector2Int(1024,768),new Vector2Int(1280,720),new Vector2Int(1920,1080)})
            {
                Screen.SetResolution(size.x,size.y,false);yield return new WaitForSecondsRealtime(.5f);
                foreach(string name in new[]{"draft-member-5","draft-placement","character-preview"})
                {var rect=app.UI.Root.Q(name).worldBound;var root=app.UI.Root.worldBound;Check(rect.width>30 && rect.height>30 && root.Contains(rect.min) && root.Contains(rect.max),size+" draft element fits: "+name);}
                Check(PicksButton("character-0") && PicksButton("character-1") && PicksButton("start-button") && PicksButton("select-back"),size+" draft choices and lock are accessible");
                yield return Capture("draft-"+size.x+"x"+size.y);
            }
            DraftMapClick(new Vec2(14,2));yield return null;
            Check(app.DraftPosition.X>0 && !app.UI.Root.Q<Button>("start-button").enabledSelf && app.DraftStatus.Length>0,"clicking the opposing half gives placement feedback and disables locking");
            Vec2 opening=new Vec2(-14,3);DraftMapClick(opening);yield return null;yield return Click("character-1");
            Check(Vec2.Distance(app.DraftPosition,opening)<.15f && app.Selected==CharacterId.Thor && app.UI.CharacterPreview.Character==CharacterId.Thor,"map click chooses actual world position and character choice updates the live model");
            yield return Click("start-button");
            Check(app.InDraft && app.ActiveDraft.Picks[0].Locked && !app.MyDraftTurn,"locking commits the human's role and position and advances to the other team");
            float deadline=Time.realtimeSinceStartup+5;while(app.InDraft && Time.realtimeSinceStartup<deadline)yield return null;
            Check(app.Page==AppPage.Match && app.Simulation.State.Players.Count==6 && app.Simulation.State.Players[0].Stats.Id==CharacterId.Thor,"all six locked choices create the real match with the selected character");
            Check(Vec2.Distance(app.Simulation.State.Players[0].Position,opening)<.15f && app.Simulation.State.IsOpeningWarmup,"selected opening position survives the protected kickoff countdown");
            app.ReplaySession();yield return null;
            Check(app.InDraft && app.ActiveDraft.TurnIndex==0 && !app.ActiveDraft.Picks[0].Locked,"replaying an AI game starts a fresh draft");
            yield return Click("select-back");yield return Click("play-tutorial");
            Check(app.Page==AppPage.Match && app.Tutorial?.Index==0 && app.Simulation.State.Mode==MatchMode.Training && !app.Simulation.State.IsOpeningWarmup,"tutorial button opens the first playable lesson immediately");
            Check(!app.UI.Root.Q<Button>("tutorial-next").enabledSelf,"incomplete lesson cannot advance without explicit skip");
            yield return Click("tutorial-skip");Check(app.Tutorial.Index==1,"skip opens the following lesson");
            yield return Click("tutorial-previous");Check(app.Tutorial.Index==0,"previous returns to a lesson for review");
            yield return Click("tutorial-retry");Check(app.Tutorial.Index==0 && !app.Tutorial.Complete,"retry resets current lesson and its completion");
            foreach(var size in new[]{new Vector2Int(1024,768),new Vector2Int(1280,720),new Vector2Int(1920,1080)})
            {
                Screen.SetResolution(size.x,size.y,false);yield return new WaitForSecondsRealtime(.5f);
                foreach(string name in new[]{"tutorial-next","tutorial-retry","tutorial-skip","tutorial-exit"})Check(PicksButton(name),size+" tutorial action remains visible: "+name);
                Check(app.UI.Root.Q("training-tools").resolvedStyle.display==DisplayStyle.None,size+" tutorial exposes lesson controls instead of advanced training tools");
                var rect=app.UI.Root.Q("tutorial-panel").worldBound;Check(rect.yMax<app.UI.Root.Q("battle-dock-anchor").worldBound.yMin,size+" lesson panel clears the skill dock");
                yield return Capture("tutorial-"+size.x+"x"+size.y);
            }
            for(int index=0;index<TutorialSession.Steps.Length;index++)
            {
                Check(app.Tutorial.Index==index,"lesson sequence: "+TutorialSession.Steps[index].Title);
                app.SetPaused(true);app.UI.ShowPause(false);
                var state=app.Simulation.State;var input=new ProbeInput();var sampler=new KeyboardMouseInput(app.WorldCamera,app.Keys,input);
                var lesson=app.Tutorial.Step.Lesson;
                input.Position=app.WorldCamera.WorldToScreenPoint(ArenaProjection.ToWorld(new Vec2(7,0)));
                switch(lesson)
                {
                    case TrainingLesson.Move:
                        input.Position=app.WorldCamera.WorldToScreenPoint(ArenaProjection.ToWorld(app.Tutorial.Target));input.Right=true;
                        LessonTick(LessonSample(sampler,input));LessonTick(default(PlayerCommand),200);break;
                    case TrainingLesson.Stop:
                        input.Pressed=app.Keys.Key(GameAction.Stop);LessonTick(LessonSample(sampler,input));
                        Check(state.Players[0].Order==UnitOrderKind.None,"stop lesson clears the existing movement order");break;
                    case TrainingLesson.Catch:
                        LessonTick(new PlayerCommand{Move=new Vec2(1,0),Aim=new Vec2(1,0)},45);
                        Check(state.Ball.OwnerId==0,"tutorial contact steals held ball without push input");break;
                    case TrainingLesson.Kick:
                        input.Pressed=input.HeldKey=app.Keys.Key(GameAction.Kick);LessonTick(LessonSample(sampler,input),8);
                        input.Clear();input.Released=app.Keys.Key(GameAction.Kick);LessonTick(LessonSample(sampler,input));break;
                    case TrainingLesson.Push:
                        LessonTick(new PlayerCommand{Move=new Vec2(1,0),Aim=new Vec2(1,0)},4);
                        input.Pressed=app.Keys.Key(GameAction.Push);LessonTick(LessonSample(sampler,input));break;
                    case TrainingLesson.Weapon:
                        input.Right=true;input.Position=app.WorldCamera.WorldToScreenPoint(ArenaProjection.ToWorld(state.Players[1].Position,.8f));
                        LessonTick(LessonSample(sampler,input));LessonTick(default(PlayerCommand),200);break;
                    case TrainingLesson.SkillQ:
                        input.Pressed=app.Keys.Key(GameAction.SkillQ);LessonTick(LessonSample(sampler,input));break;
                    case TrainingLesson.SkillE:case TrainingLesson.Detonate:
                        input.Pressed=app.Keys.Key(GameAction.SkillE);LessonTick(LessonSample(sampler,input));
                        if(lesson==TrainingLesson.Detonate){input.Clear();input.Pressed=app.Keys.Key(GameAction.Special);LessonTick(LessonSample(sampler,input));}break;
                    case TrainingLesson.Recovery:LessonTick(default(PlayerCommand),160);break;
                    case TrainingLesson.Respawn:LessonTick(default(PlayerCommand),310);break;
                    case TrainingLesson.ThorSkill:
                        input.Pressed=app.Keys.Key(GameAction.SkillE);LessonTick(LessonSample(sampler,input));
                        Check(sampler.SelectingSkillTarget && !app.Tutorial.Complete,"Thor lesson waits for left-click direction confirmation");
                        input.Clear();input.Left=true;LessonTick(LessonSample(sampler,input));break;
                    case TrainingLesson.Rules:LessonTick(default(PlayerCommand),125);break;
                }
                LessonTick(default(PlayerCommand),60);
                Check(app.Tutorial.Complete && app.UI.Root.Q<Button>("tutorial-next").enabledSelf,"actual commands and mechanics complete lesson: "+lesson);
                if(index==2 || index==11)yield return Capture("tutorial-complete-"+lesson);
                if(!app.Tutorial.Complete) {app.NextTutorialStep(true);yield return null;}
                else yield return Click("tutorial-next");
            }
            Check(app.Page==AppPage.PlayMenu && app.Tutorial==null && app.Simulation==null,"completing the final lesson returns to mode selection");
            app.GoHome();Screen.SetResolution(width,height,screenMode);yield return new WaitForSecondsRealtime(.5f);
        }
    }
}
