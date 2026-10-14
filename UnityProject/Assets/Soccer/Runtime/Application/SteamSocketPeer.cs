using System;
using System.Runtime.InteropServices;
using Soccer.Core.Networking;
using Steamworks;

namespace Soccer.Runtime
{
    /// <summary>Message boundaries and authenticated identities come from Steam Networking Sockets.</summary>
    public sealed class SteamSocketPeer : IRoomPeer
    {
        public HSteamNetConnection Handle {get;}
        public string AuthenticatedAccountId {get;}
        private bool disposed;private int remaining=128;private readonly IntPtr[] received=new IntPtr[1];
        public SteamSocketPeer(HSteamNetConnection handle,ulong remote)
        {if(!SteamNetworkingSockets.GetConnectionInfo(handle,out var info) || info.m_identityRemote.GetSteamID64()!=remote || !info.m_identityRemote.GetSteamID().IsValid())throw new ArgumentException("Steam连接身份无效");Handle=handle;AuthenticatedAccountId=SteamAccount.FromId(remote);}
        public bool Connected=>!disposed && SteamNetworkingSockets.GetConnectionInfo(Handle,out var info) && info.m_eState==ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected;
        public void Pump(){remaining=128;}
        public bool Send(PacketKind kind,byte[] body)
        {
            if(!Connected)return false;var bytes=RoomFrame.Encode(kind,body);var pin=GCHandle.Alloc(bytes,GCHandleType.Pinned);
            try{return SteamNetworkingSockets.SendMessageToConnection(Handle,pin.AddrOfPinnedObject(),(uint)bytes.Length,Constants.k_nSteamNetworkingSend_Reliable|Constants.k_nSteamNetworkingSend_NoNagle,out _) == EResult.k_EResultOK;}
            finally{pin.Free();}
        }
        public bool TryRead(out WirePacket packet)
        {
            packet=null;if(!Connected || remaining--<=0)return false;
            int count=SteamNetworkingSockets.ReceiveMessagesOnConnection(Handle,received,1);if(count<=0)return false;
            try
            {
                var message=SteamNetworkingMessage_t.FromIntPtr(received[0]);if(message.m_cbSize<1 || message.m_cbSize>NetworkCodec.MaxFrameBytes+1)throw new ArgumentException();
                var bytes=new byte[message.m_cbSize];Marshal.Copy(message.m_pData,bytes,0,bytes.Length);packet=RoomFrame.Decode(bytes);return true;
            }
            catch(Exception){Dispose();return false;}
            finally{SteamNetworkingMessage_t.Release(received[0]);received[0]=IntPtr.Zero;}
        }
        public void Dispose(){if(disposed)return;disposed=true;SteamNetworkingSockets.CloseConnection(Handle,0,"room closed",true);}
    }
}
