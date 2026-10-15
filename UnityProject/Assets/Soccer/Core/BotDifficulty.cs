using System;

namespace Soccer.Core
{
    public enum BotDifficulty { BallBoy, Player, God, Cheater }

    public static class BotDifficulties
    {
        public static readonly string[] Names = { "球童", "球员", "球神", "作弊者" };
        public static readonly string[] Descriptions = {
            "较慢反应、简单追球与射门，适合熟悉操作。",
            "会传球、使用技能和基本接应，正常属性。",
            "判断球路、预判接应与回防，利用资源差制造进球机会；正常属性。",
            "球神决策，机器人生命与能量上限 +25%，移动速度 +12%；恢复速度 +15%。"
        };
        public static int DecisionTicks(BotDifficulty difficulty)
            => difficulty == BotDifficulty.BallBoy ? 24 : difficulty == BotDifficulty.Player ? 11 : 5;

        public static CharacterStats Configure(CharacterStats source, bool bot, BotDifficulty difficulty)
        {
            var copy = source.Copy();
            if (bot && difficulty == BotDifficulty.Cheater)
            {
                copy.MaxHealth *= 1.25f; copy.MaxEnergy *= 1.25f; copy.Speed *= 1.12f;
                copy.HealthRegen *= 1.15f; copy.EnergyRegen *= 1.15f;
            }
            return copy;
        }
    }
}
