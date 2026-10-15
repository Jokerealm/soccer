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

        public struct KickPlan
        {
            public bool Ready,Pass;
            public int ReceiverId;
            public Vec2 Target;
        }
        public static Vec2 Lead(MatchRules rules,PlayerState passer,PlayerState receiver)
        {
            float limit=rules.BotDifficulty>=BotDifficulty.God ? .65f : rules.BotDifficulty==BotDifficulty.Player ? .25f : 0;
            float flight=Math.Min(limit,Vec2.Distance(passer.Position,receiver.Position)/11);
            return NavigationPathfinder.Clamp(rules,receiver,receiver.Position+receiver.Velocity*flight);
        }
        public static KickPlan PlanKick(MatchState state,MatchRules rules,PlayerState p)
        {
            float side=p.Team==Team.Cyan ? 1 : -1;
            var goal=new Vec2(side*(rules.FieldWidth/2+.5f),0);
            float goalDistance=Vec2.Distance(p.Position,goal),pressure=NearestEnemy(state,p,p.Position);
            // A clear close-range finish takes priority over circulating the ball.
            if(goalDistance<15)for(int i=0;i<(rules.BotDifficulty>=BotDifficulty.God ? 5 : 1);i++)
            {
                var shot=new Vec2(goal.X,(rules.BotDifficulty>=BotDifficulty.God ? i-2 : 0)*rules.GoalWidth*.18f);
                if(ClearLane(state,p,shot,rules.BallRadius))return new KickPlan{Ready=true,Target=shot,ReceiverId=-1};
            }
            var best=new KickPlan();float bestScore=float.NegativeInfinity;
            foreach(var ally in state.Players)
            {
                float distance=Vec2.Distance(ally.Position,p.Position);
                if(ally.Id==p.Id || ally.Team!=p.Team || ally.IsDead || ally.Stun>0 || ally.PushRemaining>0 || distance<2.5f || distance>(rules.BotDifficulty>=BotDifficulty.God ? 14 : 11))continue;
                var lead=Lead(rules,p,ally);
                float space=NearestEnemy(state,p,lead),progress=(lead.X-p.Position.X)*side;
                bool escape=pressure<4 || p.Health<45 || p.Energy<15;
                if(space<2 || !ClearLane(state,p,lead,rules.BallRadius))continue;
                if(escape ? progress < -5 || space < pressure+.6f : progress<2)continue;
                if(rules.BotDifficulty==BotDifficulty.BallBoy && (!escape || progress<0))continue;
                float required=(float)Math.Sqrt(2*rules.BallDeceleration*Math.Max(0,Vec2.Distance(p.Position,lead)-.7f)+9);
                if(required>rules.FullKickSpeed*(float)Math.Sqrt(p.Stats.KickDistance))continue;
                float score=space*.9f+progress*.65f-distance*.12f+(escape ? 3 : 0);
                if(ally.KickCooldown>1)score-=1;
                if(score>bestScore){bestScore=score;best=new KickPlan{Ready=true,Pass=true,ReceiverId=ally.Id,Target=lead};}
            }
            return best;
        }
        public static float NearestEnemy(MatchState state,PlayerState p,Vec2 point)
        {
            float space=12;
            foreach(var enemy in state.Players)if(enemy.Team!=p.Team && !enemy.IsDead)space=Math.Min(space,Vec2.Distance(enemy.Position,point));
            return space;
        }
        public static Vec2 KickTarget(MatchState state,MatchRules rules,PlayerState p)
        {
            var plan=PlanKick(state,rules,p);
            return plan.Ready ? plan.Target : new Vec2((p.Team==Team.Cyan ? 1 : -1)*(rules.FieldWidth/2+.5f),0);
        }
        public static int Chaser(MatchState state,Team team)
        {
            int best=-1;float score=float.PositiveInfinity;
            var landing=state.Ball.Position+state.Ball.Velocity*.25f;
            foreach(var ally in state.Players)
            {
                if(ally.Team!=team || ally.IsDead || ally.Stun>0)continue;
                float distance=Vec2.Distance(ally.Position,landing);
                if(state.Ball.OwnerId<0 && ally.Id==state.Ball.LastKickerId && state.Ball.SinceKick<1)distance+=5;
                if(distance<score){score=distance;best=ally.Id;}
            }
            return best;
        }
        private static float Wing(MatchState state,PlayerState p,int excluded)
        {
            int rank=0;
            foreach(var ally in state.Players)if(ally.Team==p.Team && ally.Id!=excluded && ally.Id<p.Id)rank++;
            return rank%2==0 ? -1 : 1;
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
                    : NavigationPathfinder.Clamp(rules,p,landing+new Vec2(-side*4,Wing(state,p,closest)*4));
            }
            if (owner.Team == p.Team)
            {
                float wing = Wing(state,p,owner.Id);
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
            var block = owner.Position + (ownGoal-owner.Position).Normalized*(Wing(state,p,closest)<0 ? 4 : 7);
            // Stay between the carrier and our goal instead of everyone chasing the same opponent.
            return NavigationPathfinder.Clamp(rules,p,block+new Vec2(0,Wing(state,p,closest)*1.5f));
        }
    }
}
