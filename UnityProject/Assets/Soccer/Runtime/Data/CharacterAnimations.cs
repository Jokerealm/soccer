using UnityEngine;

namespace Soccer.Runtime
{
    /// <summary>由权威切帧索引导入，不等分图集；Pivot 与统一 PPU 已写入精灵 .meta。</summary>
    [CreateAssetMenu(menuName = "技能足球/角色动画")]
    public sealed class CharacterAnimations : ScriptableObject
    {
        public Sprite[] Turn;
        public Sprite[] Run;
        public Sprite[] Kick;
        public float RunFps = 18;
        public float KickFps = 10;
        [TextArea] public string Limitation = "跑步、踢球仅 SE；其他方向使用八方向转身帧，等待补画。禁止镜像或旋转代替方向动画。";
    }
}
