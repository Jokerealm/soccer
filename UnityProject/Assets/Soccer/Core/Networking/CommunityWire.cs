using System;
using System.IO;

namespace Soccer.Core.Networking
{
    public static class CommunityWire
    {
        public static byte[] Draft(MatchDraft draft)=>NetworkCodec.Pack(w=>NetworkRoom.WriteDraft(w,draft));
        public static MatchDraft ReadDraft(byte[] bytes)
        {MatchDraft result=null;NetworkCodec.Read(bytes,r=>result=NetworkRoom.ReadDraft(r));return result;}
        public static byte[] Start(NetworkStart start)=>NetworkCodec.Pack(w=>{
            NetworkCodec.Text(w,NetworkCodec.Version);NetworkCodec.WriteFields(w,start.Rules);
            foreach(var id in start.Lineup)w.Write((int)id);w.Write(start.Positions!=null);
            if(start.Positions!=null)foreach(var point in start.Positions){w.Write(point.X);w.Write(point.Y);}
        });
        public static NetworkStart ReadStart(byte[] bytes)
        {
            var start=new NetworkStart();NetworkCodec.Read(bytes,r=>{
                if(NetworkCodec.Text(r)!=NetworkCodec.Version)throw new InvalidDataException("游戏版本不一致");
                start.Rules=NetworkCodec.ReadFields<MatchRules>(r);start.Rules.Validate();start.Lineup=new CharacterId[6];
                for(int i=0;i<6;i++){start.Lineup[i]=(CharacterId)r.ReadInt32();if(!RoomOptions.ValidCharacter(start.Lineup[i]))throw new InvalidDataException();}
                if(r.ReadBoolean()){start.Positions=new Vec2[6];for(int i=0;i<6;i++)start.Positions[i]=new Vec2(r.ReadSingle(),r.ReadSingle());MatchDraft.ValidateOpening(start.Rules,start.Positions);}
            });return start;
        }
    }
}
