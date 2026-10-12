using Soccer.Core;
using UnityEngine;

namespace Soccer.Runtime
{
    [CreateAssetMenu(menuName = "技能足球/比赛规则")]
    public sealed class MatchRulesAsset : ScriptableObject
    {
        [TextArea] public string Assumptions = "离线原型采用设计稿建议值；D02—D10 尚未作为正式规则冻结。";
        public MatchRules Rules = new MatchRules();
        public MatchRules CreateRules() => JsonUtility.FromJson<MatchRules>(JsonUtility.ToJson(Rules));
    }
}
