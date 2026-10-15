using System;
using System.Collections;
using System.IO;
using Soccer.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Soccer.Runtime
{
    public sealed partial class RuntimeSmokeProbe
    {
        private static bool HistoryVisible(VisualElement element)
        {for(var current=element;current!=null;current=current.parent)if(current.resolvedStyle.display==DisplayStyle.None)return false;return element!=null;}
        private void HistoryFixture(int own,int opponent,CharacterId character)
        {
            app.OpenSelection(MatchMode.BotMatch);app.SelectCharacter(character);app.StartMatch();app.SetPaused(true);
            var state=app.Simulation.State;
            typeof(MatchState).GetProperty(nameof(MatchState.CyanScore)).SetValue(state,own);typeof(MatchState).GetProperty(nameof(MatchState.AmberScore)).SetValue(state,opponent);
            typeof(MatchState).GetProperty(nameof(MatchState.ElapsedSeconds)).SetValue(state,175f);
            typeof(PlayerState).GetProperty(nameof(PlayerState.Goals)).SetValue(state.Players[0],2);
            typeof(PlayerState).GetProperty(nameof(PlayerState.Knockouts)).SetValue(state.Players[0],3);app.EndSession();
        }
        private IEnumerator CaptureHistoryAndSelection()
        {
            int width=Screen.width,height=Screen.height;var mode=Screen.fullScreenMode;app.GoHome();yield return new WaitForSecondsRealtime(.35f);
            int before=app.History.Matches.Count;yield return Click("profile-button");yield return Click("profile-history");
            Check(app.Page==AppPage.History && app.UI.Root.Q<Label>("history-profile").text.Contains(app.PlayerName),"home history opens current offline profile");
            if(before==0)Check(HistoryVisible(app.UI.Root.Q("history-empty")) && !HistoryVisible(app.UI.Root.Q("history-detail")),"new profile displays an honest empty history");
            yield return Click("history-back");HistoryFixture(2,1,CharacterId.Marine);yield return null;
            Check(app.History.Matches.Count==Math.Min(50,before+1) && app.History.Matches[0].OwnScore==2 && app.History.Matches[0].Goals==2 && app.History.Matches[0].Knockouts==3 && app.History.Matches[0].Coins==100,"finished match records actual role, local stats, score and settled reward");
            yield return Click("results-history");Check(app.Page==AppPage.History && app.UI.Root.Q<Label>("history-detail-title").text.Contains("2 : 1") && app.UI.Root.Q<Label>("history-detail-stats").text.Contains("击倒  3"),"results link opens the recorded match details");
            yield return Click("history-back");Check(app.Page==AppPage.Results,"history opened from results returns to results");app.EndSession();
            Check(app.History.Matches.Count==Math.Min(50,before+1),"repeated results do not duplicate the completed match");app.GoHome();
            HistoryFixture(0,1,CharacterId.Thor);app.GoHome();HistoryFixture(0,0,CharacterId.Marine);app.GoHome();
            var loaded=new MatchHistory(new MatchHistoryStore(Path.Combine(output,"history"),"offline"));
            Check(loaded.Matches.Count==app.History.Matches.Count && loaded.Matches[0].Result==MatchResult.Draw && loaded.Matches[1].Character==CharacterId.Thor && loaded.Matches[1].Result==MatchResult.Defeat,"actual JSON reload preserves newest draw, loss and heroes");
            int count=app.History.Matches.Count;app.StartTutorial();app.EndSession();app.GoHome();app.BeginLocalDraft();app.BackFromSelection();
            app.OpenSelection(MatchMode.BotMatch);app.StartMatch();app.GoHome();Check(app.History.Matches.Count==count,"tutorial, unfinished game and cancelled draft do not pollute match history");
            yield return Click("profile-button");yield return Click("profile-history");
            foreach(var size in new[]{new Vector2Int(1024,768),new Vector2Int(1280,720),new Vector2Int(1920,1080)})
            {
                Screen.SetResolution(size.x,size.y,false);yield return new WaitForSecondsRealtime(.5f);
                Check(PicksButton("history-back") && PicksButton("history-row-0") && PicksButton("history-row-1"),size+" history return and recent entries receive pointer");
                var rect=app.UI.Root.Q("history-detail").worldBound;var bounds=app.UI.Root.worldBound;Check(bounds.Contains(rect.min) && bounds.Contains(rect.max),size+" match details fit the viewport");
                yield return Click("history-row-1");Check(app.UI.Root.Q<Label>("history-detail-meta").text.Contains("雷神") && app.UI.Root.Q<Label>("history-detail-title").text.Contains("失败"),"selecting a history row updates the personal detail panel");
                yield return Capture("history-"+size.x+"x"+size.y);
            }
            Check(app.UI.Root.Q("replay-button")==null,"simple match history does not advertise an unavailable replay");
            app.GoHome();app.OpenSelection(MatchMode.Training);
            foreach(var size in new[]{new Vector2Int(1024,768),new Vector2Int(1280,720),new Vector2Int(1920,1080)})
            {
                Screen.SetResolution(size.x,size.y,false);yield return new WaitForSecondsRealtime(.5f);
                foreach(var character in new[]{CharacterId.Marine,CharacterId.Thor})
                {
                    app.SelectCharacter(character);yield return new WaitForSecondsRealtime(.35f);
                    var root=app.UI.Root;var foldout=root.Q<Foldout>("character-parameters");
                    Check(!foldout.value && !HistoryVisible(root.Q("selected-stats")),"selection parameters are hidden until requested for "+character);
                    Check(!root.Q<Label>("q-name").text.Contains("能量") && !root.Q<Label>("e-name").text.Contains("秒") && root.Q<Label>("q-copy").text.Length<35 && root.Q<Label>("e-copy").text.Length<35,"ability cards show short effects without costs and cooldown text");
                    Check(root.Q("selection-q-art").resolvedStyle.backgroundImage.texture!=null && root.Q("selection-e-art").resolvedStyle.backgroundImage.texture!=null,"selection cards use actual matching ability art");
                    Check(Mathf.Abs(app.UI.CharacterPreview.Distance-6.4f)<.1f && PicksButton("start-button") && PicksButton("preview-auto"),size+" smaller model framing preserves selection controls");
                    yield return Capture("selection-clean-"+character+"-"+size.x+"x"+size.y);
                    foldout.value=true;root.Q<ScrollView>("character-details").ScrollTo(foldout);yield return new WaitForSecondsRealtime(.15f);
                    Check(HistoryVisible(root.Q("selected-stats")) && root.Q<Label>("q-detail").text.Contains("冷却") && root.Q<Label>("e-detail").text.Contains("能量"),"expanded parameters retain truthful costs and cooldown information");
                    yield return Capture("selection-parameters-"+character+"-"+size.x+"x"+size.y);foldout.value=false;root.Q<ScrollView>("character-details").scrollOffset=Vector2.zero;
                }
            }
            app.GoHome();Screen.SetResolution(width,height,mode);yield return new WaitForSecondsRealtime(.5f);
        }
    }
}
