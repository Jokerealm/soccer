namespace Soccer.Core
{
    /// <summary>一帧输入意图。按键边沿只消费一次；输入提供者不能直接修改比赛状态。</summary>
    public struct PlayerCommand
    {
        public Vec2 Move;
        public Vec2 Aim;
        public bool OrderPressed;
        public UnitOrderKind OrderKind;
        public Vec2 OrderPosition;
        public int OrderTargetId;
        public bool QueueOrder;
        public bool StopPressed;
        // AI 的方向意图也使用避障；键盘测试的直接移动仍可用于底层验证。
        public bool Navigate;

        public bool KickPressed;
        public bool KickHeld;
        public bool KickReleased;
        public bool Cancel;
        public bool AttackPressed;
        public bool AttackHeld;
        public bool TacklePressed;
        public bool QPressed;
        public bool EPressed;
        public bool EReleased;
        public bool SpecialPressed;

        public PlayerCommand WithoutEdges()
        {
            return new PlayerCommand { Move = Move, Aim = Aim, KickHeld = KickHeld, AttackHeld = AttackHeld, Navigate = Navigate };
        }
    }

    public interface ICommandSource
    {
        PlayerCommand ReadCommand(MatchState state, int playerId);
        void Reset();
    }
}
