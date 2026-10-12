using Soccer.Core;
using UnityEngine;

namespace Soccer.Runtime
{
    /// <summary>只负责显示投影；碰撞一直在未压缩的逻辑平面进行。</summary>
    public static class ArenaProjection
    {
        public const float GroundYScale = 0.819152f;
        public static readonly Vector3 VisualUp = new Vector3(0, 0.573576f, GroundYScale);
        public static Vector3 VisualOffset(float x, float y) => Vector3.right * x + VisualUp * y;
        public static Vector3 ToWorld(Vec2 ground, float height = 0) => new Vector3(ground.X, height, ground.Y);
        public static Vec2 ToGround(Vector3 world) => new Vec2(world.x, world.z);
    }
}
