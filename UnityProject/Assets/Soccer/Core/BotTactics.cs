using System;

namespace Soccer.Core
{
    /// <summary>Uses only visible positions, velocities and resources, never another player's pending input.</summary>
    public static class BotTactics
    {
        public static bool ClearLane(MatchState state, PlayerState passer, Vec2 target, float ballRadius)
        {
            var delta = target - passer.Position;
            foreach (var enemy in state.Players)
            {
                if (enemy.Team == passer.Team || enemy.IsDead || enemy.Stun > .4f) continue;
                var position = enemy.Position + enemy.Velocity * .15f;
                if (Geometry.SweepCircle(passer.Position,delta,position,enemy.Stats.Radius+ballRadius+.15f,out float t) && t < .98f)
                    return false;
            }
            return true;
        }

        public static Vec2 KickTarget(MatchState state, MatchRules rules, PlayerState p)
        {
            float side = p.Team == Team.Cyan ? 1 : -1;
            Vec2 goal = new Vec2(side * (rules.FieldWidth / 2 + .5f), 0);
            float goalDistance = Vec2.Distance(p.Position,goal);
            for (int i=0;i<5;i++)
            {
                var shot = new Vec2(goal.X,(i-2)*rules.GoalWidth*.18f);
                if (goalDistance < 15 && ClearLane(state,p,shot,rules.BallRadius)) return shot;
            }
            Vec2 best = goal; float bestScore = float.NegativeInfinity;
            foreach (var ally in state.Players)
            {
                float distance = Vec2.Distance(ally.Position,p.Position);
                if (ally.Id == p.Id || ally.Team != p.Team || ally.IsDead || ally.Stun > 0 || distance < 2 || distance > 13) continue;
                float flight = Math.Min(.45f,distance/15);
                var lead = NavigationPathfinder.Clamp(rules,ally,ally.Position+ally.Velocity*flight);
                if (!ClearLane(state,p,lead,rules.BallRadius)) continue;
                float space = 10;
                foreach (var enemy in state.Players)
                    if (enemy.Team != p.Team && !enemy.IsDead) space=Math.Min(space,Vec2.Distance(enemy.Position,lead));
                float progress = (lead.X-p.Position.X)*side;
                float score = space*.8f + progress*.7f - distance*.12f;
                if (ally.KickCooldown > 1) score -= 3;
                if (p.Health < 40) score += 3;
                if (score > bestScore && (progress > 1 || p.Health < 45 || space > 4)) { bestScore=score;best=lead; }
            }
            return best;
        }

        public static Vec2 Position(MatchState state, MatchRules rules, PlayerState p, int closest)
        {
            float side = p.Team==Team.Cyan ? 1 : -1;
            var owner = state.Owner;
            if (owner == null)
            {
                // The closest player contests the predicted free-ball position; others keep passing lanes.
                var landing = state.Ball.Position + state.Ball.Velocity * .3f;
                return closest == p.Id ? NavigationPathfinder.Clamp(rules,p,landing)
                    : NavigationPathfinder.Clamp(rules,p,landing+new Vec2(-side*4,p.Id%2==0 ? 4 : -4));
            }
            if (owner.Team == p.Team)
            {
                float wing = p.Id%2==0 ? 1 : -1;
                var target = owner.Position + new Vec2(side*5,wing*5);
                if (!ClearLane(state,owner,target,rules.BallRadius)) target = owner.Position + new Vec2(-side*2,wing*6);
                return NavigationPathfinder.Clamp(rules,p,target);
            }
            if (closest == p.Id)
            {
                bool weak = owner.Health < 40 || owner.Energy < 20;
                return owner.Position + owner.Velocity * (weak ? .1f : .25f);
            }
            var ownGoal = new Vec2(-side*(rules.FieldWidth/2-2),0);
            var block = owner.Position + (ownGoal-owner.Position).Normalized*(p.Id%2==0 ? 4 : 7);
            // Stay between the carrier and our goal instead of everyone chasing the same opponent.
            return NavigationPathfinder.Clamp(rules,p,block+new Vec2(0,p.Id%2==0 ? 1.5f : -1.5f));
        }
    }
}
