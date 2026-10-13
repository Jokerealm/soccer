using System;
using Soccer.Core;

namespace Soccer.Runtime
{
    public sealed class TutorialStep
    {
        public readonly TrainingLesson Lesson;public readonly string Title,Body,Objective;
        public TutorialStep(TrainingLesson lesson,string title,string body,string objective) {Lesson=lesson;Title=title;Body=body;Objective=objective;}
    }
    public sealed class TutorialSession
    {
        public static readonly TutorialStep[] Steps={
            new TutorialStep(TrainingLesson.Move,"移动与指令","右键地面会持续移动。右键球员可追击或跟随；按住 Shift 右键可追加指令。","右键移动到金色标记"),
            new TutorialStep(TrainingLesson.Stop,"停止与取消","球员正在移动。S 或 H 停止移动；C 取消蓄力、技能准备和指令。","按 S 停下球员"),
            new TutorialStep(TrainingLesson.Catch,"接球与夺球","右键足球追球，实际碰到球才获得球权。碰到敌方脚前球也可夺球，无需先按空格；接球后有短暂接球保护。","从目标脚前接触足球并夺球"),
            new TutorialStep(TrainingLesson.Kick,"传球与射门","鼠标决定方向。按住 V 蓄力，松开踢出；C 可取消。踢球有 6 秒冷却，传射要留意队友与球门方向。","按住 V 后松开，踢出足球"),
            new TutorialStep(TrainingLesson.Push,"推抢与身体碰撞","靠近目标后按空格推开球员，会消耗生命。推队友或从敌人背后推，球随人移动；正面／侧面推敌人时球留原地，接触后才能拿球。","靠近目标，用空格推开球员"),
            new TutorialStep(TrainingLesson.Weapon,"追击与武器","右键敌方球员会自动追击，进入射程后攻击。武器有三格储备并逐格恢复；S 停止追击。击倒是为传球和进球创造机会。","右键目标，进入射程造成伤害"),
            new TutorialStep(TrainingLesson.SkillQ,"角色技能 1","枪兵按 Q 使用兴奋剂，消耗能量和生命并获得短暂加速。技能栏显示当前绑定、消耗和冷却；鼠标停在图标上可看说明。","使用 Q 兴奋剂"),
            new TutorialStep(TrainingLesson.SkillE,"角色技能 2","枪兵按 E 放置炸弹；无球时在脚下放置，持球时会装在足球上。炸弹会延迟爆炸，击退、震晕附近敌人。","使用 E 放置炸弹"),
            new TutorialStep(TrainingLesson.Detonate,"提前引爆","先按 E 放置炸弹，再按 G 提前引爆。引爆额外消耗 10 能量，与 E 的冷却独立。","放置炸弹后用 G 提前引爆"),
            new TutorialStep(TrainingLesson.Recovery,"生命与能量","生命会缓慢恢复；能量在消耗后延迟恢复。技能和推抢可能消耗生命，不能只盯能量。低生命时界面会提示危险。","观察生命和能量恢复"),
            new TutorialStep(TrainingLesson.Respawn,"击倒与复活","被击倒会掉球，5 秒后在原地满状态复活。复活后有短暂保护，主动行动会结束保护。","等待原地复活"),
            new TutorialStep(TrainingLesson.ThorSkill,"方向技能与鼠标确认","现在试雷神。Q 使用高速驱动；按 E 进入方向选择，鼠标选方向，再用左键确认突进。C 取消选择；右键仍负责移动。","按 E 选方向，再左键确认"),
            new TutorialStep(TrainingLesson.Rules,"一局比赛的流程","对局前两队轮流选择角色和初始位置，锁定后才开球。进球得分，上下半场各 2 分 30 秒；平局进入最多 90 秒金球加时。整球出界后根据最后触球判界外球、角球或球门球；任意球也要等待准备完成再踢。","阅读规则后完成教程")
        };
        public int Index { get; private set; }
        public TutorialStep Step => Steps[Index];
        public bool Complete { get; private set; }
        public float Age { get; private set; }
        public Vec2 Target => new Vec2(0,3);
        private bool detonationPressed;
        public TutorialSession(int index=0) {SetStep(index);}
        public void SetStep(int index) {Index=Math.Max(0,Math.Min(Steps.Length-1,index));Complete=false;Age=0;detonationPressed=false;}
        public void ObserveCommand(PlayerCommand command,MatchState state)
        {
            if(Step.Lesson==TrainingLesson.Stop && command.StopPressed)Complete=true;
            if(Step.Lesson==TrainingLesson.Detonate && command.SpecialPressed)
                foreach(var bomb in state.Bombs)if(bomb.ActorId==0 && state.Players[0].Energy>=10)detonationPressed=true;
        }
        public void ObserveEvent(MatchEvent value)
        {
            if(value.ActorId!=0)return;
            switch(Step.Lesson)
            {
                case TrainingLesson.Kick:Complete|=value.Kind==MatchEventKind.Kick;break;
                case TrainingLesson.Push:Complete|=value.Kind==MatchEventKind.Push;break;
                case TrainingLesson.Weapon:Complete|=value.Kind==MatchEventKind.Damage;break;
                case TrainingLesson.SkillQ:Complete|=value.Kind==MatchEventKind.Skill && value.Slot==SkillSlot.Q;break;
                case TrainingLesson.SkillE:case TrainingLesson.ThorSkill:Complete|=value.Kind==MatchEventKind.Skill && value.Slot==SkillSlot.E;break;
                case TrainingLesson.Detonate:Complete|=detonationPressed && (value.Kind==MatchEventKind.Explosion || value.Kind==MatchEventKind.Skill && value.Slot==SkillSlot.Special);break;
                case TrainingLesson.Respawn:Complete|=value.Kind==MatchEventKind.Respawn;break;
            }
        }
        public void Tick(MatchState state,float delta)
        {
            Age+=delta;var p=state.Players[0];
            if(Step.Lesson==TrainingLesson.Move)Complete|=Vec2.Distance(p.Position,Target)<.8f;
            if(Step.Lesson==TrainingLesson.Catch)Complete|=state.Ball.OwnerId==0;
            if(Step.Lesson==TrainingLesson.Recovery)Complete|=Age>2 && p.Health>74 && p.Energy>64;
            if(Step.Lesson==TrainingLesson.Rules)Complete|=Age>2;
        }
    }
    public sealed partial class AppController
    {
        public TutorialSession Tutorial { get; private set; }
        public void StartTutorial()
        {
            if(!EnsureEntered())return;
            CloseNetworking();localDraft=null;Tutorial=new TutorialSession();LoadTutorialStep();
        }
        private void LoadTutorialStep()
        {
            MovingTrainingTarget=false;ShowDebug=false;
            Selected=Tutorial.Step.Lesson==TrainingLesson.ThorSkill ? CharacterId.Thor : CharacterId.Marine;
            SelectedMode=MatchMode.Training;preparedLineup=null;preparedPositions=null;StartMatch();
            Simulation.SetupTrainingLesson(Tutorial.Step.Lesson);
            UI.Motion.Reset(Simulation.State.Players[0]);
            WorldCamera.GetComponent<ArenaCamera>().ResetFraming();View.ResetFeedback();View.Render(Simulation.State,Simulation.Rules,1,false,false);
            if(Tutorial.Step.Lesson==TrainingLesson.Respawn)foreach(var e in Simulation.State.Events){View.PlayEvent(e,Simulation.State);Audio.PlayEvent(e,Simulation.State);}
            UI.RefreshMatch();
        }
        public void NextTutorialStep(bool skip=false)
        {
            if(Tutorial==null || !skip && !Tutorial.Complete)return;
            if(Tutorial.Index==TutorialSession.Steps.Length-1) {GoHome();OpenPlayMenu();return;}
            Tutorial.SetStep(Tutorial.Index+1);LoadTutorialStep();
        }
        public void PreviousTutorialStep() {if(Tutorial==null)return;Tutorial.SetStep(Tutorial.Index-1);LoadTutorialStep();}
        public void RestartTutorialStep() {if(Tutorial==null)return;Tutorial.SetStep(Tutorial.Index);LoadTutorialStep();}
    }
}
