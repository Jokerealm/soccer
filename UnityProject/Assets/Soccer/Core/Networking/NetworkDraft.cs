using System;
using System.IO;
using System.Linq;

namespace Soccer.Core.Networking
{
    public sealed partial class NetworkRoom
    {
        public MatchDraft Draft { get; private set; }
        public event Action DraftChanged;
        private int draftEpoch;
        private float draftBroadcastAge;
        public void BeginDraft(MatchRules rules)
        {
            if(!CanStart) throw new InvalidOperationException("等待所有球员准备后才能选人");
            var humans=Enumerable.Range(0,6).Select(IsHumanSlot).ToArray();
            Draft=new MatchDraft(rules,humans,++draftEpoch);Lobby.Drafting=true;draftBroadcastAge=0;
            BroadcastLobby();BroadcastDraft();
        }
        public bool LockDraft(CharacterId character,Vec2 position)
        {
            if(!Connected || Draft==null || Draft.Complete) return false;
            if(IsHost) return ApplyDraftLock(LocalSlot,Draft.Epoch,character,position,null);
            server.Send(PacketKind.DraftLock,NetworkCodec.Pack(w=>{w.Write(Draft.Epoch);w.Write((int)character);w.Write(position.X);w.Write(position.Y);}));return true;
        }
        private void ReceiveDraftLock(Guest guest,WirePacket packet)
        {
            int epoch=0;CharacterId character=default;Vec2 point=default;
            NetworkCodec.Read(packet.Body,r=>{epoch=r.ReadInt32();character=(CharacterId)r.ReadInt32();point=new Vec2(r.ReadSingle(),r.ReadSingle());});
            ApplyDraftLock(guest.Member.Slot,epoch,character,point,guest.Peer);
        }
        private bool ApplyDraftLock(int slot,int epoch,CharacterId character,Vec2 point,IRoomPeer peer)
        {
            if(Draft==null || epoch!=Draft.Epoch) return false;
            if(!Draft.TryLock(slot,character,point,out var error))
            {Status=error;Changed?.Invoke();peer?.Send(PacketKind.Notice,NetworkCodec.Pack(w=>NetworkCodec.Text(w,error)));return false;}
            var member=Lobby.Members.FirstOrDefault(m=>m.Slot==slot);if(member!=null)member.Character=character;
            BroadcastDraft();FinishDraftIfReady();return true;
        }
        private void PumpDraft(float delta)
        {
            if(!IsHost || Draft==null || !Lobby.Drafting) return;
            bool changed=Draft.Tick(delta);draftBroadcastAge+=delta;
            if(changed || draftBroadcastAge>=.25f) {draftBroadcastAge=0;BroadcastDraft();}
            FinishDraftIfReady();
        }
        private void FinishDraftIfReady()
        {
            if(Draft?.Complete!=true) return;
            var start=new NetworkStart {Rules=Draft.Rules,Lineup=Draft.Lineup(),Positions=Draft.Positions()};
            LaunchMatch(start);
        }
        private void BroadcastDraft()
        {
            var body=NetworkCodec.Pack(w=>WriteDraft(w,Draft));
            foreach(var guest in guests.Where(g=>g.Joined))guest.Peer.Send(PacketKind.Draft,body);
            DraftChanged?.Invoke();
        }
        private void ReceiveDraft(WirePacket packet)
        {
            MatchDraft state=null;NetworkCodec.Read(packet.Body,r=>state=ReadDraft(r));
            if(Draft!=null && state.Epoch<Draft.Epoch) return;
            Draft=state;Lobby.Drafting=true;DraftChanged?.Invoke();
        }
        internal static void WriteDraft(BinaryWriter writer,MatchDraft draft)
        {
            writer.Write(draft.Epoch);NetworkCodec.WriteFields(writer,draft.Rules);writer.Write(draft.TurnIndex);writer.Write(draft.RemainingSeconds);
            for(int i=0;i<6;i++) {writer.Write(draft.Humans[i]);var p=draft.Picks[i];writer.Write((int)p.Character);writer.Write(p.Position.X);writer.Write(p.Position.Y);writer.Write(p.Locked);writer.Write(p.Automatic);}
        }
        internal static MatchDraft ReadDraft(BinaryReader reader)
        {
            int epoch=reader.ReadInt32();var rules=NetworkCodec.ReadFields<MatchRules>(reader);rules.Validate();int turn=reader.ReadInt32();float remaining=reader.ReadSingle();
            var humans=new bool[6];var picks=new DraftPick[6];
            for(int i=0;i<6;i++) {humans[i]=reader.ReadBoolean();picks[i]=new DraftPick {Slot=i,Character=(CharacterId)reader.ReadInt32(),Position=new Vec2(reader.ReadSingle(),reader.ReadSingle()),Locked=reader.ReadBoolean(),Automatic=reader.ReadBoolean()};}
            var draft=new MatchDraft(rules,humans,epoch);draft.Restore(turn,remaining,picks);return draft;
        }
    }
}
