using System;

namespace Soccer.Core
{
    /// <summary>The visible hexagon and the warmup walls use the same six planes.</summary>
    public static class OpeningArea
    {
        public static Vec2 Vertex(Vec2 center, float radius, int index)
        {
            float angle = index * (float)Math.PI / 3;
            return center + new Vec2((float)Math.Cos(angle), (float)Math.Sin(angle)) * radius;
        }

        public static Vec2 Constrain(MatchRules rules, PlayerState player, Vec2 point)
        {
            float limit = Math.Max(.05f, rules.OpeningHexRadius * .8660254f - player.Stats.Radius);
            var offset = point - player.OpeningPosition;
            // Project onto the convex polygon, accounting for the entire player body.
            for (int pass = 0; pass < 12; pass++)
                for (int side = 0; side < 6; side++)
                {
                    float angle = (side + .5f) * (float)Math.PI / 3;
                    var normal = new Vec2((float)Math.Cos(angle), (float)Math.Sin(angle));
                    float excess = Vec2.Dot(offset, normal) - limit;
                    if (excess > 0) offset -= normal * excess;
                }
            return player.OpeningPosition + offset;
        }

        public static bool Contains(MatchRules rules, PlayerState player, Vec2 point)
            => Vec2.Distance(point, Constrain(rules, player, point)) < .001f;
    }
}
