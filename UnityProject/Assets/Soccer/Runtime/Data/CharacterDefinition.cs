using Soccer.Core;
using UnityEngine;

namespace Soccer.Runtime
{
    [CreateAssetMenu(menuName = "技能足球/测试装备配置")]
    public sealed class CharacterDefinition : ScriptableObject
    {
        public CharacterStats Stats = new CharacterStats();
        public int PrototypeVersion;
        public string Role;
        [TextArea] public string PassiveDescription;
        public string QName;
        [TextArea] public string QDescription;
        public string EName;
        [TextArea] public string EDescription;
        public Color Accent = Color.cyan;
        public Texture2D Portrait;
        public CharacterAnimations Animations;
        public CharacterStats CreateStats() => JsonUtility.FromJson<CharacterStats>(JsonUtility.ToJson(Stats));

        public void EnsureCombatPrototype()
        {
            if (StarStrikerLoadouts.IsReference(Stats.Id))
            {
                if (PrototypeVersion < StarStrikerLoadouts.Version) Stats = StarStrikerLoadouts.Create(Stats.Id);
                Stats.MaxHealth = Stats.MaxEnergy = 100;
                bool marine = Stats.Id == CharacterId.Marine;
                var loadout = Stats.Loadout;
                var tuning = Stats.ReferenceSkills;
                Role = marine ? "兴奋剂抢位 · 炸弹控制" : "高速驱动 · 突进接应";
                PassiveDescription = marine
                    ? $"右键敌人／脉冲步枪：每发 {loadout.WeaponDamage:0} 伤害，射程 {loadout.WeaponRange:0.#}。3 格储备，自动追击并在射程内攻击，S 停止。"
                    : $"右键敌人／双联机炮：每次 {loadout.WeaponShots} 发，每发 {loadout.WeaponDamage:0} 伤害，射程 {loadout.WeaponRange:0.#}。3 格储备，自动追击并在射程内攻击，S 停止。";
                QName = marine ? "兴奋剂" : "超级驱动";
                QDescription = marine
                    ? $"消耗 10 生命，移动速度增加 {tuning.MarineStimSpeed:0.##}，持续 {tuning.MarineStimDuration:0.#} 秒。"
                    : $"消耗 10 生命，移动速度增加 {tuning.ThorDriveSpeed:0.##}，每 {tuning.ThorDecayInterval:0.###} 秒削减 10% 的加速量；本轮持续 {tuning.ThorDriveDuration:0.#} 秒。";
                EName = marine ? "遥控炸弹" : "辅助推进器";
                EDescription = marine
                    ? $"无球时放在脚下，持球时装在足球上，{tuning.BombFuse:0.#} 秒后爆炸。击退范围内敌人并眩晕 {tuning.BombStun:0.##} 秒。G 可消耗 10 能量提前引爆。"
                    : $"消耗 10 生命，沿朝向突进 {tuning.ThorDashDistance:0.#}，立即刷新超级驱动冷却；碰到单位或边界会停下。";
                PrototypeVersion = StarStrikerLoadouts.Version;
                return;
            }
            if (PrototypeVersion < PrototypeLoadouts.Version) Stats = PrototypeLoadouts.Create(Stats.Id);
            var kit = Stats.Loadout;
            bool dragon = Stats.Id == CharacterId.WindShuttle, rain = Stats.Id == CharacterId.IronGuard;
            Role = dragon ? "弹肚开路 · 护住队友" : rain ? "锅铲压阵 · 开工提速" : "饭团远投 · 开席回血";
            string weapon = dragon ? "肚皮弹锤" : rain ? "锅铲三连" : "饭团投手";
            string damage = kit.WeaponShots > 1 ? $"{kit.WeaponShots} 发散射，每发 {kit.WeaponDamage:0} 伤害" : $"{kit.WeaponDamage:0} 伤害";
            string range = dragon ? "近身挥击" : rain ? "中距离散射" : "远距离投射";
            PassiveDescription = $"右键敌人／{weapon}：{range}，{damage}。{kit.AmmoCapacity} 格储备，间隔 {kit.WeaponCooldown:0.##} 秒；停火后每 {kit.AmmoReloadSeconds:0.##} 秒补一格，不耗能量。自动追击并在射程内攻击，S 停止。";
            QName = dragon ? "弹肚波" : rain ? "大锅震场" : "饭团开路";
            QDescription = dragon ? $"发射弹肚波，碰场边反弹一次；{kit.QDamage:0} 伤害、眩晕 {kit.QStun:0.##} 秒，命中敌人后消失。" :
                rain ? $"推出短距离宽锅气劲；{kit.QDamage:0} 伤害、眩晕 {kit.QStun:0.##} 秒。适合卡住防守者，但可以横移躲开。" :
                $"向扇区扔出 {kit.QShots} 颗饭团；每颗 {kit.QDamage:0} 伤害，眩晕 {kit.QStun:0.##} 秒。多颗命中不延长眩晕。";
            EName = dragon ? "Duang护罩" : rain ? "开工啦" : "开席";
            EDescription = kit.Support == SupportKind.TeamShield ? $"为自己和附近队友提供 {kit.SupportStrength:0} 护盾，持续 {kit.SupportDuration:0.#} 秒；消耗 {kit.EEnergy:0} 能量。" :
                kit.Support == SupportKind.TeamBoost ? $"自己和附近队友加速 {kit.SupportStrength:P0}，持续 {kit.SupportDuration:0.#} 秒；消耗 {kit.EEnergy:0} 能量。" :
                $"恢复自己和附近存活队友 {kit.SupportStrength:0} 生命；消耗 {kit.EEnergy:0} 能量。";
            PrototypeVersion = PrototypeLoadouts.Version;
        }
    }
}
