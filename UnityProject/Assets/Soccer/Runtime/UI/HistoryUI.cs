using System;
using Soccer.Core;
using UnityEngine.UIElements;

namespace Soccer.Runtime
{
    public sealed class HistoryUI
    {
        private readonly AppController app;private readonly VisualElement root;private string selected;
        public HistoryUI(AppController controller,VisualElement ui){app=controller;root=ui;}
        public static string Hero(CharacterId id)=>id==CharacterId.Marine ? "枪兵" : id==CharacterId.Thor ? "雷神" : id.ToString();
        public static string Result(MatchResult value)=>value==MatchResult.Victory ? "胜利" : value==MatchResult.Defeat ? "失败" : "平局";
        public static string Source(MatchSource value)=>value==MatchSource.AI ? "AI 对练" : value==MatchSource.LAN ? "局域网" : value==MatchSource.Steam ? "Steam" : "互联网";
        private static string Date(MatchRecord value)=>DateTimeOffset.FromUnixTimeSeconds(value.FinishedUnixSeconds).ToLocalTime().ToString("MM-dd HH:mm");
        private static string Duration(float seconds){int value=(int)seconds;return value/60+":"+(value%60).ToString("00");}
        public void Open(){selected=null;Refresh();root.Q<ScrollView>("history-list").scrollOffset=UnityEngine.Vector2.zero;}
        public void Refresh()
        {
            root.Q<Label>("history-profile").text=app.WalletProfile+" · 最近 50 场";
            root.Q<Label>("history-status").text=app.HistoryStatus;
            var list=root.Q<ScrollView>("history-list");list.Clear();var matches=app.History?.Matches;
            root.Q("history-empty").EnableInClassList("hidden",matches!=null && matches.Count>0);
            root.Q("history-detail").EnableInClassList("hidden",matches==null || matches.Count==0);
            if(matches==null || matches.Count==0)return;
            MatchRecord picked=null;
            foreach(var record in matches)if(record.MatchId==selected)picked=record;
            if(picked==null)picked=matches[0];selected=picked.MatchId;
            int index=0;
            foreach(var record in matches)
            {
                var captured=record;var button=new Button(()=>{selected=captured.MatchId;app.Audio.PlayClick();Refresh();}){name="history-row-"+(index++)};button.AddToClassList("history-row");
                button.EnableInClassList("history-row-selected",record.MatchId==selected);
                var line=new VisualElement();line.AddToClassList("row");
                var result=new Label(Result(record.Result));result.AddToClassList("history-result");result.AddToClassList("history-"+record.Result.ToString().ToLowerInvariant());line.Add(result);
                var role=new Label(Hero(record.Character)+" · "+Source(record.Source));role.AddToClassList("history-role");role.AddToClassList("grow");line.Add(role);
                var score=new Label(record.OwnScore+" : "+record.OpponentScore);score.AddToClassList("history-score");line.Add(score);button.Add(line);
                var meta=new Label(Date(record)+"    "+Duration(record.DurationSeconds));meta.AddToClassList("history-meta");button.Add(meta);list.Add(button);
            }
            root.Q<Label>("history-detail-title").text=Result(picked.Result)+" · "+picked.OwnScore+" : "+picked.OpponentScore;
            root.Q<Label>("history-detail-meta").text=Hero(picked.Character)+" · "+Source(picked.Source)+"\n"+Date(picked)+" · "+Duration(picked.DurationSeconds);
            root.Q<Label>("history-detail-stats").text="进球  "+picked.Goals+"    碰撞夺球  "+picked.Steals+"\n踢球  "+picked.Kicks+"    成功传球  "+picked.Passes+"\n\n击倒  "+picked.Knockouts+"    被击倒  "+picked.Deaths+"\n伤害  "+picked.DamageDealt.ToString("0")+"    治疗  "+picked.HealingDone.ToString("0");
            root.Q<Label>("history-detail-coins").text="本局金币  +"+picked.Coins;
        }
    }
}
