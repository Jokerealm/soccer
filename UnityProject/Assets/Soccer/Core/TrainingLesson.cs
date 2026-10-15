using System;

namespace Soccer.Core
{
    public enum TrainingLesson { Move, Stop, Catch, Kick, Push, Weapon, SkillQ, SkillE, Detonate, Recovery, Respawn, ThorSkill, Rules }
    public sealed partial class MatchSimulation
    {
        public void ApplyOpeningPositions(Vec2[] positions)
        {
            if(State.Mode!=MatchMode.BotMatch || State.Tick!=0 || State.Phase!=MatchPhase.Countdown) throw new InvalidOperationException("只能在开局前布置球员");
            MatchDraft.ValidateOpening(Rules,positions);
            foreach(var player in State.Players) {player.Position=positions[player.Id];player.PreviousPosition=player.OpeningPosition=player.Position;}
        }
        /// <summary>Controlled drills use normal commands, damage and recovery after placing the teaching scenario.</summary>
        public void SetupTrainingLesson(TrainingLesson lesson)
        {
            if(State.Mode!=MatchMode.Training || !Enum.IsDefined(typeof(TrainingLesson),lesson)) throw new InvalidOperationException("只能在训练中设置教程");
            ResetPositions(false);State.Phase=MatchPhase.Playing;State.PhaseRemaining=0;State.MutableEvents.Clear();
            var player=State.Players[0];var target=State.Players[1];
            player.Position=player.PreviousPosition=new Vec2(-4,0);player.Facing=new Vec2(1,0);
            target.Position=target.PreviousPosition=new Vec2(4,0);target.Facing=new Vec2(-1,0);
            State.Ball.Position=State.Ball.PreviousPosition=new Vec2(-1,0);
            if(lesson==TrainingLesson.Push || lesson==TrainingLesson.Catch)
            { target.Position=target.PreviousPosition=new Vec2(-1.8f,0);GiveBallForSetup(target);ball.UpdateHeldBall(); }
            if(lesson==TrainingLesson.Kick) {GiveBallForSetup(player);ball.UpdateHeldBall();}
            if(lesson==TrainingLesson.Weapon) target.Position=target.PreviousPosition=new Vec2(1,0);
            if(lesson==TrainingLesson.SkillE || lesson==TrainingLesson.Detonate) target.Position=target.PreviousPosition=new Vec2(.5f,0);
            if(lesson==TrainingLesson.Recovery) {player.Health=70;player.Energy=60;player.EnergyDelay=0;}
            if(lesson==TrainingLesson.Respawn) combat.ApplyHit(target,player,player.Stats.MaxHealth*2,0,SkillSlot.Weapon);
            if(lesson==TrainingLesson.ThorSkill) target.Position=target.PreviousPosition=new Vec2(1,0);
            CancelPendingInputs();
            if(lesson==TrainingLesson.Stop) {player.Order=UnitOrderKind.Move;player.OrderPosition=new Vec2(0,3);}
        }
    }
}
