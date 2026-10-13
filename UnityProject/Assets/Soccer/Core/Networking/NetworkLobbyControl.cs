using System;
using System.Linq;

namespace Soccer.Core.Networking
{
    public sealed partial class NetworkRoom
    {
        private MatchRules countdownRules;
        private float countdownBroadcastAge;
        public bool CountingDown=>Lobby?.CountdownRemaining>0;
        public int OccupiedSlots=>Lobby==null ? 0 : Lobby.Members.Length+Lobby.Bots.Length;
        public bool SetBot(int slot,bool present)
        {
            if(!IsHost || !Connected || Running || Lobby.Drafting || slot<0 || slot>=6 || Lobby.Members.Any(m=>m.Slot==slot) ||
                guests.Any(g=>g.Challenged && !g.Joined && g.Peer.Connected && g.Member.Slot==slot))return false;
            bool exists=Lobby.Bots.Contains(slot);if(exists==present)return true;
            Lobby.Bots=present ? Lobby.Bots.Concat(new[]{slot}).OrderBy(i=>i).ToArray() : Lobby.Bots.Where(i=>i!=slot).ToArray();
            CancelCountdown();foreach(var member in Lobby.Members)member.Ready=false;BroadcastLobby();return true;
        }
        public void FillBots(){if(IsHost)for(int slot=0;slot<6;slot++)if(!Lobby.Members.Any(m=>m.Slot==slot))SetBot(slot,true);}
        public void ClearBots(){if(IsHost)foreach(int slot in Lobby.Bots.ToArray())SetBot(slot,false);}
        public bool BeginCountdown(MatchRules rules)
        {
            if(!CanStart)return false;rules.Validate();countdownRules=rules;Lobby.CountdownRemaining=3;countdownBroadcastAge=0;BroadcastLobby();return true;
        }
        private bool EveryoneReady=>OccupiedSlots==6 && Lobby.Members.All(m=>m.Ready);
        private void CancelCountdown(){if(Lobby!=null)Lobby.CountdownRemaining=0;countdownRules=null;countdownBroadcastAge=0;}
        private void PumpCountdown(float delta)
        {
            if(!IsHost || !CountingDown)return;
            if(!EveryoneReady){CancelCountdown();BroadcastLobby();return;}
            Lobby.CountdownRemaining=Math.Max(0,Lobby.CountdownRemaining-delta);countdownBroadcastAge+=delta;
            if(Lobby.CountdownRemaining<=0){var rules=countdownRules;countdownRules=null;BroadcastLobby();BeginDraft(rules);}
            else if(countdownBroadcastAge>=.2f){countdownBroadcastAge=0;BroadcastLobby();}
        }
    }
}
