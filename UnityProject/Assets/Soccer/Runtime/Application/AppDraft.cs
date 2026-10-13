using System;
using System.Linq;
using Soccer.Core;
using UnityEngine;

namespace Soccer.Runtime
{
    public sealed partial class AppController
    {
        private MatchDraft localDraft;
        private int localDraftEpoch, shownNetworkDraftEpoch;
        private CharacterId[] preparedLineup;
        private Vec2[] preparedPositions;
        public MatchDraft ActiveDraft => Room?.Draft ?? localDraft;
        public bool InDraft => Page==AppPage.CharacterSelect && ActiveDraft!=null;
        public int DraftLocalSlot => Room?.LocalSlot ?? 0;
        public bool MyDraftTurn => InDraft && ActiveDraft.CurrentSlot==DraftLocalSlot;
        public Vec2 DraftPosition { get; private set; }
        public string DraftStatus { get; private set; } = "";
        public void OpenPlayMenu()
        {
            if(!EnsureEntered()) return;
            localDraft=null;Tutorial=null;Simulation=null;View.Clear();Page=AppPage.PlayMenu;
            UI.ShowPage(Page);Audio.SetContext(Page,false);
        }
        public void BeginLocalDraft()
        {
            if(!EnsureEntered())return;
            CloseNetworking();Tutorial=null;Simulation=null;View.Clear();LocalPlayerId=0;SelectedMode=MatchMode.BotMatch;
            localDraft=new MatchDraft(RulesAsset.CreateRules(),new[]{true,false,false,false,false,false},++localDraftEpoch);
            Selected=localDraft.Picks[0].Character;DraftPosition=localDraft.Picks[0].Position;DraftStatus="";
            selectionOrigin=AppPage.PlayMenu;Page=AppPage.CharacterSelect;UI.ShowPage(Page);Audio.SetContext(Page,false);
        }
        private void ShowNetworkDraft()
        {
            if(Room?.Draft==null)return;
            LocalPlayerId=Room.LocalSlot;SelectedMode=MatchMode.BotMatch;Tutorial=null;Simulation=null;Paused=false;
            if(shownNetworkDraftEpoch!=Room.Draft.Epoch || Page!=AppPage.CharacterSelect)
            {
                shownNetworkDraftEpoch=Room.Draft.Epoch;Selected=Room.Draft.Picks[LocalPlayerId].Character;
                DraftPosition=Room.Draft.Picks[LocalPlayerId].Position;DraftStatus="";View.Clear();Page=AppPage.CharacterSelect;
                UI.ShowPage(Page);Audio.SetContext(Page,false);
            }
            if(Room.Draft.Picks[LocalPlayerId].Locked) {Selected=Room.Draft.Picks[LocalPlayerId].Character;DraftPosition=Room.Draft.Picks[LocalPlayerId].Position;}
            UI.RefreshSelection();
        }
        public void SelectDraftPosition(Vec2 point)
        { if(!InDraft || ActiveDraft.Picks[DraftLocalSlot].Locked)return;DraftPosition=point;ActiveDraft.CanPlace(DraftLocalSlot,point,out var error);DraftStatus=error;UI.RefreshSelection(); }
        public void ConfirmSelection()
        {
            if(!InDraft) {StartMatch();return;}
            if(!MyDraftTurn)return;
            if(Room!=null) { if(!Room.LockDraft(Selected,DraftPosition))DraftStatus=Room.Status; }
            else if(!localDraft.TryLock(0,Selected,DraftPosition,out var error))DraftStatus=error;
            UI.RefreshSelection();
        }
        private void TickDraft()
        {
            if(!InDraft)return;
            if(localDraft!=null)
            {
                localDraft.Tick(Mathf.Min(Time.unscaledDeltaTime,.1f));
                if(localDraft.Complete)
                {
                    preparedLineup=localDraft.Lineup();preparedPositions=localDraft.Positions();Selected=preparedLineup[0];localDraft=null;StartMatch();return;
                }
            }
            UI.RefreshDraft();
        }
        public string DraftName(int slot)
        {
            var member=Room?.Lobby.Members.FirstOrDefault(m=>m.Slot==slot);
            return member!=null ? member.Name+(slot==DraftLocalSlot ? " · 你" : "") : Room==null && slot==0 ? "你" : "AI "+(slot%3+1);
        }
        public void ReplaySession()
        {
            if(Room!=null) {if(Room.IsHost)Room.ReturnToLobby();return;}
            if(Tutorial!=null) {RestartTutorialStep();return;}
            if(SelectedMode==MatchMode.BotMatch)BeginLocalDraft();else StartMatch();
        }
    }
}
