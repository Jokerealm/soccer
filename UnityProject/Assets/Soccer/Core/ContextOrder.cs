namespace Soccer.Core
{
    /// <summary>右键上下文解析共用于原生输入和规则测试；有武器点敌人进攻，友军跟随，足球追球。</summary>
    public static class ContextOrder
    {
        public static PlayerCommand Create(MatchState state, MatchRules rules, int playerId, Vec2 ground,
            int clickedPlayerId = -1, bool clickedBall = false, bool queue = false)
        {
            var p = state.Players[playerId];
            var kind = clickedBall ? UnitOrderKind.ChaseBall : UnitOrderKind.Move;
            if (clickedPlayerId >= 0 && clickedPlayerId < state.Players.Count && clickedPlayerId != playerId && !state.Players[clickedPlayerId].IsDead)
                kind = state.Players[clickedPlayerId].Team != p.Team && rules.EnableCombat && p.Stats.Loadout.HasWeapon
                    ? UnitOrderKind.Attack : UnitOrderKind.Follow;
            return new PlayerCommand { OrderPressed = true, OrderKind = kind, OrderPosition = ground,
                OrderTargetId = clickedPlayerId, QueueOrder = queue };
        }
    }
}
