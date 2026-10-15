using System;
using System.Collections;
using Soccer.Core;
using UnityEngine;
using UnityEngine.Networking;

namespace Soccer.Runtime
{
    public sealed partial class AppController
    {
        public void StartChatPoll(string channel,string target,string context,long after)
        {UI.Chat.PollStarted();StartCoroutine(PollChat(channel,target,context,after,socialGeneration));}
        private IEnumerator PollChat(string channel,string target,string context,long after,int generation)
        {
            ChatHistory history=null;string error="";
            using(var request=SocialRequest("/api/chat?channel="+Uri.EscapeDataString(channel)+"&target="+Uri.EscapeDataString(target)+"&after="+after,"GET"))
            {
                yield return request.SendWebRequest();if(generation!=socialGeneration)yield break;
                if(request.result==UnityWebRequest.Result.Success){try{history=JsonUtility.FromJson<ChatHistory>(request.downloadHandler.text);if(history?.Messages==null)throw new ArgumentException();}catch(ArgumentException){error="聊天记录暂时无法读取";}}
                else error=SocialFailure(request);
            }
            UI.Chat.Polled(context,history,error);
        }
        public void StartChatSend(ChatRequest message,string context)=>StartCoroutine(SendChatMessage(message,context,socialGeneration));
        private IEnumerator SendChatMessage(ChatRequest message,string context,int generation)
        {
            ChatMessage result=null;string error="";
            using(var request=SocialRequest("/api/chat","POST",JsonUtility.ToJson(message)))
            {
                yield return request.SendWebRequest();if(generation!=socialGeneration)yield break;
                if(request.result==UnityWebRequest.Result.Success){try{result=JsonUtility.FromJson<ChatMessage>(request.downloadHandler.text);if(result==null || result.Id<1)throw new ArgumentException();}catch(ArgumentException){error="消息发送结果暂时无法确认";}}
                else error=SocialFailure(request);
            }
            UI.Chat.Sent(context,result,error);
        }
    }
}
