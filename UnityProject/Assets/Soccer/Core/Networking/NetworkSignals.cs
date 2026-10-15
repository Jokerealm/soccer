using System;
using System.IO;
using System.Linq;

namespace Soccer.Core.Networking
{
    public sealed partial class NetworkRoom
    {
        public event Action<TacticalSignal> SignalReceived;
        private readonly double[] lastSignals = { -10,-10,-10,-10,-10,-10 };
        private MatchRules signalRules;

        public bool SendSignal(SignalKind kind,Vec2 position,int targetId=-1)
        {
            if(!Connected || !Running || signalRules==null)return false;
            var signal=new TacticalSignal {PlayerId=LocalSlot,Kind=kind,Position=position,TargetId=targetId};
            if(!signal.Valid(signalRules.FieldWidth,signalRules.FieldHeight))return false;
            if(IsHost)return RelaySignal(signal);
            return server.Send(PacketKind.TeamSignal,NetworkCodec.Pack(w=>{w.Write(matchEpoch);NetworkCodec.WriteFields(w,signal);}));
        }
        private bool RelaySignal(TacticalSignal signal)
        {
            if(signalRules==null || !signal.Valid(signalRules.FieldWidth,signalRules.FieldHeight))throw new InvalidDataException("信号无效");
            if(clock-lastSignals[signal.PlayerId]<1)return false;
            lastSignals[signal.PlayerId]=clock;
            var bytes=NetworkCodec.Pack(w=>{w.Write(matchEpoch);NetworkCodec.WriteFields(w,signal);});
            foreach(var guest in guests.Where(g=>g.Joined && g.Member.Team==(signal.PlayerId<3 ? Team.Cyan : Team.Amber)))
                guest.Peer.Send(PacketKind.TeamSignal,bytes);
            if((LocalSlot<3)==(signal.PlayerId<3))SignalReceived?.Invoke(signal);
            return true;
        }
        private void ReceiveSignal(WirePacket packet,int authenticatedSlot=-1)
        {
            int epoch=0;TacticalSignal signal=null;
            NetworkCodec.Read(packet.Body,r=>{epoch=r.ReadInt32();signal=NetworkCodec.ReadFields<TacticalSignal>(r);});
            if(epoch!=matchEpoch || !Running)return;
            if(IsHost)
            {
                if(signal.PlayerId!=authenticatedSlot)throw new InvalidDataException("信号身份无效");
                RelaySignal(signal);
            }
            else
            {
                if(signalRules==null || !signal.Valid(signalRules.FieldWidth,signalRules.FieldHeight) || (signal.PlayerId<3)!=(LocalSlot<3))
                    throw new InvalidDataException("信号队伍无效");
                SignalReceived?.Invoke(signal);
            }
        }
        private void ResetSignals(MatchRules rules)
        {signalRules=rules;for(int i=0;i<6;i++)lastSignals[i]=-10;}
    }
}
