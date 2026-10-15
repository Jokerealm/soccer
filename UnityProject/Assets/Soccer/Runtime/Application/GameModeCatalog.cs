namespace Soccer.Runtime
{
    /// <summary>前端玩法入口独立于离线模拟模式；房间联机与离线训练共用比赛规则，排位仍独立于本地对练。</summary>
    public enum GamePlaylist { Normal, Ranked, Entertainment }
    public enum ConnectionRoute { Internet, LAN, Steam }

    public sealed class GameModeInfo
    {
        public readonly string Name, EnglishName, Subtitle, Description, RuleOne, RuleTwo, RuleThree;
        public GameModeInfo(string name, string english, string subtitle, string description, string one, string two, string three)
        { Name = name; EnglishName = english; Subtitle = subtitle; Description = description; RuleOne = one; RuleTwo = two; RuleThree = three; }
    }

    public static class GameModeCatalog
    {
        private static readonly GameModeInfo[] Modes = {
            new GameModeInfo("普通模式", "CASUAL PLAY", "开路，救援，配合进球。", "用武器牵制对手、用技能保护队友，在混战中配合把球送进球门。",
                "武器普攻 + 耗能技能 + 团队支援", "击倒掉球，5 秒后原地复活", "支持互联网与局域网房间，空位由 AI 补齐"),
            new GameModeInfo("排位模式", "RANKED LEAGUE", "每一场，都是向上一步。", "在公平的竞技规则下与实力相近的对手交锋，用团队配合争取胜利。",
                "与普通模式共用竞技规则", "服务端结算排位分与比赛结果", "通过游戏帐号服务组队排位，六名真人开局"),
            new GameModeInfo("娱乐模式", "PARTY ARENA", "让意想不到，成为好球。", "更大胆的技能、更有趣的球场变化，和朋友一起享受欢乐对局。",
                "规划强化技能与球场事件", "欢乐对局，不影响段位", "规划支持互联网与局域网房间")
        };
        public static GameModeInfo Get(GamePlaylist mode) => Modes[(int)mode];
        public static bool SupportsLAN(GamePlaylist mode) => mode != GamePlaylist.Ranked;
    }
}
