using Soccer.Core;
using System.Text.Json;

// No third-party test packages: the exact Core sources run outside the engine.
var results = new List<object>();
int failed = 0;
void Test(string name, Action test)
{
    try { test(); results.Add(new { name, passed = true, error = "" }); Console.WriteLine("PASS " + name); }
    catch (Exception e) { failed++; results.Add(new { name, passed = false, error = e.Message }); Console.WriteLine("FAIL " + name + ": " + e.Message); }
}
void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
void Near(float actual, float expected, float tolerance = 0.001f) => Assert(Math.Abs(actual - expected) <= tolerance, $"{actual} != {expected}");
CharacterStats[] Roster() => new[] {
    new CharacterStats { Id = CharacterId.WindShuttle, Name = "风梭", Speed = 5.6f, Radius = 0.43f, KickDistance = 0.95f },
    new CharacterStats { Id = CharacterId.IronGuard, Name = "铁卫", Speed = 4.3f, Radius = 0.64f, KickDistance = 0.9f, PushResistance = 0.2f, QCooldown = 9, ECooldown = 12, QWindup = 0.12f, QDuration = 0.8f, EDuration = 1.2f },
    new CharacterStats { Id = CharacterId.ArcLight, Name = "弧光", Speed = 4.7f, Radius = 0.48f, KickDistance = 1.15f, QCooldown = 8, ECooldown = 12 }
};
MatchSimulation Sim(CharacterId id = CharacterId.WindShuttle, MatchMode mode = MatchMode.Training, MatchRules rules = null)
{
    rules = LegacyField(rules);
    rules.EnableCombat = false; // Historical football regression fixtures. Combat has its own roster below.
    var sim = new MatchSimulation(rules, Roster(), id, mode);
    sim.State.Phase = MatchPhase.Playing;
    return sim;
}
// 保留旧的固定坐标回归场景；新地图在下方用真实默认尺寸独立验证。
MatchRules LegacyField(MatchRules rules=null)
{
    rules??=new MatchRules(); rules.MovementSpeedScale=1; rules.EnableOutOfPlay=false; rules.BallRadius=.18f; rules.FieldWidth=32; rules.FieldHeight=18; rules.CenterCircleRadius=2; return rules;
}
void Steps(MatchSimulation sim, int ticks, PlayerCommand input = default)
{
    var commands = new PlayerCommand[sim.State.Players.Count];
    for (int i = 0; i < ticks; i++) { commands[0] = i == 0 ? input : input.WithoutEdges(); sim.Step(commands); }
}
void IsolateBall(MatchSimulation sim)
{
    foreach (var p in sim.State.Players) p.Position = new Vec2(-10 + p.Id * 2, -7);
    sim.State.Ball.OwnerId = -1;
}

Test("diagonal movement is normalized", () => {
    var a = Sim(); var b = Sim();
    a.State.Players[0].Position = b.State.Players[0].Position = new Vec2(-12, -6);
    Steps(a, 60, new PlayerCommand { Move = new Vec2(1, 0) });
    Steps(b, 60, new PlayerCommand { Move = new Vec2(1, 1) });
    Near(Vec2.Distance(a.State.Players[0].Position, new Vec2(-12, -6)), Vec2.Distance(b.State.Players[0].Position, new Vec2(-12, -6)), 0.002f);
});
Test("opening warmup allows movement and facing without using match time", () => {
    var sim = new MatchSimulation(new MatchRules(), Roster(), CharacterId.WindShuttle, MatchMode.BotMatch);
    var origin = sim.State.Players[0].Position;
    Steps(sim, 60, new PlayerCommand { Move = new Vec2(-1, 0), Aim=new Vec2(0,1) });
    Near(sim.State.RemainingSeconds, 300); Near(sim.State.ElapsedSeconds,0);
    Near(Vec2.Distance(origin, sim.State.Players[0].Position),5.6f*sim.Rules.MovementSpeedScale,.003f);
    Near(Vec2.Distance(sim.State.Players[0].Facing,new Vec2(0,1)),0);
    Assert(sim.State.IsOpeningWarmup,"warmup ended prematurely");
});
Test("opening shield stops every body and keeps football free and stationary", () => {
    var sim=new MatchSimulation(new MatchRules(),Roster(),CharacterId.WindShuttle,MatchMode.BotMatch);
    var commands=new PlayerCommand[sim.State.Players.Count];
    for(int tick=0;tick<170;tick++)
    {
        foreach(var p in sim.State.Players) commands[p.Id]=new PlayerCommand {Move=(sim.State.Ball.Position-p.Position).Normalized};
        sim.Step(commands);
        foreach(var p in sim.State.Players)
            Assert(Vec2.Distance(p.Position,sim.State.Ball.Position)>=sim.Rules.OpeningShieldRadius+p.Stats.Radius-.001f,"body entered shield");
        Assert(sim.State.Ball.OwnerId==-1 && sim.State.Ball.Position.Length==0 && sim.State.Ball.Velocity.Length==0,"warmup changed football");
    }
});
Test("opening shield blocks swept high-speed crossings and permits sliding", () => {
    var roster=Roster(); roster[0].Speed=600;
    var sim=new MatchSimulation(new MatchRules {PhysicsSubsteps=1},roster,CharacterId.WindShuttle,MatchMode.Training);
    Steps(sim,1,new PlayerCommand {Move=new Vec2(1,0)});
    Assert(sim.State.Players[0].Position.X<0,"fast movement tunneled through dome");
    var p=sim.State.Players[0]; p.Stats.Speed=5.6f; var before=p.Position;
    Steps(sim,15,new PlayerCommand {Move=new Vec2(1,1)});
    Assert(p.Position.Y>before.Y+.2f && Vec2.Distance(p.Position,sim.State.Ball.Position)>=sim.Rules.OpeningShieldRadius+p.Stats.Radius-.001f,"shield trapped sliding movement");
});
Test("opening warmup discards combat and kick inputs without costs or queued actions", () => {
    var roster=new[]{StarStrikerLoadouts.Create(CharacterId.Marine),StarStrikerLoadouts.Create(CharacterId.Thor)};
    var sim=new MatchSimulation(new MatchRules(),roster,CharacterId.Marine,MatchMode.Training);
    var p=sim.State.Players[0];
    var input=new PlayerCommand {Move=new Vec2(0,1),Aim=new Vec2(1,0),QPressed=true,EPressed=true,SpecialPressed=true,
        TacklePressed=true,AttackPressed=true,AttackHeld=true,KickPressed=true,KickHeld=true,KickReleased=true};
    Steps(sim,181,input);
    Assert(sim.State.Phase==MatchPhase.Playing,"countdown did not end");
    Near(p.Health,100); Near(p.Energy,100); Near(p.QCooldown,0); Near(p.ECooldown,0); Near(p.TackleCooldown,0);
    Assert(p.Ammo==3 && !p.Charging && !p.AttackPending && sim.State.Projectiles.Count==0 && sim.State.Bombs.Count==0,"warmup performed an action");
    Steps(sim,1);
    Assert(!sim.State.Events.Any(e=>e.Kind==MatchEventKind.Skill || e.Kind==MatchEventKind.Weapon || e.Kind==MatchEventKind.Tackle),"queued warmup action leaked into play");
});
Test("opening shield releases at kickoff without resetting warmup position", () => {
    var sim=new MatchSimulation(new MatchRules(),Roster(),CharacterId.WindShuttle,MatchMode.Training);
    Steps(sim,170,new PlayerCommand {Move=new Vec2(1,0)}); var waiting=sim.State.Players[0].Position;
    Steps(sim,11);
    Assert(!sim.State.IsOpeningWarmup && sim.State.Phase==MatchPhase.Playing,"shield remained after countdown");
    Near(Vec2.Distance(waiting,sim.State.Players[0].Position),0);
    Steps(sim,60,new PlayerCommand {Move=new Vec2(1,0)});
    Assert(sim.State.Ball.OwnerId==0,"football remained inaccessible after kickoff");
});
Test("opening shield repairs body overlap without pushing players into protected area", () => {
    var sim=new MatchSimulation(new MatchRules(),Roster(),CharacterId.WindShuttle,MatchMode.Training);
    foreach(var p in sim.State.Players) p.Position=Vec2.Zero;
    Steps(sim,1);
    foreach(var p in sim.State.Players) Assert(p.Position.Length>=sim.Rules.OpeningShieldRadius+p.Stats.Radius-.001f,"body separation entered protected area");
    Assert(sim.State.Ball.OwnerId==-1,"overlapped spawn acquired football");
});
Test("opening warmup bots move but never request attacks or skills", () => {
    var sim=new MatchSimulation(new MatchRules(),Roster(),CharacterId.WindShuttle,MatchMode.BotMatch);
    var bot=new BotCommandSource(sim.Rules); var command=bot.ReadCommand(sim.State,1);
    Assert(command.Move.Length>0 && !command.AttackPressed && !command.KickPressed && !command.TacklePressed && !command.QPressed && !command.EPressed,"bot warmup command incorrect");
});
Test("self pickup locked after kick", () => {
    var sim = Sim(); sim.ResetTraining(true);
    Steps(sim, 1, new PlayerCommand { Aim = new Vec2(1, 0), KickPressed = true, KickReleased = true });
    Assert(sim.State.Ball.OwnerId == -1 && sim.State.Ball.SelfCatchRemaining > 0, "kicker reacquired immediately");
});
Test("kick distance uses square root coefficient", () => {
    var sim = Sim(CharacterId.ArcLight); sim.ResetTraining(true);
    sim.State.Players[0].Charging = true; sim.State.Players[0].ChargeTime = 0.85f;
    Steps(sim, 1, new PlayerCommand { Aim = new Vec2(1, 0), KickReleased = true });
    Near(sim.State.Ball.Velocity.Length, 18 * (float)Math.Sqrt(1.15) - 8f / 60, 0.01f);
});
Test("cancel precedes same-tick kick release", () => {
    var sim = Sim(); sim.ResetTraining(true);
    sim.State.Players[0].Charging = true; sim.State.Players[0].ChargeTime = 0.5f;
    Steps(sim, 1, new PlayerCommand { Cancel = true, KickReleased = true });
    Assert(sim.State.Ball.OwnerId == 0 && !sim.State.Players[0].Charging, "cancel released ball");
});
Test("input loss cancels charge and later release cannot shoot", () => {
    var sim = Sim(); sim.ResetTraining(true);
    Steps(sim, 15, new PlayerCommand { KickPressed = true, KickHeld = true });
    sim.CancelPendingInputs();
    Steps(sim, 1, new PlayerCommand { KickReleased = true });
    Assert(sim.State.Ball.OwnerId == 0 && !sim.State.Players[0].Charging, "pause leaked pending shot");
});
Test("no-ball held input does not charge on pickup", () => {
    var sim = Sim();
    Steps(sim, 1, new PlayerCommand { KickPressed = true, KickHeld = true });
    sim.State.Ball.Position = sim.State.Players[0].Position;
    Steps(sim, 2, new PlayerCommand { KickHeld = true });
    Assert(sim.State.Ball.OwnerId == 0 && !sim.State.Players[0].Charging, "holding without fresh press started charge");
});
Test("full charge auto releases", () => {
    var sim = Sim(); sim.ResetTraining(true);
    Steps(sim, 83, new PlayerCommand { Aim = new Vec2(1, 0), KickPressed = true, KickHeld = true });
    Assert(sim.State.Ball.OwnerId == -1 && sim.State.Players[0].Kicks == 1, "auto release absent");
});
foreach (float degrees in new[] { 44f, 45f, 46f, 90f, 180f })
Test($"rear carry contact angle {degrees} degrees", () => {
    var sim = Sim(); sim.GiveTrainingTargetBall();
    var a = sim.State.Players[0]; var target = sim.State.Players[1];
    target.Facing = new Vec2(1, 0); a.DashDirection = new Vec2(0, 1);
    var relative = Vec2.Rotate(target.Facing, degrees * (float)Math.PI / 180);
    var original = sim.State.Ball.Position;
    TackleSystem.Resolve(sim, a, target, relative);
    Assert(sim.State.Ball.OwnerId == (degrees <= 45 ? 1 : -1), "wrong rear carry/drop branch");
    Assert(target.PushRemaining > 0 && a.Steals == 0, "push did not displace or directly stole ball");
    if (degrees > 45) Near(Vec2.Distance(original, sim.State.Ball.Position), 0);
});
Test("push without body contact does not affect possession", () => {
    var sim = Sim(); sim.GiveTrainingTargetBall(); sim.State.Players[1].Position = new Vec2(8, 0);
    Steps(sim, 30, new PlayerCommand { Aim = new Vec2(1, 0), TacklePressed = true });
    Assert(sim.State.Ball.OwnerId == 1 && sim.State.Players[1].PushRemaining == 0, "remote push affected ball or target");
});
Test("swept push hits first body without automatic ball transfer", () => {
    var rules = new MatchRules { TackleDuration = 0.01f, TackleWindup = 0, PhysicsSubsteps = 1 };
    var sim = Sim(rules: rules); sim.GiveTrainingTargetBall(); sim.State.Players[1].Facing=new Vec2(1,0);
    Steps(sim, 1, new PlayerCommand { Aim = new Vec2(1, 0), TacklePressed = true });
    Assert(sim.State.Players[1].PushRemaining > 0 && sim.State.Ball.OwnerId == 1, "dash tunneled or rear push stole ball");
});
Test("full protection blocks displacement and ball release", () => {
    var sim = Sim(); sim.GiveTrainingTargetBall(); var a = sim.State.Players[0]; var b = sim.State.Players[1];
    b.FullProtection = 1; a.DashDirection = new Vec2(-1, 0);
    TackleSystem.Resolve(sim, a, b, new Vec2(-1, 0));
    Assert(sim.State.Ball.OwnerId == 1 && b.PushRemaining == 0, "full protection broken");
});
Test("controlled target is not displaced or disarmed by repeated push", () => {
    var sim = Sim(); sim.GiveTrainingTargetBall(); var a = sim.State.Players[0]; var b = sim.State.Players[1];
    b.ControlProtection = .2f; a.DashDirection = new Vec2(-1, 0);
    TackleSystem.Resolve(sim, a, b, new Vec2(-1, 0));
    Assert(sim.State.Ball.OwnerId == 1 && b.PushRemaining == 0, "control protection caused release or push");
});
Test("control protection cannot transfer possession to the pusher", () => {
    var sim = Sim(); sim.GiveTrainingTargetBall(); var a = sim.State.Players[0]; var b = sim.State.Players[1];
    b.ControlProtection = .6f; a.DashDirection = new Vec2(-1, 0);
    TackleSystem.Resolve(sim, a, b, new Vec2(-1, 0));
    Assert(sim.State.Ball.OwnerId == 1 && b.Stun == 0, "blocked displacement changed possession");
});
Test("push cancels charge and repeated push is suppressed", () => {
    var sim = Sim(); sim.GiveTrainingTargetBall(); var a = sim.State.Players[0]; var b = sim.State.Players[1];
    a.DashDirection = new Vec2(1, 0); b.Facing=new Vec2(1,0); b.Charging = true;
    TackleSystem.Resolve(sim, a, b, new Vec2(1, 0));
    Assert(!b.Charging && sim.State.Ball.OwnerId == 1, "rear push did not cancel charge/retain possession");
    var velocity = b.PushVelocity; a.DashDirection = new Vec2(-1, 0);
    TackleSystem.Resolve(sim, a, b, new Vec2(-1, 0));
    Near(Vec2.Distance(velocity, b.PushVelocity), 0);
});
Test("partial goal crossing does not score", () => {
    var sim = Sim(); IsolateBall(sim); sim.State.Ball.Position = new Vec2(16.1f, 0);
    Steps(sim, 1); Assert(sim.State.CyanScore == 0, "partially crossed ball scored");
});
Test("fast ball crosses goal once, round resets to losing side", () => {
    var sim = Sim(); IsolateBall(sim); sim.State.Ball.Position = new Vec2(16.05f, 0); sim.State.Ball.Velocity = new Vec2(24, 0);
    Steps(sim, 1); Assert(sim.State.CyanScore == 1, "missed goal");
    Steps(sim, 20); Assert(sim.State.CyanScore == 1, "goal counted twice");
    Steps(sim, 240); Assert(sim.State.Owner?.Team == Team.Amber, "wrong kickoff owner");
});
Test("swept goalpost blocks fast shot", () => {
    var sim = Sim(rules: new MatchRules { PhysicsSubsteps = 1 }); IsolateBall(sim);
    sim.State.Ball.Position = new Vec2(15.5f, 2.1f); sim.State.Ball.Velocity = new Vec2(24, 0);
    Steps(sim, 1); Assert(sim.State.CyanScore == 0 && sim.State.Ball.Velocity.X < 0, "shot passed through post");
});
Test("wall bounces outside goal opening", () => {
    var sim = Sim(); IsolateBall(sim); sim.State.Ball.Position = new Vec2(15.8f, 5); sim.State.Ball.Velocity = new Vec2(18, 0);
    Steps(sim, 2); Assert(sim.State.Ball.Velocity.X < 0 && sim.State.CyanScore == 0, "wall did not bounce");
});
Test("dribbling can score", () => {
    var sim = Sim(); sim.ResetTraining(true); sim.State.Players[0].Position = new Vec2(15.57f, 0);
    Steps(sim, 1, new PlayerCommand { Aim = new Vec2(1, 0) }); Assert(sim.State.CyanScore == 1, "held ball ignored goal");
});
Test("tied match has finite overtime", () => {
    var sim = Sim(mode: MatchMode.BotMatch, rules: new MatchRules { MatchSeconds = 0.03f, OvertimeSeconds = 0.03f });
    sim.State.Half=2; // Test the final-period boundary; half-time has independent coverage below.
    Steps(sim, 2); Assert(sim.State.Overtime && sim.State.Phase == MatchPhase.Playing, "no overtime");
    Steps(sim, 3); Assert(sim.State.Phase == MatchPhase.Finished, "overtime never ended");
});
Test("first half ends without finishing or granting overtime", () => {
    var sim=Sim(mode:MatchMode.BotMatch,rules:new MatchRules {MatchSeconds=2,RestartCountdown=.05f});
    Near(sim.State.PeriodRemainingSeconds(sim.Rules),1);
    Steps(sim,60);
    Assert(sim.State.Half==2 && sim.State.Phase==MatchPhase.Countdown && !sim.State.Overtime,"first half did not restart second half");
    Near(sim.State.RemainingSeconds,1,.0001f); Near(sim.State.ElapsedSeconds,1,.0001f);
    Near(sim.State.PeriodRemainingSeconds(sim.Rules),1,.0001f);
    Assert(sim.State.NextKickoff==Team.Amber && sim.State.Owner?.Team==Team.Amber,"second half did not give opponent kickoff");
    Steps(sim,1); Near(sim.State.RemainingSeconds,1,.0001f);
    Steps(sim,3); Assert(sim.State.Phase==MatchPhase.Playing,"half-time countdown never ended");
});
Test("half-time preserves score and five second in-place recovery", () => {
    var sim=CombatSim(mode:MatchMode.BotMatch,rules:new MatchRules {MatchSeconds=2});
    sim.State.RemainingSeconds=1.01f; sim.State.CyanScore=2; sim.State.AmberScore=1;
    var p=sim.State.Players[0]; p.Health=0; p.RespawnRemaining=4.8f; p.Position=new Vec2(-9,5);
    Steps(sim,1);
    Assert(sim.State.Half==2 && sim.State.CyanScore==2 && sim.State.AmberScore==1,"half-time changed score or finished match");
    Assert(p.IsDead && p.RespawnRemaining>4.78f,"half-time revived player early");
    Near(Vec2.Distance(p.Position,new Vec2(-9,5)),0);
});
Test("last instant first-half goal celebrates once before second half", () => {
    var sim=Sim(mode:MatchMode.BotMatch,rules:new MatchRules {MatchSeconds=2,GoalCelebration=.05f});
    IsolateBall(sim); sim.State.RemainingSeconds=1.001f;
    sim.State.Ball.Position=new Vec2(16.175f,0); sim.State.Ball.Velocity=new Vec2(24,0);
    Steps(sim,1);
    Assert(sim.State.CyanScore==1 && sim.State.HalfTimePending && sim.State.Phase==MatchPhase.GoalCelebration,"buzzer goal lost or celebration skipped");
    Steps(sim,5);
    Assert(sim.State.Half==2 && !sim.State.HalfTimePending && sim.State.Phase==MatchPhase.Countdown && sim.State.CyanScore==1,"goal restart bypassed half-time or counted twice");
    Near(sim.State.RemainingSeconds,1);
});
Test("overtime goal ends match immediately", () => {
    var sim = Sim(mode: MatchMode.BotMatch); sim.State.Overtime = true; IsolateBall(sim);
    sim.State.Ball.Position = new Vec2(16.05f, 0); sim.State.Ball.Velocity = new Vec2(24, 0);
    Steps(sim, 1); Assert(sim.State.Phase == MatchPhase.Finished && sim.State.CyanScore == 1, "golden goal did not finish");
});
Test("training utilities cannot mutate bot match", () => {
    var sim = Sim(mode: MatchMode.BotMatch); var position = sim.State.Players[0].Position;
    sim.ResetTraining(true); sim.GiveTrainingTargetBall();
    Assert(sim.State.Ball.OwnerId == -1 && Vec2.Distance(position, sim.State.Players[0].Position) == 0, "training cheat entered match");
});
Test("wind lane needs confirmation and cancel spends no cooldown", () => {
    var sim = Sim(); Steps(sim, 1, new PlayerCommand { EPressed = true });
    Assert(sim.State.Players[0].PreviewingE && sim.State.Effects.Count == 0, "lane bypassed preview");
    Steps(sim, 1, new PlayerCommand { Cancel = true, EReleased = true });
    Assert(sim.State.Effects.Count == 0 && sim.State.Players[0].ECooldown == 0, "cancel consumed E");
    Steps(sim, 1, new PlayerCommand { EPressed = true }); Steps(sim, 1, new PlayerCommand { EReleased = true });
    Assert(sim.State.Effects.Count == 1 && sim.State.Players[0].ECooldown > 0, "lane did not activate");
});
Test("arc preparation requires ball and replaces previous preparation", () => {
    var sim = Sim(CharacterId.ArcLight); Steps(sim, 1, new PlayerCommand { QPressed = true });
    Assert(sim.State.Players[0].QCooldown == 0, "no-ball Q spent cooldown");
    sim.ResetTraining(true); Steps(sim, 1, new PlayerCommand { QPressed = true }); Steps(sim, 1, new PlayerCommand { EPressed = true });
    Assert(sim.State.Players[0].Preparation == ShotPreparation.Power && sim.State.Players[0].QCooldown > 0, "preparations stacked/refunded cooldown");
});
Test("root reduces push distance but enemy front contact releases ball", () => {
    var sim = Sim(CharacterId.IronGuard); sim.ResetTraining(true); Steps(sim, 1, new PlayerCommand { EPressed = true });
    var owner = sim.State.Players[0]; var attacker = sim.State.Players[1]; attacker.DashDirection = new Vec2(-1, 0);
    TackleSystem.Resolve(sim, attacker, owner, new Vec2(-1, 0));
    Assert(owner.RootRemaining > 0 && sim.State.Ball.OwnerId == -1 && owner.PushRemaining>0, "root made ball immune");
});
Test("shield creation overlapping ball cannot award goal", () => {
    var sim = Sim(CharacterId.IronGuard); IsolateBall(sim);
    sim.State.Players[0].Position = new Vec2(15.2f, 0); sim.State.Players[0].Facing = new Vec2(1, 0);
    sim.State.Ball.Position = new Vec2(14.6f, 0);
    Steps(sim, 15, new PlayerCommand { Aim = new Vec2(1, 0), QPressed = true });
    Assert(sim.State.CyanScore == 0 && sim.State.Ball.Position.X < 16.18f, "shield generated goal");
});
Test("long bot run stays finite and inside field", () => {
    var sim = Sim(mode: MatchMode.BotMatch); var bots = sim.State.Players.Select(_ => new BotCommandSource(sim.Rules)).ToArray();
    for (int n = 0; n < 18000 && sim.State.Phase != MatchPhase.Finished; n++)
    {
        var commands = bots.Select((bot, i) => bot.ReadCommand(sim.State, i)).ToArray(); sim.Step(commands);
        foreach (var p in sim.State.Players)
        {
            Assert(float.IsFinite(p.Position.X) && float.IsFinite(p.Position.Y), "nonfinite player position");
            Assert(Math.Abs(p.Position.X) <= 16 - p.Stats.Radius + 0.001f && Math.Abs(p.Position.Y) <= 9 - p.Stats.Radius + 0.001f, "player escaped field");
        }
        Assert(float.IsFinite(sim.State.Ball.Position.X) && float.IsFinite(sim.State.Ball.Position.Y), "nonfinite ball");
        Assert(sim.State.Ball.OwnerId >= -1 && sim.State.Ball.OwnerId < 6, "illegal owner");
    }
});

Test("short recovery buffers skill exactly once", () => {
    var sim = Sim(CharacterId.IronGuard); var p = sim.State.Players[0]; p.ActionLock = 0.08f;
    Steps(sim, 1, new PlayerCommand { QPressed = true });
    Assert(sim.State.Effects.Count == 0, "skill bypassed recovery");
    Steps(sim, 10);
    Assert(sim.State.Effects.Count == 1 && p.QCooldown > 0, "buffer lost or repeated skill");
});
Test("buffer expires during long recovery", () => {
    var sim = Sim(CharacterId.IronGuard); sim.State.Players[0].ActionLock = 0.4f;
    Steps(sim, 35, new PlayerCommand { QPressed = true });
    Assert(sim.State.Effects.Count == 0 && sim.State.Players[0].QCooldown == 0, "expired skill fired late");
});
Test("tap during recovery preserves release and shoots once", () => {
    var sim = Sim(); sim.ResetTraining(true); var p = sim.State.Players[0]; p.ActionLock = 0.08f;
    Steps(sim, 1, new PlayerCommand { Aim = new Vec2(1, 0), KickPressed = true, KickHeld = true });
    Steps(sim, 1, new PlayerCommand { Aim = new Vec2(1, 0), KickReleased = true });
    Steps(sim, 12, new PlayerCommand { Aim = new Vec2(1, 0) });
    Assert(p.Kicks == 1 && !p.Charging, "buffer swallowed release or repeated shot");
});
Test("buffered wind preview preserves quick confirmation", () => {
    var sim = Sim(); sim.State.Players[0].ActionLock = 0.06f;
    Steps(sim, 1, new PlayerCommand { EPressed = true });
    Steps(sim, 1, new PlayerCommand { EReleased = true });
    Steps(sim, 8);
    Assert(sim.State.Effects.Count == 1 && !sim.State.Players[0].PreviewingE, "quick E confirmation lost");
});
Test("buffer accepts skill just before cooldown ends", () => {
    var sim = Sim(CharacterId.IronGuard); sim.State.Players[0].QCooldown = 0.07f;
    Steps(sim, 12, new PlayerCommand { QPressed = true });
    Assert(sim.State.Effects.Count == 1, "near-ready key lost");
});
Test("buffer does not wait for long cooldown", () => {
    var sim = Sim(CharacterId.IronGuard); sim.State.Players[0].QCooldown = 0.3f;
    Steps(sim, 1, new PlayerCommand { QPressed = true });
    Assert(sim.State.Events.Any(e => e.Kind == MatchEventKind.ActionRejected && e.Slot == SkillSlot.Q), "missing rejection");
    Steps(sim, 30);
    Assert(sim.State.Effects.Count == 0, "unavailable skill fired much later");
});
Test("cancel removes buffered skill and same-tick new press", () => {
    var sim = Sim(CharacterId.IronGuard); sim.State.Players[0].ActionLock = 0.07f;
    Steps(sim, 1, new PlayerCommand { QPressed = true });
    Steps(sim, 1, new PlayerCommand { Cancel = true, EPressed = true });
    Steps(sim, 12);
    Assert(sim.State.Effects.Count == 0 && sim.State.Players[0].RootRemaining == 0, "cancel leaked buffered action");
});
Test("pause clears buffered tackle", () => {
    var sim = Sim(); sim.State.Players[0].ActionLock = 0.07f;
    Steps(sim, 1, new PlayerCommand { TacklePressed = true }); sim.CancelPendingInputs();
    Steps(sim, 15);
    Assert(sim.State.Players[0].TackleCooldown == 0, "resume fired tackle");
});
Test("control interrupts pending skill", () => {
    var sim = Sim(CharacterId.IronGuard); var p = sim.State.Players[0]; p.ActionLock = 0.07f;
    Steps(sim, 1, new PlayerCommand { QPressed = true }); p.Stun = 0.08f;
    Steps(sim, 15);
    Assert(sim.State.Effects.Count == 0, "stun preserved queued attack");
});
Test("training reset clears buffered skill", () => {
    var sim = Sim(CharacterId.IronGuard); sim.State.Players[0].ActionLock = 0.07f;
    Steps(sim, 1, new PlayerCommand { QPressed = true }); sim.ResetTraining(false); Steps(sim, 12);
    Assert(sim.State.Effects.Count == 0, "reset leaked buffered skill");
});
Test("skill event identifies Q and E for each character", () => {
    foreach (var id in new[] { CharacterId.WindShuttle, CharacterId.IronGuard, CharacterId.ArcLight })
    {
        var sim = Sim(id); sim.ResetTraining(true);
        Steps(sim, 1, new PlayerCommand { QPressed = true });
        Assert(sim.State.Events.Any(e => e.Kind == MatchEventKind.Skill && e.Slot == SkillSlot.Q), "Q event lacks slot");
        sim.ResetTraining(true);
        Steps(sim, 1, new PlayerCommand { EPressed = true, EReleased = true });
        Assert(sim.State.Events.Any(e => e.Kind == MatchEventKind.Skill && e.Slot == SkillSlot.E), "E event lacks slot");
    }
});
Test("power kick event keeps preparation and charge intensity", () => {
    var sim = Sim(CharacterId.ArcLight); sim.ResetTraining(true);
    Steps(sim, 1, new PlayerCommand { EPressed = true });
    Steps(sim, 30, new PlayerCommand { KickPressed = true, KickHeld = true });
    Steps(sim, 1, new PlayerCommand { KickReleased = true });
    var kick = sim.State.Events.Single(e => e.Kind == MatchEventKind.Kick);
    Assert(kick.Shot == ShotPreparation.Power && kick.Strength > 0.45f && kick.Strength < 0.6f, "event lost power or intensity");
});
Test("fast wall contact emits bounce once with position", () => {
    var sim = Sim(); IsolateBall(sim); sim.State.Ball.Position = new Vec2(15.8f, 5); sim.State.Ball.Velocity = new Vec2(18, 0);
    Steps(sim, 1);
    var bounce = sim.State.Events.Single(e => e.Kind == MatchEventKind.Bounce);
    Assert(bounce.Strength > 0.5f && bounce.Position.X > 15, "bounce lacks impact data");
});

Test("wind preview release survives the end of a slide", () => {
    var sim = Sim(); var p = sim.State.Players[0];
    Steps(sim, 1, new PlayerCommand { EPressed = true });
    p.Dash = DashKind.Slide; p.DashDirection = new Vec2(1, 0); p.DashRemaining = 0.06f; p.DashSpeed = 1;
    Steps(sim, 1, new PlayerCommand { EReleased = true }); Steps(sim, 10);
    Assert(sim.State.Effects.Count == 1 && !p.PreviewingE, "release at end of slide got lost");
});
Test("expired wind confirmation clears preview instead of firing late", () => {
    var sim = Sim(); var p = sim.State.Players[0]; Steps(sim, 1, new PlayerCommand { EPressed = true });
    p.Dash = DashKind.Slide; p.DashDirection = new Vec2(1, 0); p.DashRemaining = 0.4f; p.DashSpeed = 1;
    Steps(sim, 1, new PlayerCommand { EReleased = true }); Steps(sim, 35);
    Assert(sim.State.Effects.Count == 0 && !p.PreviewingE, "expired release left sticky preview");
});
Test("losing possession clears buffered shot before reacquisition", () => {
    var sim = Sim(); sim.ResetTraining(true); var p = sim.State.Players[0]; p.ActionLock = 0.06f;
    Steps(sim, 1, new PlayerCommand { KickPressed = true, KickReleased = true });
    sim.State.Ball.OwnerId = 1; Steps(sim, 1); sim.State.Ball.OwnerId = 0; Steps(sim, 12);
    Assert(p.Kicks == 0 && !p.Charging, "reacquisition fired obsolete shot");
});
Test("buffered action uses latest aim at execution", () => {
    var sim = Sim(); sim.State.Players[0].ActionLock = 0.07f;
    Steps(sim, 1, new PlayerCommand { TacklePressed = true, Aim = new Vec2(1, 0) });
    Steps(sim, 6, new PlayerCommand { Aim = new Vec2(0, 1) });
    Near(sim.State.Players[0].DashDirection.Y, 1);
});

// Combat fixtures use interchangeable equipment, with a shared body and full resources.
MatchSimulation CombatSim(CharacterId slot = CharacterId.WindShuttle, MatchMode mode = MatchMode.Training, MatchRules rules = null)
{
    var sim = new MatchSimulation(LegacyField(rules), new[] {
        PrototypeLoadouts.Create(CharacterId.WindShuttle), PrototypeLoadouts.Create(CharacterId.IronGuard),
        PrototypeLoadouts.Create(CharacterId.ArcLight) }, slot, mode);
    sim.State.Phase = MatchPhase.Playing;
    foreach(var p in sim.State.Players) p.Stats.HealthRegen=0;
    return sim;
}
void CombatPair(MatchSimulation sim, float distance = 1.5f)
{
    foreach (var p in sim.State.Players) p.Position = p.PreviousPosition = new Vec2(-12 + p.Id * 2, -7);
    sim.State.Players[0].Position = sim.State.Players[0].PreviousPosition = Vec2.Zero;
    var enemy = sim.State.Players.First(p => p.Team == Team.Amber);
    enemy.Position = enemy.PreviousPosition = new Vec2(distance, 0);
    sim.State.Ball.OwnerId = -1; sim.State.Ball.Position = new Vec2(10, 6);
}
Test("combat kits share neutral body and full initial resources", () => {
    var sim = CombatSim(mode: MatchMode.BotMatch);
    foreach (var p in sim.State.Players) { Near(p.Health, 100); Near(p.Energy, 100); Near(p.Stats.Speed, 5); Near(p.Stats.Radius, 0.5f); }
});
Test("one hammer press has windup and consumes one stock without energy cost", () => {
    var sim = CombatSim(); CombatPair(sim); var target = sim.State.Players[1];
    Steps(sim, 1, new PlayerCommand { AttackPressed = true, Aim = new Vec2(1, 0) });
    Near(target.Health, 100);
    Steps(sim, 12); Near(target.Health, 72); Near(sim.State.Players[0].Energy, 100);
    Steps(sim, 25); Near(target.Health, 72); Assert(sim.State.Players[0].Ammo == 2, "one click repeated the attack");
});
Test("melee misses behind and beyond reach", () => {
    foreach (float distance in new[] { -1.5f, 3f }) {
        var sim = CombatSim(); CombatPair(sim, distance);
        Steps(sim, 20, new PlayerCommand { AttackPressed = true, Aim = new Vec2(1, 0) }); Near(sim.State.Players[1].Health, 100);
    }
});
Test("held weapon obeys cooldown and can knock out", () => {
    var sim = CombatSim(); CombatPair(sim);
    Steps(sim, 150, new PlayerCommand { AttackHeld = true, Aim = new Vec2(1, 0) });
    Assert(sim.State.Players[1].IsDead && sim.State.Players[0].Knockouts == 1, "held attack skipped combat or repeated knockout");
    Assert(sim.State.CyanScore == 0 && sim.State.AmberScore == 0, "knockout awarded a goal");
});
Test("weapon and stun projectile never damage teammates", () => {
    var sim = CombatSim(mode: MatchMode.BotMatch); CombatPair(sim);
    var ally = sim.State.Players[1]; ally.Position = new Vec2(1.2f, 0);
    Steps(sim, 15, new PlayerCommand { AttackPressed = true, Aim = new Vec2(1, 0) }); Near(ally.Health, 100);
    Steps(sim, 30, new PlayerCommand { QPressed = true, Aim = new Vec2(1, 0) }); Near(ally.Health, 100); Near(ally.Stun, 0);
});
Test("ranged projectile hits first enemy at high speed without tunneling", () => {
    var sim = CombatSim(CharacterId.IronGuard, MatchMode.BotMatch, new MatchRules { PhysicsSubsteps = 1 }); CombatPair(sim, 2);
    sim.State.Players[0].Stats.Loadout.WeaponSpeed = 600;
    sim.State.Players[4].Position = new Vec2(4, 0);
    Steps(sim, 12, new PlayerCommand { AttackPressed = true, Aim = new Vec2(1, 0) });
    Near(sim.State.Players[3].Health, 70); Near(sim.State.Players[4].Health, 100);
});
Test("ranged projectile can miss and disappears at its range or wall", () => {
    var sim = CombatSim(CharacterId.IronGuard); CombatPair(sim, 4);
    sim.State.Players[1].Position = new Vec2(4, 2);
    Steps(sim, 60, new PlayerCommand { AttackPressed = true, Aim = new Vec2(1, 0) });
    Near(sim.State.Players[1].Health, 100); Assert(sim.State.Projectiles.Count == 0, "projectile did not expire");
    sim.State.Players[0].Position = new Vec2(15.4f, 6);
    Steps(sim, 60, new PlayerCommand { QPressed = true, Aim = new Vec2(1, 0) }); Assert(sim.State.Projectiles.Count == 0, "wall let projectile escape");
});
Test("Q works without possession, consumes energy and applies stun", () => {
    var sim = CombatSim(); CombatPair(sim, 3);
    Steps(sim, 1, new PlayerCommand { QPressed = true, Aim = new Vec2(1, 0) });
    Near(sim.State.Players[0].Energy, 70); Assert(sim.State.Players[0].QCooldown > 0 && sim.State.Projectiles.Count == 1, "Q did not launch");
    Steps(sim, 15); Near(sim.State.Players[1].Health, 80); Assert(sim.State.Players[1].Stun > 0, "Q missed stun");
});
Test("insufficient energy rejects skills without cooldown or effects", () => {
    var sim = CombatSim(); CombatPair(sim); var p = sim.State.Players[0]; p.Energy = 1;
    Steps(sim, 1, new PlayerCommand { QPressed = true });
    Assert(p.QCooldown == 0 && sim.State.Projectiles.Count == 0 && sim.State.Events.Any(e => e.Kind == MatchEventKind.ActionRejected && e.Slot == SkillSlot.Q), "Q spent unavailable energy");
    Steps(sim, 1, new PlayerCommand { EPressed = true }); Near(p.ECooldown, 0); Near(p.ShieldHealth, 0);
});
Test("energy regeneration observes spend delay and clamps at maximum", () => {
    var sim = CombatSim(); var p = sim.State.Players[0];
    Steps(sim, 1, new PlayerCommand { EPressed = true }); Near(p.Energy, 65);
    Steps(sim, 74); Near(p.Energy, 65);
    Steps(sim, 61); Near(p.Energy, 77, 0.01f);
    Steps(sim, 300); Near(p.Energy, 100);
});
Test("stun interrupts attacks and charge and cannot chain during protection", () => {
    var sim = CombatSim(); CombatPair(sim); var a = sim.State.Players[0]; var b = sim.State.Players[1];
    b.Charging = b.AttackPending = true; b.Dash = DashKind.Slide;
    var combat = new CombatSystem(sim); combat.ApplyHit(a, b, 10, 0.8f, SkillSlot.Q);
    Assert(!b.Charging && !b.AttackPending && b.Dash == DashKind.None, "stun preserved an action");
    b.Stun = 0.2f; combat.ApplyHit(a, b, 10, 0.8f, SkillSlot.Q); Near(b.Stun, 0.2f); Near(b.Health, 80);
});
Test("death drops ball and fully disables movement, combat and body contact", () => {
    var sim = CombatSim(); CombatPair(sim); var a = sim.State.Players[0]; var b = sim.State.Players[1];
    sim.State.Ball.OwnerId = b.Id; b.Health = 10; b.Charging = b.AttackPending = true;
    new CombatSystem(sim).ApplyHit(a, b, 20, 1, SkillSlot.Q);
    var position = b.Position; Assert(sim.State.Ball.OwnerId == -1 && b.IsDead && !b.Charging && !b.AttackPending, "death failed to detach ball/actions");
    var commands = new PlayerCommand[2]; commands[1] = new PlayerCommand { Move = new Vec2(1, 1), AttackHeld = true, QPressed = true, EPressed = true, TacklePressed = true };
    sim.Step(commands); Near(Vec2.Distance(b.Position, position), 0); Near(a.Health, 100);
    sim.State.Ball.Position = b.Position; sim.State.Ball.Velocity = Vec2.Zero; Steps(sim, 1);
    Assert(sim.State.Ball.OwnerId != b.Id, "dead body caught ball");
    a.Position = b.Position; Steps(sim, 1); Near(Vec2.Distance(b.Position, position), 0);
});
Test("exactly five seconds restores resources and cooldowns at death position", () => {
    var sim = CombatSim(); CombatPair(sim); var p = sim.State.Players[0]; var enemy = sim.State.Players[1];
    p.Position = new Vec2(-4, 2); p.Health = 1; p.QCooldown = 4; p.Energy = 2;
    new CombatSystem(sim).ApplyHit(enemy, p, 10, 0, SkillSlot.Weapon); var position = p.Position;
    Steps(sim, 299); Assert(p.IsDead, "revived before five seconds");
    Steps(sim, 1); Assert(!p.IsDead && p.RespawnedThisTick, "did not revive after 300 ticks");
    Near(Vec2.Distance(p.Position, position), 0); Near(p.Health, 100); Near(p.Energy, 100);
    Near(p.QCooldown, 0); Near(p.ECooldown, 0); Near(p.WeaponCooldown, 0); Near(p.Stun, 0); Near(p.FullProtection, 1);
    Assert(p.Ammo == p.Stats.Loadout.AmmoCapacity && p.AmmoReloadRemaining == 0, "revive did not refill attack stock");
    Assert(p.Deaths == 1 && enemy.Knockouts == 1, "respawn lost match statistics");
});
Test("respawn protection blocks damage and ends on active attack", () => {
    var sim = CombatSim(); CombatPair(sim); var p = sim.State.Players[0]; var enemy = sim.State.Players[1]; var combat = new CombatSystem(sim);
    combat.ApplyHit(enemy, p, 100, 0, SkillSlot.Weapon); Steps(sim, 300);
    combat.ApplyHit(enemy, p, 20, 1, SkillSlot.Q); Near(p.Health, 100); Near(p.Stun, 0);
    Steps(sim, 1, new PlayerCommand { AttackPressed = true, Aim = new Vec2(1, 0) }); Near(p.FullProtection, 0);
    combat.ApplyHit(enemy, p, 20, 0, SkillSlot.Weapon); Near(p.Health, 80);
});
Test("goal restart preserves death location and full five-second timer", () => {
    var sim = CombatSim(mode: MatchMode.BotMatch); CombatPair(sim); var p = sim.State.Players[3]; p.Position = new Vec2(6, 2);
    new CombatSystem(sim).ApplyHit(sim.State.Players[0], p, 100, 0, SkillSlot.Weapon); var position = p.Position;
    sim.Score(Team.Cyan); Steps(sim, 245);
    Assert(p.IsDead && sim.State.Phase == MatchPhase.Playing, "goal revived dead player early");
    Near(Vec2.Distance(p.Position, position), 0);
    Steps(sim, 55); Assert(!p.IsDead, "goal transition delayed respawn"); Near(Vec2.Distance(p.Position, position), 0);
});
Test("all-down kickoff uses a free ball without restoring dead players", () => {
    var sim = CombatSim(mode: MatchMode.BotMatch); var a = sim.State.Players[0];
    foreach (var p in sim.State.Players.Where(p => p.Team == Team.Amber)) new CombatSystem(sim).ApplyHit(a, p, 100, 0, SkillSlot.Weapon);
    sim.Score(Team.Cyan); Steps(sim, 125); Assert(sim.State.Owner == null && sim.State.Players.Where(p => p.Team == Team.Amber).All(p => p.IsDead), "restart assigned ball to corpse");
});
Test("pause cancellation preserves damage, energy and death timer", () => {
    var sim = CombatSim(); var p = sim.State.Players[0]; p.Health = 40; p.Energy = 23; p.AttackPending = true;
    sim.CancelPendingInputs(); Near(p.Health, 40); Near(p.Energy, 23); Assert(!p.AttackPending, "pause leaked weapon windup");
    new CombatSystem(sim).ApplyHit(sim.State.Players[1], p, 100, 0, SkillSlot.Weapon);
    Steps(sim, 50); float timer = p.RespawnRemaining; sim.CancelPendingInputs(); Near(p.RespawnRemaining, timer);
});
Test("team shield absorbs damage and applies only in friendly range", () => {
    var sim = CombatSim(mode: MatchMode.BotMatch); CombatPair(sim, 2);
    var ally = sim.State.Players[1]; ally.Position = new Vec2(0, 2);
    sim.State.Players[2].Position = new Vec2(-8, 4);
    Steps(sim, 1, new PlayerCommand { EPressed = true }); Near(ally.ShieldHealth, 35); Near(sim.State.Players[2].ShieldHealth, 0); Near(sim.State.Players[3].ShieldHealth, 0);
    var combat = new CombatSystem(sim); combat.ApplyHit(sim.State.Players[3], ally, 28, 0, SkillSlot.Weapon);
    Near(ally.Health, 100); Near(ally.ShieldHealth, 7);
    combat.ApplyHit(sim.State.Players[3], ally, 20, 0, SkillSlot.Weapon); Near(ally.Health, 87); Near(ally.ShieldHealth, 0);
});
Test("team shield expires and never grants permanent immunity", () => {
    var sim = CombatSim(); Steps(sim, 1, new PlayerCommand { EPressed = true }); Steps(sim, 121);
    Near(sim.State.Players[0].ShieldHealth, 0);
});
Test("healing pulse restores alive teammates without reviving or overhealing", () => {
    var sim = CombatSim(CharacterId.ArcLight, MatchMode.BotMatch); var p = sim.State.Players[0]; p.Position = Vec2.Zero; p.Health = 90;
    var ally = sim.State.Players[1]; ally.Position = new Vec2(0, 2); ally.Health = 40;
    var dead = sim.State.Players[2]; dead.Position = new Vec2(0, -2); dead.Health = 0; dead.RespawnRemaining = 4;
    var enemy = sim.State.Players[3]; enemy.Position = new Vec2(2, 0); enemy.Health = 50;
    Steps(sim, 1, new PlayerCommand { EPressed = true }); Near(p.Health, 100); Near(ally.Health, 70); Near(enemy.Health, 50);
    Assert(dead.IsDead, "heal revived dead teammate"); Near(p.HealingDone, 40);
});
Test("team boost affects nearby allies and expires", () => {
    var sim = CombatSim(CharacterId.IronGuard, MatchMode.BotMatch); var p = sim.State.Players[0]; p.Position = new Vec2(-8, -3);
    sim.State.Players[1].Position = new Vec2(-8, -1); sim.State.Players[2].Position = new Vec2(8, 4);
    Steps(sim, 1, new PlayerCommand { EPressed = true }); Assert(sim.State.Players[1].BoostRemaining > 0, "ally boost missing");
    Near(sim.State.Players[2].BoostRemaining, 0);
    Steps(sim, 10); var origin = p.Position; Steps(sim, 60, new PlayerCommand { Move = new Vec2(1, 0) }); Near(Vec2.Distance(p.Position, origin), 6, 0.01f);
    Steps(sim, 60); Near(p.BoostRemaining, 0);
});
Test("training reset clears combat projectiles and restores full state", () => {
    var sim = CombatSim(); Steps(sim, 1, new PlayerCommand { QPressed = true });
    sim.State.Players[0].Health = 20; sim.ResetTraining(true);
    Near(sim.State.Players[0].Health, 100); Near(sim.State.Players[0].Energy, 100); Assert(sim.State.Projectiles.Count == 0, "reset retained projectiles");
});
Test("invalid combat stats fail before entering a match", () => {
    foreach (float value in new[] { 0, -1, float.NaN, float.PositiveInfinity }) {
        bool rejected = false; var roster = new[] { PrototypeLoadouts.Create(CharacterId.WindShuttle), PrototypeLoadouts.Create(CharacterId.IronGuard), PrototypeLoadouts.Create(CharacterId.ArcLight) }; roster[0].MaxHealth = value;
        try { _ = new MatchSimulation(new MatchRules(), roster, CharacterId.WindShuttle, MatchMode.Training); } catch (ArgumentException) { rejected = true; }
        Assert(rejected, "invalid health accepted");
    }
});
Test("three-v-three combat bots fight, support, pass and complete finite matches", () => {
    var sim = CombatSim(mode: MatchMode.BotMatch, rules: new MatchRules { MatchSeconds = 90, OvertimeSeconds = 10 });
    var bots = sim.State.Players.Select(_ => new BotCommandSource(sim.Rules)).ToArray(); int hits = 0, support = 0, knockouts = 0;
    for (int n = 0; n < 18000 && sim.State.Phase != MatchPhase.Finished; n++) {
        sim.Step(bots.Select((bot, i) => bot.ReadCommand(sim.State, i)).ToArray());
        hits += sim.State.Events.Count(e => e.Kind == MatchEventKind.Damage); support += sim.State.Events.Count(e => e.Kind == MatchEventKind.Support);
        knockouts += sim.State.Events.Count(e => e.Kind == MatchEventKind.Knockout);
        foreach (var p in sim.State.Players) {
            Assert(float.IsFinite(p.Position.X) && float.IsFinite(p.Position.Y) && Math.Abs(p.Position.X) <= 16 - p.Stats.Radius + 0.001f && Math.Abs(p.Position.Y) <= 9 - p.Stats.Radius + 0.001f, "combat escaped field");
            Assert(p.Health >= 0 && p.Health <= p.Stats.MaxHealth && p.Energy >= 0 && p.Energy <= p.Stats.MaxEnergy, "resources exceeded bounds");
        }
        Assert(sim.State.Owner == null || !sim.State.Owner.IsDead, "dead owner in match"); Assert(sim.State.Projectiles.Count <= 30, "unbounded projectiles");
    }
    Assert(sim.State.Phase == MatchPhase.Finished && hits > 10 && support > 5 && knockouts > 0 && sim.State.Players.Sum(p => p.Passes) > 0, $"bots lack play: hits={hits}, support={support}, KO={knockouts}, passes={sim.State.Players.Sum(p => p.Passes)}, phase={sim.State.Phase}");
});

Test("stunned bodies still block a ball but cannot catch it", () => {
    var sim = CombatSim(); CombatPair(sim, 2); sim.State.Players[1].Stun = 1;
    sim.State.Ball.Position = new Vec2(0.8f, 0); sim.State.Ball.Velocity = new Vec2(9, 0);
    Steps(sim, 5);
    Assert(sim.State.Ball.OwnerId == -1 && sim.State.Ball.Velocity.X < 0, "stunned body swallowed or ignored ball");
});
Test("protected first target blocks a projectile without damage or stun", () => {
    var sim = CombatSim(mode: MatchMode.BotMatch); CombatPair(sim, 2);
    sim.State.Players[3].FullProtection = 1; sim.State.Players[4].Position = new Vec2(4, 0);
    Steps(sim, 30, new PlayerCommand { QPressed = true, Aim = new Vec2(1, 0) });
    Near(sim.State.Players[3].Health, 100); Near(sim.State.Players[3].Stun, 0); Near(sim.State.Players[4].Health, 100);
    Assert(sim.State.Projectiles.Count == 0, "projectile pierced protection");
});
Test("combat and same-tick kick release cannot fire both actions", () => {
    var sim = CombatSim(); sim.ResetTraining(true); var p = sim.State.Players[0]; p.Charging = true; p.ChargeTime = 0.5f;
    Steps(sim, 1, new PlayerCommand { AttackPressed = true, KickReleased = true, Aim = new Vec2(1, 0) });
    Assert(sim.State.Ball.OwnerId == 0 && p.Kicks == 0 && !p.Charging, "attack let shot escape");
});
Test("relay and team boost use highest bonus with a global cap", () => {
    var sim = CombatSim(); var p = sim.State.Players[0]; p.Position = new Vec2(-12, -6);
    p.BoostRemaining = 2; p.BoostStrength = 0.2f; p.RelayRemaining = 2;
    Steps(sim, 60, new PlayerCommand { Move = new Vec2(1, 0) }); Near(p.Position.X, -6, 0.01f);
    p.Position = new Vec2(-12, -6); p.BoostRemaining = 2; p.BoostStrength = 0.8f;
    Steps(sim, 60, new PlayerCommand { Move = new Vec2(1, 0) }); Near(p.Position.X, -5.75f, 0.01f);
});

Test("combat strafing keeps full speed through weapon and skill recovery", () => {
    foreach (bool skill in new[] { false, true }) {
        var sim = CombatSim(); var p = sim.State.Players[0]; p.Position = new Vec2(-12, -6);
        Steps(sim, 60, new PlayerCommand { Move = new Vec2(1, 0), Aim = new Vec2(0, 1), AttackHeld = !skill, QPressed = skill });
        Near(p.Position.X, -7, 0.002f); Near(p.Position.Y, -6);
    }
});
Test("three stock burst has an empty window and reloads one stock at a time", () => {
    var sim = CombatSim(); var p = sim.State.Players[0];
    Steps(sim, 45, new PlayerCommand { AttackHeld = true, Aim = new Vec2(0, 1) });
    Assert(p.Ammo == 0, "three attacks did not spend all stock"); Near(p.Energy, 100);
    int beforeReload = (int)Math.Ceiling(p.AmmoReloadRemaining / MatchSimulation.TickSeconds) - 1;
    Steps(sim, beforeReload); Assert(p.Ammo == 0, "stock regenerated early");
    Steps(sim, 1); Assert(p.Ammo == 1, "first reload refilled the whole burst");
    Steps(sim, 69); Assert(p.Ammo == 2, "second stock did not regenerate independently");
    Steps(sim, 69); Assert(p.Ammo == 3 && p.AmmoReloadRemaining == 0, "stock exceeded capacity or retained timer");
});
Test("a new attack restarts the reload wait without spending energy", () => {
    var sim = CombatSim(); var p = sim.State.Players[0];
    Steps(sim, 1, new PlayerCommand { AttackPressed = true }); Steps(sim, 40);
    Assert(p.AmmoReloadRemaining < 0.6f, "reload did not advance");
    Steps(sim, 1, new PlayerCommand { AttackPressed = true });
    Near(p.AmmoReloadRemaining, p.Stats.Loadout.AmmoReloadSeconds); Assert(p.Ammo == 1, "new attack failed to spend stock");
    Near(p.Energy, 100);
});
Test("short weapon buffer uses current aim and fires only once", () => {
    var sim = CombatSim(); var p = sim.State.Players[0]; p.WeaponCooldown = 0.08f;
    Steps(sim, 1, new PlayerCommand { AttackPressed = true, Aim = new Vec2(1, 0) });
    Assert(p.Ammo == 3, "buffer fired before weapon was ready");
    Steps(sim, 20, new PlayerCommand { Aim = new Vec2(0, 1) });
    Assert(p.Ammo == 2, "buffer lost or repeated attack"); Near(p.AttackFacing.Y, 1);
});
Test("empty stock and long cooldown presses never replay later", () => {
    foreach (bool empty in new[] { false, true }) {
        var sim = CombatSim(); var p = sim.State.Players[0];
        if (empty) { p.Ammo = 0; p.AmmoReloadRemaining = 0.05f; } else p.WeaponCooldown = 0.5f;
        Steps(sim, 1, new PlayerCommand { AttackPressed = true }); Steps(sim, 100);
        Assert(p.Ammo == (empty ? 2 : 3) && p.WeaponVisual == 0, "old click fired after long wait");
    }
});
Test("cancel pause and stun remove a buffered weapon click", () => {
    foreach (int interrupt in new[] { 0, 1, 2 }) {
        var sim = CombatSim(); var p = sim.State.Players[0]; p.WeaponCooldown = 0.08f;
        Steps(sim, 1, new PlayerCommand { AttackPressed = true });
        if (interrupt == 0) Steps(sim, 1, new PlayerCommand { Cancel = true });
        else if (interrupt == 1) sim.CancelPendingInputs();
        else new CombatSystem(sim).ApplyHit(sim.State.Players[1], p, 1, 0.05f, SkillSlot.Q);
        Steps(sim, 30); Assert(p.Ammo == 3 && !p.AttackPending, "interrupted buffer fired");
    }
});
Test("dragon control wave rebounds once within its total range", () => {
    var sim = CombatSim(); CombatPair(sim); var p = sim.State.Players[0]; var target = sim.State.Players[1];
    p.Position = new Vec2(14, 0); target.Position = new Vec2(11.5f, 0);
    Steps(sim, 40, new PlayerCommand { QPressed = true, Aim = new Vec2(1, 0) });
    Near(target.Health, 80); Assert(target.Stun > 0 && sim.State.Projectiles.Count == 0, "rebound failed to hit behind caster");
    sim.ResetTraining(false); p.Position = new Vec2(14, 5); target.Position = new Vec2(-10, -5);
    Steps(sim, 100, new PlayerCommand { QPressed = true, Aim = new Vec2(1, 0) });
    Assert(sim.State.Projectiles.Count == 0, "rebound extended total travel indefinitely");
});
Test("rice control spreads three projectiles without chaining stun", () => {
    var sim = CombatSim(CharacterId.ArcLight); CombatPair(sim); var target = sim.State.Players[1];
    Steps(sim, 1, new PlayerCommand { QPressed = true, Aim = new Vec2(1, 0) });
    Assert(sim.State.Projectiles.Count == 3 && sim.State.Projectiles[0].Direction.Y < 0 && sim.State.Projectiles[2].Direction.Y > 0,
        "rice volley did not spread");
    Steps(sim, 10); Near(target.Health, 82); Assert(target.Stun > 0 && target.Stun <= 0.55f, "pellets extended stun");
});
Test("rain scatter rewards close range while rice weapon retains long reach", () => {
    var rain = CombatSim(CharacterId.IronGuard); CombatPair(rain, 2);
    Steps(rain, 15, new PlayerCommand { AttackPressed = true, Aim = new Vec2(1, 0) }); Near(rain.State.Players[1].Health, 70);
    var rice = CombatSim(CharacterId.ArcLight); CombatPair(rice, 6);
    Steps(rice, 30, new PlayerCommand { AttackPressed = true, Aim = new Vec2(1, 0) }); Near(rice.State.Players[1].Health, 82);
});


Test("ordinary kicks always stay on the plane", () => {
    var sim=Sim(); sim.ResetTraining(true);
    Steps(sim,1,new PlayerCommand { Aim=new Vec2(1,0),KickPressed=true,KickReleased=true });
    Steps(sim,30);
    Assert(sim.State.Ball.FlightMode==BallFlightMode.Ground && !sim.State.Ball.Airborne,"ordinary input enabled height");
    Near(sim.State.Ball.Height,0); Near(sim.State.Ball.VerticalVelocity,0);
});
Test("unmarked height values cannot alter ordinary reception", () => {
    var sim=Sim(); IsolateBall(sim); var b=sim.State.Ball;
    sim.State.Players[0].Position=b.Position=Vec2.Zero; b.Height=b.PreviousHeight=100; b.VerticalVelocity=8;
    Steps(sim,1); Assert(b.OwnerId==0,"numeric height changed planar pickup without a skill");
    Near(b.Height,0); Near(b.PreviousHeight,0); Near(b.VerticalVelocity,0);
});
Test("ordinary goal ignores unused height values", () => {
    var sim=Sim(); IsolateBall(sim); var b=sim.State.Ball;
    b.Position=new Vec2(16.05f,0); b.Velocity=new Vec2(24,0); b.Height=100;
    Steps(sim,1); Assert(sim.State.CyanScore==1,"ordinary goal considered vertical clearance");
});
Test("only explicit skill launch enables an overhead trajectory", () => {
    var sim=Sim(); sim.ResetTraining(true); var p=sim.State.Players[0];
    p.Position=new Vec2(-7,0); sim.State.Players[1].Position=new Vec2(-2.5f,0);
    Assert(sim.TryLaunchOverheadSkill(0,new Vec2(13,0),11),"valid held-ball skill failed");
    Assert(sim.State.Ball.FlightMode==BallFlightMode.SkillOverhead,"skill mode was not marked");
    Steps(sim,20); var b=sim.State.Ball;
    Assert(b.Position.X>sim.State.Players[1].Position.X && b.Height>sim.Rules.SkillPlayerClearHeight && b.OwnerId==-1 && b.Velocity.X>0,
        "skill ball did not clear the player");
});
Test("skill high ball still scores by the planar goal mouth", () => {
    foreach (float height in new[] { 1.82f,3,10 }) {
        var sim=Sim(); IsolateBall(sim); var b=sim.State.Ball;
        b.FlightMode=BallFlightMode.SkillOverhead; b.Height=height; b.Position=new Vec2(15.7f,0); b.Velocity=new Vec2(18,0);
        Steps(sim,3); Assert(sim.State.CyanScore==1,"skill goal was rejected by a crossbar or height limit");
        Assert(b.FlightMode==BallFlightMode.Ground,"goal retained temporary flight");
    }
});
Test("skill ball uses the same planar goalpost collision", () => {
    var sim=Sim(rules:new MatchRules { PhysicsSubsteps=1 }); IsolateBall(sim); var b=sim.State.Ball;
    b.FlightMode=BallFlightMode.SkillOverhead; b.Height=3; b.Position=new Vec2(15.5f,2.1f); b.Velocity=new Vec2(24,0);
    Steps(sim,1); Assert(sim.State.CyanScore==0 && b.Velocity.X<0,"skill height bypassed a planar goalpost");
});
Test("skill first landing returns immediately to 2D without vertical rebound", () => {
    var sim=Sim(); IsolateBall(sim); var b=sim.State.Ball;
    b.FlightMode=BallFlightMode.SkillOverhead; b.Height=0.05f; b.VerticalVelocity=-8; b.Position=new Vec2(3,3); b.Velocity=new Vec2(4,0);
    Steps(sim,1); Assert(b.FlightMode==BallFlightMode.Ground && !b.Airborne,"landing retained a flight state");
    Near(b.Height,0); Near(b.PreviousHeight,0); Near(b.VerticalVelocity,0);
    Assert(b.Velocity.Length<3.3f,"skill landing did not hand off velocity");
    Steps(sim,120); Near(b.Height,0); Near(b.VerticalVelocity,0);
});
Test("invalid skill launch never enables height", () => {
    var sim=Sim(); Assert(!sim.TryLaunchOverheadSkill(0,new Vec2(8,0),8),"no-ball player launched skill");
    sim.ResetTraining(true);
    foreach (var velocity in new[] { new Vec2(float.NaN,0),new Vec2(100,0) })
        Assert(!sim.TryLaunchOverheadSkill(0,velocity,8),"invalid horizontal speed accepted");
    Assert(!sim.TryLaunchOverheadSkill(0,new Vec2(8,0),0) && !sim.TryLaunchOverheadSkill(0,new Vec2(8,0),float.NaN),"invalid vertical speed accepted");
    Assert(sim.State.Ball.OwnerId==0 && sim.State.Ball.FlightMode==BallFlightMode.Ground,"rejected launch changed possession or mode");
});
Test("reception and training reset clear the temporary skill flight mode", () => {
    foreach (bool reset in new[] { false,true }) {
        var sim=Sim(); sim.ResetTraining(true); Assert(sim.TryLaunchOverheadSkill(0,new Vec2(8,0),8),"skill failed");
        Steps(sim,12);
        if (reset) sim.ResetTraining(true); else sim.GiveBallForSetup(sim.State.Players[1]);
        var b=sim.State.Ball; Assert(b.FlightMode==BallFlightMode.Ground && !b.Airborne,"reset retained skill height");
        Near(b.Height,0); Near(b.PreviousHeight,0); Near(b.VerticalVelocity,0);
    }
});
Test("skill high ball cannot be caught by standing below its clearance", () => {
    var sim=Sim(); IsolateBall(sim); var b=sim.State.Ball;
    sim.State.Players[0].Position=b.Position=Vec2.Zero;
    b.FlightMode=BallFlightMode.SkillOverhead; b.Height=2; b.VerticalVelocity=-1;
    Steps(sim,5); Assert(b.OwnerId==-1,"ground player caught the skill ball overhead");
});
Test("skill descending low ball can be received", () => {
    var sim=Sim(); IsolateBall(sim); var b=sim.State.Ball;
    sim.State.Players[0].Position=b.Position=Vec2.Zero;
    b.FlightMode=BallFlightMode.SkillOverhead; b.Height=0.2f; b.VerticalVelocity=-2;
    Steps(sim,1); Assert(b.OwnerId==0 && b.FlightMode==BallFlightMode.Ground,"descending skill ball could not return to normal reception");
});

MatchSimulation StarSim(CharacterId id = CharacterId.Marine, MatchMode mode = MatchMode.Training)
{
    var roster=StarStrikerLoadouts.CreateRoster(); foreach(var stats in roster) stats.HealthRegen=0;
    var sim = new MatchSimulation(new MatchRules { MovementSpeedScale=1 }, roster, id, mode);
    if(mode==MatchMode.Training) sim.ResetTraining(false); else sim.State.Phase = MatchPhase.Playing;
    return sim;
}
Test("star roster contains two reference characters with uniform 100 resources", () => {
    var roster = StarStrikerLoadouts.CreateRoster();
    Assert(roster.Length == 2 && roster[0].Id == CharacterId.Marine && roster[1].Id == CharacterId.Thor, "wrong active roster");
    foreach (var p in roster) { Near(p.MaxHealth,100); Near(p.MaxEnergy,100); p.ReferenceSkills.Validate(); }
    Near(roster[0].Speed,2.35f); Near(roster[1].Speed,2); Near(roster[0].PushPower,.62f); Near(roster[1].PushPower,.75f);
});
Test("two character roster supports training opponents and a full 3v3 match", () => {
    var training = StarSim(); Assert(training.State.Players.Count == 2 && training.State.Players[1].Stats.Id == CharacterId.Thor,"training target missing");
    var match = StarSim(CharacterId.Thor,MatchMode.BotMatch);
    Assert(match.State.Players.Count == 6 && match.State.Players[0].Stats.Id == CharacterId.Thor,"3v3 or selection lost");
    Assert(match.State.Players.Count(p => p.Team == Team.Cyan) == 3 && match.State.Players.Count(p => p.Team == Team.Amber) == 3,"unbalanced teams");
    foreach (var id in new[] { CharacterId.Marine,CharacterId.Thor })
        Assert(match.State.Players.Count(p=>p.Team==Team.Cyan && p.Stats.Id==id)==match.State.Players.Count(p=>p.Team==Team.Amber && p.Stats.Id==id),"unequal team character composition");
});
Test("ordinary kick starts a six second cooldown independently of tackle", () => {
    var sim=StarSim(); sim.ResetTraining(true); var p=sim.State.Players[0];
    Steps(sim,1,new PlayerCommand { Aim=new Vec2(1,0),KickPressed=true,KickReleased=true });
    Near(p.KickCooldown,6); Near(p.TackleCooldown,0); Near(p.Health,100);
    sim.GiveBallForSetup(p); Steps(sim,1,new PlayerCommand { KickPressed=true,KickReleased=true });
    Assert(sim.State.Ball.OwnerId==0 && !p.Charging && p.Kicks==1,"cooldown did not block second kick");
    Steps(sim,359); Steps(sim,1,new PlayerCommand { KickPressed=true,KickReleased=true });
    Assert(p.Kicks==2 && sim.State.Ball.OwnerId==-1,"kick did not recover after six seconds");
});
Test("cancelled kick consumes neither cooldown nor health", () => {
    var sim=StarSim(); sim.ResetTraining(true); var p=sim.State.Players[0];
    Steps(sim,15,new PlayerCommand { KickPressed=true,KickHeld=true });
    Steps(sim,1,new PlayerCommand { KickReleased=true,Cancel=true });
    Assert(sim.State.Ball.OwnerId==0 && !p.Charging,"cancel lost possession"); Near(p.KickCooldown,0); Near(p.Health,100);
});
Test("tackle spends inverse force health once and has five second cooldown", () => {
    var sim=StarSim(); var p=sim.State.Players[0];
    Steps(sim,1,new PlayerCommand { TacklePressed=true,Aim=new Vec2(0,-1) });
    Near(p.Health,100-10/.62f); Near(p.Energy,100); Near(p.TackleCooldown,5); Near(p.KickCooldown,0);
    Steps(sim,30); float health=p.Health;
    Steps(sim,1,new PlayerCommand { TacklePressed=true }); Near(p.Health,health);
    Assert(p.TackleCooldown>4,"early tackle restarted cooldown");
});
Test("higher push force costs less health and pushes farther", () => {
    var marine=StarSim(); var thor=StarSim(CharacterId.Thor);
    foreach (var sim in new[] {marine,thor}) {
        Steps(sim,1,new PlayerCommand { TacklePressed=true,Aim=new Vec2(0,-1) });
        var p=sim.State.Players[0]; p.DashDirection=new Vec2(1,0);
        TackleSystem.Resolve(sim,p,sim.State.Players[1],new Vec2(1,0));
    }
    Assert(thor.State.Players[0].Health>marine.State.Players[0].Health,"stronger force cost more health");
    Assert(thor.State.Players[1].PushVelocity.Length>marine.State.Players[1].PushVelocity.Length,"force does not affect push");
});
Test("insufficient tackle health rejects without spending or starting cooldown", () => {
    var sim=StarSim(); var p=sim.State.Players[0]; p.Health=sim.Rules.TackleHealthCost(p.Stats);
    float hp=p.Health; Steps(sim,1,new PlayerCommand {TacklePressed=true});
    Near(p.Health,hp); Near(p.TackleCooldown,0); Assert(p.Dash==DashKind.None && !p.IsDead,"low health tackle executed");
    Assert(sim.State.Events.Any(e=>e.Kind==MatchEventKind.ActionRejected),"no rejection feedback");
});
Test("kicking and tackling can be used while the other action cools down", () => {
    var sim=StarSim(); sim.ResetTraining(true); var p=sim.State.Players[0];
    Steps(sim,1,new PlayerCommand {Aim=new Vec2(1,0),KickPressed=true,KickReleased=true}); Steps(sim,10);
    Steps(sim,1,new PlayerCommand {Aim=new Vec2(0,-1),TacklePressed=true});
    Assert(p.KickCooldown>0 && p.TackleCooldown>0,"kick blocked tackle");
    Steps(sim,20); sim.GiveBallForSetup(p); p.KickCooldown=0;
    Steps(sim,1,new PlayerCommand {KickPressed=true,KickReleased=true});
    Assert(sim.State.Ball.OwnerId==-1 && p.TackleCooldown>0,"tackle blocked kick");
});
Test("marine stim uses table costs and increases speed for five seconds", () => {
    var sim=StarSim(); var p=sim.State.Players[0]; p.Position=new Vec2(-8,-5);
    Steps(sim,1,new PlayerCommand {QPressed=true}); Near(p.Health,90); Near(p.Energy,88); Near(p.QCooldown,6); Near(p.SkillSpeedBonus,.65f);
    var start=p.Position; Steps(sim,60,new PlayerCommand {Move=new Vec2(1,0)});
    Near(Vec2.Distance(start,p.Position),3,.01f);
    Steps(sim,240); Near(p.SkillSpeedRemaining,0); Near(p.SkillSpeedBonus,0);
});
Test("reference skill rejects missing energy without spending health", () => {
    foreach (var id in new[] { CharacterId.Marine,CharacterId.Thor }) {
        var sim=StarSim(id); var p=sim.State.Players[0]; p.Energy=1;
        Steps(sim,1,new PlayerCommand {QPressed=true}); Near(p.Health,100); Near(p.QCooldown,0);
        Assert(p.SkillSpeedRemaining==0,"failed skill created buff");
    }
});
Test("reference skill health payment cannot kill the caster or waste energy", () => {
    var sim=StarSim(); var p=sim.State.Players[0]; p.Health=10;
    Steps(sim,1,new PlayerCommand {QPressed=true}); Near(p.Health,10); Near(p.Energy,100); Near(p.QCooldown,0);
});
Test("marine bomb is placed at feet without possession", () => {
    var sim=StarSim(); var p=sim.State.Players[0];
    Steps(sim,1,new PlayerCommand {EPressed=true}); Near(p.Energy,60); Near(p.Health,100); Near(p.ECooldown,8);
    Assert(sim.State.Bombs.Count==1 && !sim.State.Bombs[0].AttachedToBall,"ground bomb missing");
    Near(Vec2.Distance(p.Position,sim.State.Bombs[0].Position),0);
});
Test("marine bomb follows the football across a normal ground kick", () => {
    var sim=StarSim(); sim.ResetTraining(true); Steps(sim,1,new PlayerCommand {EPressed=true});
    Assert(sim.State.Bombs[0].AttachedToBall,"held ball did not receive bomb");
    Steps(sim,1,new PlayerCommand {Aim=new Vec2(1,0),KickPressed=true,KickReleased=true}); Steps(sim,10);
    Assert(sim.State.Ball.Position.X>-2 && sim.State.Bombs[0].AttachedToBall,"bomb did not travel with pass");
    Near(Vec2.Distance(sim.State.Ball.Position,sim.State.Bombs[0].Position),0,.04f);
    Assert(sim.State.Ball.FlightMode==BallFlightMode.Ground,"bomb kick activated height");
});
Test("marine automatic bomb fuse waits three seconds then pushes and stuns", () => {
    var sim=StarSim(); var target=sim.State.Players[1]; target.Position=new Vec2(-.8f,0);
    Steps(sim,1,new PlayerCommand {EPressed=true}); Steps(sim,178);
    Assert(sim.State.Bombs.Count==1 && target.Stun==0,"bomb exploded before three seconds");
    Steps(sim,1); Assert(sim.State.Bombs.Count==0 && target.Stun>1.7f && target.PushRemaining>0,"bomb did not explode with control");
    Near(target.Health,100); Assert(sim.State.Events.Any(e=>e.Kind==MatchEventKind.Explosion),"explosion event absent");
});
Test("marine remote detonation costs ten energy and works during E cooldown", () => {
    var sim=StarSim(); Steps(sim,1,new PlayerCommand {EPressed=true});
    Steps(sim,1,new PlayerCommand {SpecialPressed=true}); Near(sim.State.Players[0].Energy,50);
    Assert(sim.State.Bombs.Count==0 && sim.State.Players[0].ECooldown>7.9f,"manual detonation blocked or reset E");
});
Test("remote detonation without a bomb does not spend energy", () => {
    var sim=StarSim(); Steps(sim,1,new PlayerCommand {SpecialPressed=true}); Near(sim.State.Players[0].Energy,100);
});
Test("insufficient detonation energy preserves the bomb until its fuse", () => {
    var sim=StarSim(); Steps(sim,1,new PlayerCommand {EPressed=true}); sim.State.Players[0].Energy=5;
    Steps(sim,1,new PlayerCommand {SpecialPressed=true}); Assert(sim.State.Bombs.Count==1,"failed detonation removed bomb");
    Near(sim.State.Players[0].Energy,5);
});
Test("bomb control ignores allies and protected opponents", () => {
    var sim=StarSim(mode:MatchMode.BotMatch); var p=sim.State.Players[0];
    foreach (var other in sim.State.Players) other.Position=new Vec2(10,-6+other.Id);
    p.Position=new Vec2(-5,0); var ally=sim.State.Players[1]; ally.Position=new Vec2(-3.8f,0);
    var enemy=sim.State.Players[3]; enemy.Position=new Vec2(-5,1.5f); enemy.FullProtection=1;
    Steps(sim,1,new PlayerCommand {EPressed=true}); Steps(sim,1,new PlayerCommand {SpecialPressed=true});
    Near(ally.Stun,0); Near(enemy.Stun,0); Near(ally.PushRemaining,0); Near(enemy.PushRemaining,0);
});
Test("thor drive follows table cost and discrete 0.375 second decay", () => {
    var sim=StarSim(CharacterId.Thor); var p=sim.State.Players[0];
    Steps(sim,1,new PlayerCommand {QPressed=true}); Near(p.Health,90); Near(p.Energy,80); Near(p.QCooldown,8); Near(p.SkillSpeedBonus,2.4f);
    Steps(sim,21); Near(p.SkillSpeedBonus,2.4f); Steps(sim,1); Near(p.SkillSpeedBonus,2.16f,.002f);
    Steps(sim,22); Near(p.SkillSpeedBonus,1.944f,.002f);
});
Test("thor thrusters use separate costs and immediately refresh drive", () => {
    var sim=StarSim(CharacterId.Thor); var p=sim.State.Players[0];
    Steps(sim,1,new PlayerCommand {QPressed=true});
    Steps(sim,1,new PlayerCommand {EPressed=true,Aim=new Vec2(0,1)});
    Near(p.Health,80); Near(p.Energy,62); Near(p.ECooldown,10); Near(p.QCooldown,0); Near(p.KickCooldown,0); Near(p.TackleCooldown,0);
    Steps(sim,10); Near(p.Position.Y,2.4f,.01f); Assert(p.Dash==DashKind.None,"thruster dash did not finish");
    Steps(sim,10); Steps(sim,1,new PlayerCommand {QPressed=true}); Near(p.Health,70); Assert(p.QCooldown>7.9f,"refreshed drive unavailable");
});
Test("thor ground thrusters stop at bodies and never change ball height", () => {
    var sim=StarSim(CharacterId.Thor); var p=sim.State.Players[0]; var target=sim.State.Players[1]; target.Position=new Vec2(0,0);
    Steps(sim,1,new PlayerCommand {EPressed=true,Aim=new Vec2(1,0)}); Steps(sim,15);
    Assert(p.Position.X<=-1.09f && p.Dash==DashKind.None,"thrusters passed through a unit");
    Assert(sim.State.Ball.FlightMode==BallFlightMode.Ground && !sim.State.Ball.Airborne,"thrusters enabled airborne rules");
});
Test("training reset clears bombs speed buffs and both shared cooldowns", () => {
    var sim=StarSim(); Steps(sim,1,new PlayerCommand {QPressed=true}); Steps(sim,1,new PlayerCommand {EPressed=true});
    sim.State.Players[0].KickCooldown=6; sim.State.Players[0].TackleCooldown=5; sim.ResetTraining(false);
    var p=sim.State.Players[0]; Near(p.Health,100); Near(p.Energy,100); Near(p.KickCooldown,0); Near(p.TackleCooldown,0);
    Near(p.SkillSpeedRemaining,0); Assert(sim.State.Bombs.Count==0,"reset leaked bomb");
});
Test("knockout clears owned bombs and revival restores both actions in place", () => {
    var sim=StarSim(); var p=sim.State.Players[0]; var enemy=sim.State.Players[1];
    Steps(sim,1,new PlayerCommand {EPressed=true}); p.KickCooldown=6; p.TackleCooldown=5;
    var position=p.Position; sim.ApplySkillHit(enemy,p,100,0,SkillSlot.Weapon);
    Assert(p.IsDead && sim.State.Bombs.Count==0,"knockout retained bomb");
    Steps(sim,300); Assert(!p.IsDead,"five second revive failed"); Near(p.Health,100); Near(p.Energy,100);
    Near(p.KickCooldown,0); Near(p.TackleCooldown,0); Near(Vec2.Distance(position,p.Position),0);
});
Test("two character bots respect new cooldowns and remain finite", () => {
    var sim=StarSim(mode:MatchMode.BotMatch); var bots=sim.State.Players.Select(p=>new BotCommandSource(sim.Rules)).ToArray();
    for (int i=0;i<1800;i++) sim.Step(sim.State.Players.Select(p=>bots[p.Id].ReadCommand(sim.State,p.Id)).ToArray());
    foreach (var p in sim.State.Players) Assert(Geometry.IsFinite(p.Position.X) && Geometry.IsFinite(p.Position.Y) && p.Health>=0 && p.Health<=100,"invalid bot state");
    Assert(sim.State.Players.Sum(p=>p.Kicks)>0,"bots never kicked");
});

Test("larger field permits running beyond both former boundaries", () => {
    var sim=new MatchSimulation(new MatchRules(),StarStrikerLoadouts.CreateRoster(),CharacterId.Marine,MatchMode.Training);
    sim.ResetTraining(false); var p=sim.State.Players[0]; p.Position=p.PreviousPosition=new Vec2(15.8f,9.4f);
    Steps(sim,120,new PlayerCommand {Move=new Vec2(1,1)});
    Assert(p.Position.X>18 && p.Position.Y>11,"old 32 by 18 bounds still block running");
});
Test("larger field clamps bodies at the new corner", () => {
    var sim=new MatchSimulation(new MatchRules(),StarStrikerLoadouts.CreateRoster(),CharacterId.Marine,MatchMode.Training);
    sim.ResetTraining(false); var p=sim.State.Players[0]; p.Position=new Vec2(sim.Rules.FieldWidth*.5f-1,sim.Rules.FieldHeight*.5f-1);
    Steps(sim,120,new PlayerCommand {Move=new Vec2(1,1)});
    Near(p.Position.X,sim.Rules.FieldWidth*.5f-p.Stats.Radius);
    Near(p.Position.Y,sim.Rules.FieldHeight*.5f-p.Stats.Radius);
});
Test("football passes the former wall and scores only at the enlarged goal", () => {
    var sim=new MatchSimulation(new MatchRules(),StarStrikerLoadouts.CreateRoster(),CharacterId.Marine,MatchMode.Training);
    sim.ResetTraining(false); var ball=sim.State.Ball; ball.Position=new Vec2(15.8f,0); ball.Velocity=new Vec2(18,0);
    Steps(sim,6); Assert(ball.Position.X>16.5f && ball.Velocity.X>0 && sim.State.CyanScore==0,"former goal line blocked or scored");
    ball.Position=new Vec2(sim.Rules.FieldWidth*.5f-.1f,0); ball.Velocity=new Vec2(18,0);
    Steps(sim,3); Assert(sim.State.CyanScore==1,"new goal line missed a planar shot");
});
Test("new boundary restarts shots outside the goal mouth", () => {
    var sim=new MatchSimulation(new MatchRules(),StarStrikerLoadouts.CreateRoster(),CharacterId.Marine,MatchMode.Training);
    sim.ResetTraining(false); var ball=sim.State.Ball;
    ball.Position=new Vec2(sim.Rules.FieldWidth*.5f-.2f,8); ball.Velocity=new Vec2(18,0);
    Steps(sim,3); Assert(sim.State.Phase==MatchPhase.SetPiece && sim.State.Restart==RestartKind.GoalKick && sim.State.CyanScore==0,"new field out of play failed");
});
Test("center circle radius controls warmup blocking on enlarged field", () => {
    var rules=new MatchRules {CenterCircleRadius=2.8f};
    var sim=new MatchSimulation(rules,StarStrikerLoadouts.CreateRoster(),CharacterId.Marine,MatchMode.Training);
    var p=sim.State.Players[0]; p.Position=new Vec2(-5,0);
    Steps(sim,170,new PlayerCommand {Move=new Vec2(1,0)});
    Near(p.Position.Length,rules.CenterCircleRadius+p.Stats.Radius,.002f);
    Assert(sim.State.Ball.OwnerId==-1 && sim.State.Ball.Position.Length<.001f,"changed center circle failed to protect ball");
});

Test("enlarged kickoff circle starts all players outside without corrective teleport", () => {
    var sim=new MatchSimulation(new MatchRules(),StarStrikerLoadouts.CreateRoster(),CharacterId.Marine,MatchMode.BotMatch);
    var spawn=sim.State.Players.Select(p=>p.Position).ToArray();
    foreach(var p in sim.State.Players)
        Assert(p.Position.Length>=sim.Rules.CenterCircleRadius+p.Stats.Radius+.8f,"opening spawn starts inside enlarged shield");
    sim.Step(new PlayerCommand[sim.State.Players.Count]);
    foreach(var p in sim.State.Players) Near(Vec2.Distance(spawn[p.Id],p.Position),0,.001f);
});
Test("larger current map permits running past the previous 48 by 28 bounds", () => {
    foreach(int x in new[]{-1,1}) foreach(int y in new[]{-1,1})
    {
        var sim=new MatchSimulation(new MatchRules(),StarStrikerLoadouts.CreateRoster(),CharacterId.Marine,MatchMode.Training);
        sim.ResetTraining(false); var p=sim.State.Players[0];p.Position=new Vec2(x*23.8f,y*14.6f);
        Steps(sim,60,new PlayerCommand {Move=new Vec2(x,y)});
        Assert(Math.Abs(p.Position.X)>25 && Math.Abs(p.Position.Y)>16,"previous enlarged field still blocks running");
    }
});

// 0.15: finite bodies, persistent RTS orders, whole-ball boundaries and violent-football set pieces.
void EmptyField(MatchSimulation sim)
{
    foreach (var p in sim.State.Players) { p.Position = new Vec2(-18 + p.Id * 3, -12); p.PreviousPosition = p.Position; }
    sim.State.Ball.OwnerId = -1; sim.State.Ball.Position = new Vec2(-20, 12); sim.State.Ball.Velocity = Vec2.Zero;
}
Test("ordinary movement cannot tunnel through a stationary player", () => {
    var sim=StarSim(); EmptyField(sim); var p=sim.State.Players[0];var b=sim.State.Players[1];
    p.Position=new Vec2(-3,0);b.Position=Vec2.Zero;p.Stats.Speed=600;sim.Rules.PhysicsSubsteps=1;
    Steps(sim,1,new PlayerCommand {Move=new Vec2(1,0)});
    Assert(p.Position.X<0 && Vec2.Distance(p.Position,b.Position)>=p.Stats.Radius+b.Stats.Radius-.001f,"ordinary movement crossed a body");
});
Test("head-on simultaneous movement cannot swap players", () => {
    var sim=StarSim();EmptyField(sim);var a=sim.State.Players[0];var b=sim.State.Players[1];a.Position=new Vec2(-2,0);b.Position=new Vec2(2,0);
    a.Stats.Speed=b.Stats.Speed=300;sim.Rules.PhysicsSubsteps=1;
    sim.Step(new[]{new PlayerCommand {Move=new Vec2(1,0)},new PlayerCommand {Move=new Vec2(-1,0)}});
    Assert(a.Position.X<b.Position.X && Vec2.Distance(a.Position,b.Position)>=a.Stats.Radius+b.Stats.Radius-.001f,"head-on units overlapped or swapped");
});
Test("normal movement slides along a contacted body", () => {
    var sim=StarSim();EmptyField(sim);var p=sim.State.Players[0];var b=sim.State.Players[1];p.Position=new Vec2(-1.35f,0);b.Position=Vec2.Zero;
    Steps(sim,30,new PlayerCommand {Move=new Vec2(1,1)});
    Assert(p.Position.Y>.6f && Vec2.Distance(p.Position,b.Position)>=1.3f-.001f,"sliding trapped or penetrated unit");
});
Test("friendly bodies fully separate and larger body moves less", () => {
    var sim=StarSim(mode:MatchMode.BotMatch);EmptyField(sim);var a=sim.State.Players[0];var b=sim.State.Players[1];
    a.Position=Vec2.Zero;b.Position=new Vec2(.5f,0);var beforeA=a.Position;var beforeB=b.Position;
    Steps(sim,1);Assert(Vec2.Distance(a.Position,b.Position)>=a.Stats.Radius+b.Stats.Radius-.001f,"friendly overlap remained");
    Assert(Vec2.Distance(beforeA,a.Position)>Vec2.Distance(beforeB,b.Position),"size had no collision inertia");
});
Test("crowded boundary separation remains inside field and non-overlapping", () => {
    var sim=StarSim(mode:MatchMode.BotMatch);EmptyField(sim);
    for(int i=0;i<3;i++)sim.State.Players[i].Position=new Vec2(27.1f,15.8f+i*.1f);
    Steps(sim,20);
    foreach(var p in sim.State.Players)Assert(Math.Abs(p.Position.X)<=28-p.Stats.Radius+.001f && Math.Abs(p.Position.Y)<=17-p.Stats.Radius+.001f,"separation left field");
    for(int i=0;i<3;i++)for(int j=i+1;j<3;j++)Assert(Vec2.Distance(sim.State.Players[i].Position,sim.State.Players[j].Position)>=sim.State.Players[i].Stats.Radius+sim.State.Players[j].Stats.Radius-.002f,"crowd overlap remained");
});
Test("body radius determines clearance through the same gap", () => {
    var sim=StarSim(mode:MatchMode.BotMatch);EmptyField(sim);var p=sim.State.Players[0];
    sim.State.Players[1].Stats=new CharacterStats {Radius=.5f};sim.State.Players[2].Stats=new CharacterStats {Radius=.5f};
    sim.State.Players[1].Position=new Vec2(0,1.15f);sim.State.Players[2].Position=new Vec2(0,-1.15f);p.Position=new Vec2(-4,0);
    Assert(NavigationPathfinder.ClearSegment(sim.State,sim.Rules,p,p.Position,new Vec2(4,0)),"small body could not fit gap");
    p.Stats.Radius=.8f;Assert(!NavigationPathfinder.ClearSegment(sim.State,sim.Rules,p,p.Position,new Vec2(4,0)),"large body passed narrow gap");
});
Test("single right click persists, routes around enemy and stops on arrival", () => {
    var sim=StarSim();EmptyField(sim);var p=sim.State.Players[0];p.Position=new Vec2(-4,0);sim.State.Players[1].Position=Vec2.Zero;
    Steps(sim,320,ContextOrder.Create(sim.State,sim.Rules,0,new Vec2(4,0)));
    Assert(Vec2.Distance(p.Position,new Vec2(4,0))<.12f && p.Order==UnitOrderKind.None,"click order failed to route or finish: "+p.Position);
});
Test("navigation detours around friendly players as well as enemies", () => {
    var sim=StarSim(mode:MatchMode.BotMatch);EmptyField(sim);var p=sim.State.Players[0];p.Position=new Vec2(-3,0);sim.State.Players[1].Position=Vec2.Zero;
    var path=NavigationPathfinder.Find(sim.State,sim.Rules,p,new Vec2(3,0));
    Assert(path.Count>1 && path.Any(point=>Math.Abs(point.Y)>.5f),"friendly unit was absent from path graph");
});
Test("persistent movement replans when a body blocks its next segment", () => {
    var sim=StarSim();EmptyField(sim);var p=sim.State.Players[0];p.Position=new Vec2(-4,0);sim.State.Players[1].Position=new Vec2(0,4);
    Steps(sim,40,ContextOrder.Create(sim.State,sim.Rules,0,new Vec2(4,0)));sim.State.Players[1].Position=Vec2.Zero;
    Steps(sim,280);Assert(Vec2.Distance(p.Position,new Vec2(4,0))<.12f,"moving obstacle broke persistent path");
});
Test("Shift appends orders and stop clears the entire queue", () => {
    var sim=StarSim();EmptyField(sim);var p=sim.State.Players[0];p.Position=Vec2.Zero;
    Steps(sim,1,ContextOrder.Create(sim.State,sim.Rules,0,new Vec2(1,0)));
    Steps(sim,1,ContextOrder.Create(sim.State,sim.Rules,0,new Vec2(1,1),queue:true));Assert(p.QueuedOrders==1,"queued order replaced first");
    Steps(sim,100);Assert(Vec2.Distance(p.Position,new Vec2(1,1))<.12f && p.QueuedOrders==0,"waypoint queue did not finish");
    Steps(sim,1,ContextOrder.Create(sim.State,sim.Rules,0,new Vec2(10,0)));
    Steps(sim,1,ContextOrder.Create(sim.State,sim.Rules,0,new Vec2(10,2),queue:true));Steps(sim,1,new PlayerCommand {StopPressed=true});
    var position=p.Position;Steps(sim,30);Near(Vec2.Distance(position,p.Position),0);Assert(p.Order==UnitOrderKind.None && p.QueuedOrders==0,"stop retained route");
});
Test("right-click context respects teams, weapons and the football", () => {
    var sim=StarSim(mode:MatchMode.BotMatch);
    Assert(ContextOrder.Create(sim.State,sim.Rules,0,Vec2.Zero,3).OrderKind==UnitOrderKind.Attack,"armed enemy click did not attack");
    Assert(ContextOrder.Create(sim.State,sim.Rules,0,Vec2.Zero,1).OrderKind==UnitOrderKind.Follow,"friendly click did not follow");
    Assert(ContextOrder.Create(sim.State,sim.Rules,0,Vec2.Zero,clickedBall:true).OrderKind==UnitOrderKind.ChaseBall,"ball click did not chase");
    sim.State.Players[0].Stats.Loadout.HasWeapon=false;
    Assert(ContextOrder.Create(sim.State,sim.Rules,0,Vec2.Zero,3).OrderKind==UnitOrderKind.Follow,"unarmed unit received attack order");
});
Test("right-click enemy chases into weapon range and fires without holding mouse", () => {
    var sim=StarSim();EmptyField(sim);var p=sim.State.Players[0];var enemy=sim.State.Players[1];p.Position=new Vec2(-9,0);enemy.Position=Vec2.Zero;
    float hp=enemy.Health;Steps(sim,180,ContextOrder.Create(sim.State,sim.Rules,0,enemy.Position,enemy.Id));
    Assert(p.Position.X>-6.7f && enemy.Health<hp && p.Ammo<3,"persistent attack did not chase and fire");
});
Test("right-click ground replaces attack and no longer fires", () => {
    var sim=StarSim();EmptyField(sim);var p=sim.State.Players[0];p.Position=new Vec2(-4,0);sim.State.Players[1].Position=Vec2.Zero;
    Steps(sim,1,ContextOrder.Create(sim.State,sim.Rules,0,Vec2.Zero,1));
    Steps(sim,1,ContextOrder.Create(sim.State,sim.Rules,0,new Vec2(-6,3)));Steps(sim,90);
    Assert(p.Order!=UnitOrderKind.Attack && !p.AttackPending,"ground order continued attacking");
});
Test("attack order tracks moving target and clears when target dies", () => {
    var sim=StarSim();EmptyField(sim);var p=sim.State.Players[0];var target=sim.State.Players[1];p.Position=new Vec2(-10,0);target.Position=Vec2.Zero;
    Steps(sim,1,ContextOrder.Create(sim.State,sim.Rules,0,target.Position,1));target.Position=new Vec2(5,4);Steps(sim,30);
    Near(Vec2.Distance(p.OrderPosition,target.Position),0);target.Health=0;target.RespawnRemaining=5;Steps(sim,1);
    Assert(p.Order==UnitOrderKind.None,"dead target remained locked");
});
Test("cancel and pause clear navigation and never restore queued attacks", () => {
    var sim=StarSim();EmptyField(sim);Steps(sim,1,ContextOrder.Create(sim.State,sim.Rules,0,new Vec2(4,5)));sim.CancelPendingInputs();
    var p=sim.State.Players[0];var before=p.Position;Steps(sim,30);Near(Vec2.Distance(before,p.Position),0);
    Steps(sim,1,ContextOrder.Create(sim.State,sim.Rules,0,new Vec2(4,5)));Steps(sim,1,new PlayerCommand {Cancel=true});Assert(p.Order==UnitOrderKind.None,"cancel retained order");
});
Test("navigation routes around the opening shield using real body clearance", () => {
    var sim=new MatchSimulation(new MatchRules(),StarStrikerLoadouts.CreateRoster(),CharacterId.Marine,MatchMode.Training);
    var p=sim.State.Players[0];sim.State.Players[1].Position=new Vec2(10,8);p.Position=new Vec2(-6,0);
    var path=NavigationPathfinder.Find(sim.State,sim.Rules,p,new Vec2(6,0));
    Assert(path.Count>1,"opening dome omitted from path graph");
    foreach(var point in path)Assert(point.Length>=sim.Rules.OpeningShieldRadius+p.Stats.Radius-.001f,"path entered warmup dome");
});
Test("moving player receives stationary ball by a continuous relative sweep", () => {
    var sim=StarSim();EmptyField(sim);var p=sim.State.Players[0];p.Position=new Vec2(-3,0);p.Stats.Speed=600;sim.Rules.PhysicsSubsteps=1;
    sim.State.Ball.Position=Vec2.Zero;Steps(sim,1,new PlayerCommand {Move=new Vec2(1,0)});
    Assert(sim.State.Ball.OwnerId==0,"fast player crossed a stationary ball without contact");
});
Test("held football cannot overlap a contacted opponent body", () => {
    var sim=StarSim();sim.ResetTraining(true);var p=sim.State.Players[0];var other=sim.State.Players[1];p.Position=Vec2.Zero;p.Facing=new Vec2(1,0);other.Position=new Vec2(1.3f,0);
    Steps(sim,1,new PlayerCommand {Aim=new Vec2(1,0)});
    Assert(Vec2.Distance(sim.State.Ball.Position,other.Position)>=other.Stats.Radius+sim.Rules.BallRadius-.001f,"held football penetrated opponent");
});
Test("whole football must cross the touchline before out of play", () => {
    var sim=StarSim();EmptyField(sim);var b=sim.State.Ball;b.LastTouchId=0;b.Position=new Vec2(0,17.1f);b.Velocity=Vec2.Zero;
    Steps(sim,1);Assert(sim.State.Phase==MatchPhase.Playing,"partial crossing was out");
    b.Velocity=new Vec2(0,18);Steps(sim,1);Assert(sim.State.Restart==RestartKind.ThrowIn && sim.State.RestartTeam==Team.Amber,"whole crossing did not award opponent throw-in");
});
Test("defender last touch awards correct corner at every end and side", () => {
    foreach(int side in new[]{-1,1})foreach(int end in new[]{-1,1})
    {
        var sim=StarSim();EmptyField(sim);var b=sim.State.Ball;b.LastTouchId=side>0?1:0;b.Position=new Vec2(side*28.1f,end*8);b.Velocity=new Vec2(side*24,0);Steps(sim,1);
        Assert(sim.State.Restart==RestartKind.Corner && sim.State.RestartTeam==(side>0?Team.Cyan:Team.Amber),"wrong corner team");
        Assert(Math.Sign(sim.State.RestartPosition.X)==side && Math.Sign(sim.State.RestartPosition.Y)==end,"wrong corner location");
    }
});
Test("attacker last touch outside goal awards defending goal kick", () => {
    foreach(int side in new[]{-1,1})
    {
        var sim=StarSim();EmptyField(sim);var b=sim.State.Ball;b.LastTouchId=side>0?0:1;b.Position=new Vec2(side*28.1f,8);b.Velocity=new Vec2(side*24,0);Steps(sim,1);
        Assert(sim.State.Restart==RestartKind.GoalKick && sim.State.RestartTeam==(side>0?Team.Amber:Team.Cyan),"wrong goal kick");
    }
});
Test("body deflection updates last touch without erasing kick attribution", () => {
    var sim=StarSim();EmptyField(sim);var p=sim.State.Players[1];p.Position=new Vec2(0,0);p.Stun=1;
    var b=sim.State.Ball;b.Position=new Vec2(-1.3f,0);b.Velocity=new Vec2(24,0);b.LastKickerId=0;b.LastTouchId=0;
    Steps(sim,1);Assert(b.LastTouchId==1 && b.LastKickerId==0,"deflection lost last touch or passer provenance");
});
Test("diagonal boundary sweep selects first crossed line", () => {
    var sim=StarSim();EmptyField(sim);var b=sim.State.Ball;b.Position=new Vec2(28.2f,17);b.LastTouchId=1;b.Velocity=new Vec2(18,18);Steps(sim,1);
    Assert(sim.State.Restart==RestartKind.Corner,"later touchline overrode earlier goal line");
});
Test("normal goal has priority over out-of-play and is counted once", () => {
    var sim=StarSim();EmptyField(sim);var b=sim.State.Ball;b.Position=new Vec2(28.1f,0);b.LastTouchId=0;b.Velocity=new Vec2(18,0);
    Steps(sim,10);Assert(sim.State.CyanScore==1 && sim.State.Restart==RestartKind.None,"goal became out or repeated");
});
Test("held football can go out of play and retains owner last touch", () => {
    var sim=StarSim();sim.ResetTraining(true);var p=sim.State.Players[0];p.Position=new Vec2(0,16.5f);
    Steps(sim,1,new PlayerCommand {Aim=new Vec2(0,1)});
    Assert(sim.State.Restart==RestartKind.ThrowIn && sim.State.RestartTeam==Team.Amber,"held ball was clamped back inside");
});
Test("set-piece preparation freezes game clock and ball while enforcing spacing", () => {
    var sim=StarSim(mode:MatchMode.BotMatch);EmptyField(sim);sim.Restarts.Begin(RestartKind.FreeKick,Team.Cyan,new Vec2(5,0));
    float time=sim.State.RemainingSeconds;var ball=sim.State.Ball.Position;var commands=new PlayerCommand[6];
    for(int i=0;i<6;i++)commands[i]=new PlayerCommand {Move=(ball-sim.State.Players[i].Position).Normalized,AttackHeld=true,QPressed=true,TacklePressed=true};
    for(int tick=0;tick<75;tick++)sim.Step(commands);
    Near(time,sim.State.RemainingSeconds);Near(Vec2.Distance(ball,sim.State.Ball.Position),0);
    foreach(var p in sim.State.Players.Where(p=>p.Team==Team.Amber))Assert(Vec2.Distance(p.Position,ball)>=sim.Rules.SetPieceDistance+p.Stats.Radius-.002f,"enemy entered restart exclusion");
    Assert(sim.State.Projectiles.Count==0 && sim.State.Bombs.Count==0,"set-piece accepted combat inputs");
});
Test("set-piece allows manual aim and kick after preparation without prior cooldown lock", () => {
    var sim=StarSim();sim.SetupTrainingRestart(RestartKind.FreeKick);var p=sim.State.Players[0];p.KickCooldown=6;
    Steps(sim,65);Steps(sim,1,new PlayerCommand {Aim=new Vec2(1,.2f),KickPressed=true,KickReleased=true});
    Assert(sim.State.Phase==MatchPhase.Playing && sim.State.Ball.OwnerId==-1 && sim.State.Ball.Velocity.X>0,"restart kick did not release fixed ball");
    Near(p.KickCooldown,6);Assert(sim.State.Ball.RestartKickerId==0,"second-touch guard missing");
});
Test("restart kicker second touch awards free kick even inside penalty area", () => {
    var sim=StarSim();sim.SetupTrainingRestart(RestartKind.FreeKick);Steps(sim,65);Steps(sim,1,new PlayerCommand {Aim=new Vec2(1,0),KickPressed=true,KickReleased=true});
    var b=sim.State.Ball;b.Position=new Vec2(26,0);b.SelfCatchRemaining=0;b.Velocity=Vec2.Zero;sim.State.Players[0].Position=b.Position;
    Steps(sim,1);Assert(sim.State.Restart==RestartKind.FreeKick && sim.State.RestartTeam==Team.Amber,"second touch did not create free kick");
});
Test("another player touch ends restart second-touch restriction", () => {
    var sim=StarSim();sim.SetupTrainingRestart(RestartKind.FreeKick);Steps(sim,65);Steps(sim,1,new PlayerCommand {Aim=new Vec2(1,0),KickPressed=true,KickReleased=true});
    var b=sim.State.Ball;b.Position=sim.State.Players[1].Position;b.Velocity=Vec2.Zero;Steps(sim,1);
    Assert(sim.State.Phase==MatchPhase.Playing && b.OwnerId==1 && b.RestartKickerId==-1,"other receiver failed to release guard");
});
Test("throw-in cannot directly score while direct free kick can", () => {
    foreach(var kind in new[]{RestartKind.ThrowIn,RestartKind.FreeKick,RestartKind.Corner})
    {
        var sim=StarSim();EmptyField(sim);var b=sim.State.Ball;b.RestartKickerId=0;b.ReleasedRestart=kind;b.LastTouchId=0;b.Position=new Vec2(28.1f,0);b.Velocity=new Vec2(18,0);Steps(sim,1);
        if(kind==RestartKind.ThrowIn)Assert(sim.State.CyanScore==0 && sim.State.Restart==RestartKind.GoalKick,"direct throw-in scored");
        else Assert(sim.State.CyanScore==1,"direct free kick or corner did not score");
    }
});
Test("direct restart own goal awards corner instead of a score", () => {
    var sim=StarSim();EmptyField(sim);var b=sim.State.Ball;b.RestartKickerId=0;b.ReleasedRestart=RestartKind.FreeKick;b.LastTouchId=0;b.Position=new Vec2(-28.1f,0);b.Velocity=new Vec2(-18,0);Steps(sim,1);
    Assert(sim.State.AmberScore==0 && sim.State.Restart==RestartKind.Corner && sim.State.RestartTeam==Team.Amber,"restart own goal incorrectly counted");
});
Test("weapon attacks and off-ball tackles stay legal in violent football", () => {
    var sim=StarSim();EmptyField(sim);var p=sim.State.Players[0];var enemy=sim.State.Players[1];p.Position=new Vec2(-2,0);enemy.Position=Vec2.Zero;
    Steps(sim,30,new PlayerCommand {Aim=new Vec2(1,0),AttackHeld=true});Assert(enemy.Health<100 && sim.State.Phase==MatchPhase.Playing,"weapon caused foul");
    p.Position=new Vec2(-1.4f,0);p.ActionLock=0;Steps(sim,20,new PlayerCommand {Aim=new Vec2(1,0),TacklePressed=true});
    Assert(sim.State.Phase==MatchPhase.Playing && sim.State.Restart==RestartKind.None,"off-ball violent tackle caused foul");
});
Test("AI automatically takes awarded set pieces", () => {
    var sim=StarSim();sim.Restarts.Begin(RestartKind.Corner,Team.Amber,new Vec2(27.65f,16.65f));var bot=new BotCommandSource(sim.Rules);
    var commands=new PlayerCommand[2];for(int tick=0;tick<180 && sim.State.Phase==MatchPhase.SetPiece;tick++){commands[1]=bot.ReadCommand(sim.State,1);sim.Step(commands);}
    Assert(sim.State.Phase==MatchPhase.Playing && sim.State.Players[1].Kicks==1,"bot left restart stuck");
});
Test("restart waits for original full respawn when awarded team is dead", () => {
    var sim=StarSim();var p=sim.State.Players[1];p.Health=0;p.RespawnRemaining=1;
    sim.Restarts.Begin(RestartKind.GoalKick,Team.Amber,new Vec2(25,0));Steps(sim,30);
    Assert(p.IsDead && sim.State.RestartPlayerId==-1,"restart revived or selected dead kicker");
    Steps(sim,35);Assert(!p.IsDead && sim.State.RestartPlayerId==1,"respawned team could not take restart");
});
Test("training exposes corner and free kick and resetting clears restart state", () => {
    var sim=StarSim();sim.SetupTrainingRestart(RestartKind.Corner);Assert(sim.State.Restart==RestartKind.Corner && sim.State.RestartPlayerId==0,"corner practice not local");
    sim.SetupTrainingRestart(RestartKind.FreeKick);Assert(sim.State.Restart==RestartKind.FreeKick && sim.State.RestartPlayerId==0,"free kick practice not local");
    sim.ResetTraining(false);Assert(sim.State.Phase==MatchPhase.Playing && sim.State.Restart==RestartKind.None && sim.State.Ball.RestartKickerId==-1,"reset retained set piece");
});
Test("3v3 navigation and restarts stay finite and non-overlapping in sustained match", () => {
    var sim=StarSim(mode:MatchMode.BotMatch);sim.Rules.MatchSeconds=40;sim.State.RemainingSeconds=40;
    var bots=sim.State.Players.Select(_=>new BotCommandSource(sim.Rules)).ToArray();var commands=new PlayerCommand[6];
    for(int tick=0;tick<3000 && sim.State.Phase!=MatchPhase.Finished;tick++)
    {
        for(int i=0;i<6;i++)commands[i]=bots[i].ReadCommand(sim.State,i);sim.Step(commands);
        foreach(var p in sim.State.Players)Assert(Geometry.IsFinite(p.Position.X)&&Geometry.IsFinite(p.Position.Y),"non-finite bot position");
        if(sim.State.Phase==MatchPhase.Playing)for(int i=0;i<6;i++)for(int j=i+1;j<6;j++)
        {var a=sim.State.Players[i];var b=sim.State.Players[j];if(!a.IsDead&&!b.IsDead)Assert(Vec2.Distance(a.Position,b.Position)>=a.Stats.Radius+b.Stats.Radius-.004f,"sustained match bodies overlapped");}
    }
});

// 0.16: collision acquisition and displacement-based possession.
MatchSimulation PossessionFixture(bool allied=false)
{
    var sim=StarSim(mode:allied?MatchMode.BotMatch:MatchMode.Training);EmptyField(sim);
    var target=sim.State.Players[1];target.Position=Vec2.Zero;target.Facing=new Vec2(1,0);
    sim.State.Players[0].Position=new Vec2(-4,3);sim.GiveBallForSetup(target);Steps(sim,1);return sim;
}
Test("being near football outside the physical circles never grants possession", () => {
    var sim=StarSim();EmptyField(sim);var p=sim.State.Players[0];p.Position=Vec2.Zero;
    sim.State.Ball.Position=new Vec2(p.Stats.Radius+sim.Rules.BallRadius+.015f,0);
    Steps(sim,60);Assert(sim.State.Ball.OwnerId==-1,"proximity acquired football without touching");
});
Test("surface contact with stationary football grants possession", () => {
    var sim=StarSim();EmptyField(sim);var p=sim.State.Players[0];p.Position=Vec2.Zero;
    sim.State.Ball.Position=new Vec2(p.Stats.Radius+sim.Rules.BallRadius,0);
    Steps(sim,1);Assert(sim.State.Ball.OwnerId==0,"tangent physical contact did not receive football");
});
Test("collision acquisition rejects a false remote contact request", () => {
    var sim=StarSim();EmptyField(sim);var p=sim.State.Players[0];sim.State.Ball.Position=Vec2.Zero;
    Assert(!sim.TryAcquireBallOnContact(p,p.Position) && sim.State.Ball.OwnerId==-1,"remote request bypassed collider overlap");
});
Test("ordinary football collision grants possession at every kick speed", () => {
    foreach(float speed in new[]{1,10,18,24})
    {
        var sim=StarSim();EmptyField(sim);var p=sim.State.Players[0];p.Position=Vec2.Zero;
        sim.State.Ball.Position=new Vec2(-p.Stats.Radius-sim.Rules.BallRadius-.01f,0);sim.State.Ball.Velocity=new Vec2(speed,0);
        Steps(sim,60);Assert(sim.State.Ball.OwnerId==0,"speed threshold rejected actual collision: "+speed);
    }
});
Test("right-click pursuit approaches until colliding rather than granting ball remotely", () => {
    var sim=StarSim();EmptyField(sim);var p=sim.State.Players[0];p.Position=new Vec2(-3,0);sim.State.Ball.Position=Vec2.Zero;
    var commands=new PlayerCommand[2];commands[0]=ContextOrder.Create(sim.State,sim.Rules,0,Vec2.Zero,clickedBall:true);
    bool received=false;
    for(int tick=0;tick<150;tick++)
    {
        float before=Vec2.Distance(p.Position,sim.State.Ball.Position);sim.Step(commands);commands[0]=default;
        if(sim.State.Ball.OwnerId==0){Assert(before<=p.Stats.Radius+sim.Rules.BallRadius+p.Stats.Speed*MatchSimulation.TickSeconds+.001f,"pursuit acquired while far away");received=true;break;}
    }
    Assert(received,"pursuit never physically reached ball");
});
Test("front push releases ball in place and never assigns it to pusher", () => {
    var sim=PossessionFixture();var actor=sim.State.Players[0];var target=sim.State.Players[1];var position=sim.State.Ball.Position;
    actor.DashDirection=new Vec2(-1,0);TackleSystem.Resolve(sim,actor,target,new Vec2(-1,0));
    Assert(sim.State.Ball.OwnerId==-1 && target.PushRemaining>0 && actor.Steals==0,"front push directly assigned ball or failed to displace");
    Near(Vec2.Distance(position,sim.State.Ball.Position),0);Near(sim.State.Ball.Velocity.Length,0);
    Assert(sim.State.Ball.LastTouchId==target.Id && sim.State.Ball.LastKickerId==-1,"push falsely recorded pusher as ball toucher");
});
Test("side push leaves football at its exact original ground position throughout displacement", () => {
    var sim=PossessionFixture();var actor=sim.State.Players[0];var target=sim.State.Players[1];var position=sim.State.Ball.Position;
    actor.Position=new Vec2(0,2);actor.DashDirection=new Vec2(0,-1);TackleSystem.Resolve(sim,actor,target,new Vec2(0,-1));
    for(int tick=0;tick<25;tick++){Steps(sim,1);Near(Vec2.Distance(position,sim.State.Ball.Position),0);Assert(sim.State.Ball.OwnerId==-1,"displaced target or distant pusher received football");}
    Assert(target.Position.Y<-.9f,"push did not make space");
});
Test("pushed enemy cannot immediately reacquire the released ball", () => {
    var sim=PossessionFixture();var actor=sim.State.Players[0];var target=sim.State.Players[1];actor.DashDirection=new Vec2(0,-1);
    TackleSystem.Resolve(sim,actor,target,new Vec2(0,-1));target.Position=sim.State.Ball.Position;
    Assert(!sim.TryAcquireBallOnContact(target,target.Position),"displaced owner immediately recaught football");
    Assert(sim.State.Ball.OwnerId==-1,"displacement blocked nothing");
});
Test("released ball remains free until another physical contact and only then counts capture", () => {
    var sim=PossessionFixture();var actor=sim.State.Players[0];var target=sim.State.Players[1];actor.Position=new Vec2(0,3);actor.DashDirection=new Vec2(0,-1);
    TackleSystem.Resolve(sim,actor,target,new Vec2(0,-1));Steps(sim,25);Assert(sim.State.Ball.OwnerId==-1 && actor.Steals==0,"push acquired remotely");
    actor.Position=sim.State.Ball.Position+new Vec2(actor.Stats.Radius+sim.Rules.BallRadius+.02f,0);Steps(sim,1);
    Assert(sim.State.Ball.OwnerId==-1,"near pusher received free ball");
    actor.Position-=new Vec2(.02f,0);Steps(sim,1);Assert(sim.State.Ball.OwnerId==0 && actor.Steals==1,"real contact did not capture released ball");
});
Test("friendly push retains possession from front side and rear", () => {
    foreach(var relative in new[]{new Vec2(1,0),new Vec2(-1,0),new Vec2(0,1)})
    {
        var sim=PossessionFixture(allied:true);var actor=sim.State.Players[0];var target=sim.State.Players[1];actor.DashDirection=relative;
        TackleSystem.Resolve(sim,actor,target,relative);
        Assert(sim.State.Ball.OwnerId==target.Id && target.PushRemaining>0 && actor.Steals==0,"friendly push dropped or transferred ball");
        var offset=sim.State.Ball.Position-target.Position;Steps(sim,8);
        Assert(sim.State.Ball.OwnerId==target.Id && target.Position.Length>.3f,"friendly push lost carry or did not move");
        Near(Vec2.Distance(sim.State.Ball.Position-target.Position,offset),0,.002f);
    }
});
Test("real friendly dash collision pushes ball carrier and ball moves with teammate", () => {
    var sim=PossessionFixture(allied:true);var actor=sim.State.Players[0];var target=sim.State.Players[1];actor.Position=new Vec2(-1.6f,0);
    var before=sim.State.Ball.Position;var targetBefore=target.Position;
    Steps(sim,35,new PlayerCommand {Aim=new Vec2(1,0),TacklePressed=true});
    Assert(target.Position.X>.8f && sim.State.Ball.OwnerId==target.Id,"teammate dash did not push while preserving ball");
    Near(Vec2.Distance(sim.State.Ball.Position-before,target.Position-targetBefore),0,.003f);
});
Test("enemy rear push moves carrier and football together", () => {
    var sim=PossessionFixture();var actor=sim.State.Players[0];var target=sim.State.Players[1];actor.Position=new Vec2(-2,0);actor.DashDirection=new Vec2(1,0);
    var original=sim.State.Ball.Position;TackleSystem.Resolve(sim,actor,target,new Vec2(1,0));Steps(sim,25);
    Assert(sim.State.Ball.OwnerId==target.Id && target.Position.X>.9f && actor.Steals==0,"rear contact disarmed carrier");
    Near(Vec2.Distance(sim.State.Ball.Position-original,target.Position),0,.003f);
});
Test("rear protection rotates with carrier facing and does not depend on pusher aim", () => {
    foreach(var facing in new[]{new Vec2(1,0),new Vec2(0,1),new Vec2(-1,0),new Vec2(0,-1)})
    {
        var sim=PossessionFixture();var actor=sim.State.Players[0];var target=sim.State.Players[1];target.Facing=facing;actor.DashDirection=-facing;
        TackleSystem.Resolve(sim,actor,target,facing);Assert(sim.State.Ball.OwnerId==target.Id,"rear angle used pusher aim rather than carrier facing");
        sim=PossessionFixture();actor=sim.State.Players[0];target=sim.State.Players[1];target.Facing=facing;actor.DashDirection=facing;
        TackleSystem.Resolve(sim,actor,target,-facing);Assert(sim.State.Ball.OwnerId==-1,"front angle used pusher aim rather than carrier facing");
    }
});
Test("pushing without a ball only displaces character", () => {
    var sim=StarSim();EmptyField(sim);var actor=sim.State.Players[0];var target=sim.State.Players[1];var position=sim.State.Ball.Position;actor.DashDirection=new Vec2(1,0);
    TackleSystem.Resolve(sim,actor,target,new Vec2(-1,0));Steps(sim,25);
    Assert(target.Position.X>-14.5f && sim.State.Ball.OwnerId==-1,"off-ball push changed ownership or failed");Near(Vec2.Distance(position,sim.State.Ball.Position),0);
});
Test("front drop creates no catch or capture event until actual collision", () => {
    var sim=PossessionFixture();sim.State.MutableEvents.Clear();var actor=sim.State.Players[0];var target=sim.State.Players[1];actor.DashDirection=new Vec2(-1,0);
    TackleSystem.Resolve(sim,actor,target,new Vec2(-1,0));
    Assert(sim.State.Events.Any(e=>e.Kind==MatchEventKind.BallDropped) && !sim.State.Events.Any(e=>e.Kind==MatchEventKind.Catch||e.Kind==MatchEventKind.Steal),"push emitted an immediate possession event");
});
Test("a third player can collect ball released by someone else's push", () => {
    var sim=PossessionFixture(allied:true);var actor=sim.State.Players[3];var target=sim.State.Players[1];actor.DashDirection=new Vec2(0,-1);
    TackleSystem.Resolve(sim,actor,target,new Vec2(0,-1));var collector=sim.State.Players[4];collector.Position=sim.State.Ball.Position;
    Steps(sim,1);Assert(sim.State.Ball.OwnerId==collector.Id && actor.Steals==0 && collector.Steals==1,"pusher had priority without ball collision");
});
Test("bomb push uses explosion contact direction for rear carry and front release", () => {
    foreach(bool rear in new[]{false,true})
    {
        var sim=PossessionFixture();var actor=sim.State.Players[0];actor.Position=new Vec2(-8,4);var target=sim.State.Players[1];
        sim.State.MutableBombs.Add(new RemoteBomb {ActorId=0,Team=Team.Cyan,Position=new Vec2(rear?-2:2,0),Remaining=.001f,Radius=3});
        Steps(sim,1);Assert(target.PushRemaining>0 && sim.State.Ball.OwnerId==(rear?target.Id:-1),"explosion used caster position or wrong carry semantics");
    }
});
Test("displacement capture provenance is cleared by training reset and restart", () => {
    var sim=PossessionFixture();var actor=sim.State.Players[0];actor.DashDirection=new Vec2(-1,0);TackleSystem.Resolve(sim,actor,sim.State.Players[1],new Vec2(-1,0));
    Assert(sim.State.Ball.DroppedOwnerId==1,"drop provenance missing");sim.ResetTraining(false);Assert(sim.State.Ball.DroppedOwnerId==-1,"reset kept drop provenance");
    sim.SetupTrainingRestart(RestartKind.FreeKick);Assert(sim.State.Ball.DroppedOwnerId==-1,"set piece retained drop provenance");
});
Test("rear protection angle is finite and configurable", () => {
    foreach(float angle in new[]{float.NaN,float.PositiveInfinity,-1,91})
    {bool rejected=false;try{new MatchRules {RearPushCarryAngleDegrees=angle}.Validate();}catch(ArgumentException){rejected=true;}Assert(rejected,"invalid rear angle accepted");}
});

Test("initial ball contact receives even when fast ball is travelling away", () => {
    var sim=StarSim();EmptyField(sim);sim.Rules.PhysicsSubsteps=1;var p=sim.State.Players[0];p.Position=Vec2.Zero;
    sim.State.Ball.Position=new Vec2(p.Stats.Radius+sim.Rules.BallRadius-.001f,0);sim.State.Ball.Velocity=new Vec2(24,0);
    Steps(sim,1);Assert(sim.State.Ball.OwnerId==p.Id,"initial actual contact was lost when ball travelled away");
});

Test("standard front push training requires walking into released football", () => {
    var sim=StarSim();sim.GiveTrainingTargetBall();Steps(sim,26,new PlayerCommand {Aim=new Vec2(1,0),TacklePressed=true});
    Assert(sim.State.Ball.OwnerId!=1,"standard front push did not release carrier");
    var commands=new PlayerCommand[2];commands[0]=ContextOrder.Create(sim.State,sim.Rules,0,sim.State.Ball.Position,clickedBall:true);
    for(int tick=0;tick<100 && sim.State.Ball.OwnerId<0;tick++){sim.Step(commands);commands[0]=default;}
    Assert(sim.State.Ball.OwnerId==0,"standard released ball could not be acquired by collider contact");
    Steps(sim,1,new PlayerCommand {Aim=new Vec2(1,0),KickPressed=true,KickHeld=true});
    Assert(sim.State.Players[0].Charging,"actual receiver could not start charging");
});

// 0.16.1: held-ball interception, reception protection and map-derived carrying speed.
MatchSimulation InterceptionFixture(bool allied=false)
{
    var sim=PossessionFixture(allied);Steps(sim,16);return sim;
}
foreach(var approach in new[]{new Vec2(1,0),new Vec2(0,1),new Vec2(0,-1)})
Test($"walking into enemy held ball steals without tackling from {approach}", () => {
    var sim=InterceptionFixture();var p=sim.State.Players[0];var owner=sim.State.Players[1];
    var ball=sim.State.Ball.Position;var radius=p.Stats.Radius+sim.Rules.BallRadius;
    p.Position=ball+approach*(radius+.15f);p.TackleCooldown=99;
    Steps(sim,10,new PlayerCommand {Move=-approach,Aim=-approach});
    Assert(sim.State.Ball.OwnerId==p.Id && p.Steals==1,"actual held-ball contact did not steal");
    Near(p.Health,100);Assert(p.TackleCooldown>98 && owner.PushRemaining==0,"interception required push or paid health");
    Assert(sim.State.Ball.LastTouchId==p.Id,"interception did not update last physical toucher");
});
Test("right-click held football obtains possession without any tackle input", () => {
    foreach(float scale in new[]{1f,1.6f,2.5f}) {
    var sim=InterceptionFixture();sim.Rules.MovementSpeedScale=scale;var p=sim.State.Players[0];p.Position=sim.State.Ball.Position+new Vec2(3,0);
    var chase=ContextOrder.Create(sim.State,sim.Rules,0,sim.State.Ball.Position,clickedBall:true);
    for(int tick=0;tick<150 && p.Steals==0;tick++) Steps(sim,1,chase);
    Assert(p.Steals>=1 && sim.State.Ball.OwnerId==p.Id,"ball pursuit never intercepted held football at scale "+scale);Near(p.Health,100);Near(p.TackleCooldown,0); }
});
Test("standing outside held ball surface does not steal and football does not evade", () => {
    var sim=InterceptionFixture();var p=sim.State.Players[0];var ball=sim.State.Ball.Position;
    p.Position=ball+new Vec2(p.Stats.Radius+sim.Rules.BallRadius+.01f,0);Steps(sim,30);
    Assert(sim.State.Ball.OwnerId==1 && p.Steals==0,"nearby body received without touching");Near(Vec2.Distance(ball,sim.State.Ball.Position),0);
});
Test("touching carrier from behind cannot reach their front football", () => {
    var sim=InterceptionFixture();var p=sim.State.Players[0];var owner=sim.State.Players[1];
    p.Position=owner.Position-new Vec2(p.Stats.Radius+owner.Stats.Radius,0);
    Steps(sim,60,new PlayerCommand {Move=new Vec2(1,0)});
    Assert(sim.State.Ball.OwnerId==owner.Id && p.Steals==0,"body contact stole a football on the far side");
});
Test("teammate touching held football leaves healthy teammate possession intact", () => {
    var sim=InterceptionFixture(allied:true);var p=sim.State.Players[0];var owner=sim.State.Players[1];
    p.Position=sim.State.Ball.Position+new Vec2(p.Stats.Radius+sim.Rules.BallRadius,0);Steps(sim,60);
    Assert(sim.State.Ball.OwnerId==owner.Id && p.Steals==0,"friendly automatic reception stole teammate ball");
});
foreach(int substeps in new[]{1,4,16})
Test($"held contact lock lasts 0.25 seconds with {substeps} physics substeps", () => {
    var sim=PossessionFixture();sim.Rules.PhysicsSubsteps=substeps;var p=sim.State.Players[0];
    p.Position=sim.State.Ball.Position+new Vec2(p.Stats.Radius+sim.Rules.BallRadius,0);
    Steps(sim,13);Assert(sim.State.Ball.OwnerId==1 && p.Steals==0,"received before protection expired");
    Steps(sim,2);Assert(sim.State.Ball.OwnerId==0 && p.Steals==1,"persistent surface contact did not receive at expiry");
    Near(sim.State.Ball.PossessionContactRemaining,sim.Rules.PossessionContactLock,.017f);
    Steps(sim,10);Assert(sim.State.Ball.OwnerId==0 && p.Steals==1,"new owner immediately lost ball in later substep");
});
foreach(string condition in new[]{"stunned","pushed","dead","respawned"})
Test($"{condition} player cannot automatically intercept held football", () => {
    var sim=InterceptionFixture();var p=sim.State.Players[0];
    p.Position=sim.State.Ball.Position+new Vec2(p.Stats.Radius+sim.Rules.BallRadius,0);
    if(condition=="stunned")p.Stun=1;
    if(condition=="pushed")p.PushRemaining=1;
    if(condition=="dead"){p.Health=0;p.RespawnRemaining=5;}
    if(condition=="respawned"){p.Health=0;p.RespawnRemaining=.001f;}
    Steps(sim,1);Assert(sim.State.Ball.OwnerId==1 && p.Steals==0,"ineligible receiver stole ball");
});
Test("full protection blocks direct held-ball interception", () => {
    var sim=InterceptionFixture();var p=sim.State.Players[0];sim.State.Owner.FullProtection=1;
    p.Position=sim.State.Ball.Position+new Vec2(p.Stats.Radius+sim.Rules.BallRadius,0);Steps(sim,30);
    Assert(sim.State.Ball.OwnerId==1,"complete protection failed during interception");
});
Test("fast sideways player intercepts held ball on swept path before passing it", () => {
    var sim=InterceptionFixture();sim.Rules.PhysicsSubsteps=1;var p=sim.State.Players[0];
    p.Stats.Speed=600;p.Position=sim.State.Ball.Position+new Vec2(.2f,-3);
    Steps(sim,1,new PlayerCommand {Move=new Vec2(0,1)});
    Assert(sim.State.Ball.OwnerId==p.Id && p.Steals==1 && p.Position.Y>3,"held-ball collision tunneled at high speed");
});
Test("fast carrier translation exposes held ball along its continuous path", () => {
    var sim=InterceptionFixture();sim.Rules.PhysicsSubsteps=1;var p=sim.State.Players[0];var owner=sim.State.Players[1];
    p.Position=sim.State.Ball.Position+new Vec2(.6f,3);owner.Stats.Speed=600;
    var commands=new PlayerCommand[2];commands[1]=new PlayerCommand {Move=new Vec2(0,1)};sim.Step(commands);
    Assert(owner.Position.Y>5 && sim.State.Ball.OwnerId==p.Id && p.Steals==1,"moving held ball skipped receiver between endpoints");
});
Test("earliest of two opponents receives moving held ball only once", () => {
    var sim=InterceptionFixture(allied:true);sim.Rules.PhysicsSubsteps=1;var owner=sim.State.Players[1];
    var first=sim.State.Players[4];var later=sim.State.Players[3];var ball=sim.State.Ball.Position;
    first.Position=ball+new Vec2(.6f,2);later.Position=ball+new Vec2(.6f,5);owner.Stats.Speed=600;
    var commands=new PlayerCommand[6];commands[1]=new PlayerCommand {Move=new Vec2(0,1)};sim.Step(commands);
    Assert(sim.State.Ball.OwnerId==first.Id && first.Steals==1 && later.Steals==0,"slot order overrode earliest collision");
    Assert(sim.State.Events.Count(e=>e.Kind==MatchEventKind.Steal)==1,"one held-ball contact emitted multiple steals");
});
Test("ordinary pass reception starts contact protection independently of kicker lock", () => {
    var sim=StarSim();EmptyField(sim);var p=sim.State.Players[0];p.Position=Vec2.Zero;sim.State.Ball.Position=p.Position;
    Steps(sim,1);Assert(sim.State.Ball.OwnerId==p.Id && sim.State.Ball.PossessionContactRemaining>.23f,"free-ball reception omitted contact protection");
    Steps(sim,1,new PlayerCommand {Aim=new Vec2(0,1),KickPressed=true,KickReleased=true});
    Near(sim.State.Ball.PossessionContactRemaining,0);Assert(sim.State.Ball.SelfCatchRemaining>0,"kicking kept contact lock or lost self-catch lock");
    sim.ResetTraining(true);Assert(sim.State.Ball.PossessionContactRemaining>0,"setup reception lock absent");
    sim.ResetTraining(false);Near(sim.State.Ball.PossessionContactRemaining,0);
    sim.SetupTrainingRestart(RestartKind.FreeKick);Near(sim.State.Ball.PossessionContactRemaining,0);
});
Test("turning does not sweep football through the carrier body to a distant receiver", () => {
    var sim=InterceptionFixture();var p=sim.State.Players[0];var owner=sim.State.Players[1];
    p.Position=owner.Position+new Vec2(0,p.Stats.Radius+owner.Stats.Radius+.02f);
    var commands=new PlayerCommand[2];commands[1]=new PlayerCommand {Aim=new Vec2(-1,0)};sim.Step(commands);
    Assert(sim.State.Ball.OwnerId==owner.Id && p.Steals==0,"rotation chord generated false ball contact");
});
Test("reception protection clears when pushed ball is released", () => {
    var sim=PossessionFixture();var p=sim.State.Players[0];var owner=sim.State.Players[1];
    Assert(sim.State.Ball.PossessionContactRemaining>0,"fixture lacks reception lock");
    p.DashDirection=new Vec2(-1,0);TackleSystem.Resolve(sim,p,owner,new Vec2(-1,0));
    Near(sim.State.Ball.PossessionContactRemaining,0);p.Position=sim.State.Ball.Position+new Vec2(p.Stats.Radius+sim.Rules.BallRadius,0);
    Steps(sim,1);Assert(sim.State.Ball.OwnerId==p.Id,"previous contact lock prevented free-ball reception after push");
});
Test("holding football moves at eighty percent of ordinary running speed", () => {
    var carrying=StarSim();carrying.ResetTraining(true);var running=StarSim();
    var origin=carrying.State.Players[0].Position;Steps(carrying,60,new PlayerCommand {Move=new Vec2(0,1)});
    Steps(running,60,new PlayerCommand {Move=new Vec2(0,1)});
    Near(Vec2.Distance(carrying.State.Players[0].Position,origin),Vec2.Distance(running.State.Players[0].Position,origin)*.8f,.002f);
});
Test("automatic possession tuning rejects nonfinite or invalid values", () => {
    foreach(float value in new[]{float.NaN,float.PositiveInfinity,-.1f})
    {bool rejected=false;try{new MatchRules {PossessionContactLock=value}.Validate();}catch(ArgumentException){rejected=true;}Assert(rejected,"invalid reception lock accepted");}
    foreach(float value in new[]{float.NaN,float.PositiveInfinity,0,1.01f})
    {bool rejected=false;try{new MatchRules {PossessionSpeed=value}.Validate();}catch(ArgumentException){rejected=true;}Assert(rejected,"invalid carry speed accepted");}
});

Test("default movement tuning speeds up reference roster without changing possession ratio",()=> {
 var sim=new MatchSimulation(new MatchRules(),StarStrikerLoadouts.CreateRoster(),CharacterId.Marine,MatchMode.Training);sim.ResetTraining(false);
 var p=sim.State.Players[0];p.Position=Vec2.Zero;sim.State.Players[1].Position=new Vec2(15,12);sim.State.Ball.Position=new Vec2(-15,12);
 Steps(sim,60,new PlayerCommand{Move=new Vec2(1,0)});Near(p.Position.X,2.35f*1.6f,.002f);
 sim.ResetTraining(true);p.Position=Vec2.Zero;sim.State.Players[1].Position=new Vec2(15,12);
 Steps(sim,60,new PlayerCommand{Move=new Vec2(1,0)});Near(p.Position.X,2.35f*1.6f*.8f,.002f);
});
Test("passive health recovers two per second and clamps without reviving a downed player",()=> {
 var sim=new MatchSimulation(new MatchRules(),StarStrikerLoadouts.CreateRoster(),CharacterId.Marine,MatchMode.Training);sim.ResetTraining(false);
 var p=sim.State.Players[0];p.Health=60;Steps(sim,120);Near(p.Health,64,.002f);Near(p.HealingDone,0);
 p.Health=99.99f;Steps(sim,2);Near(p.Health,100);
 p.Health=0;p.RespawnRemaining=2;Steps(sim,60);Near(p.Health,0);Assert(p.IsDead,"passive recovery revived downed player");
 sim.State.Phase=MatchPhase.SetPiece;p.Health=60;p.RespawnRemaining=0;Steps(sim,20);Near(p.Health,60);
});
Test("default faster navigation reaches a short destination without oscillating",()=> {
 var sim=new MatchSimulation(new MatchRules(),StarStrikerLoadouts.CreateRoster(),CharacterId.Marine,MatchMode.Training);sim.ResetTraining(false);
 var p=sim.State.Players[0];p.Position=Vec2.Zero;sim.State.Players[1].Position=new Vec2(15,12);sim.State.Ball.Position=new Vec2(-15,12);
 Steps(sim,1,ContextOrder.Create(sim.State,sim.Rules,0,new Vec2(1.03f,0)));Steps(sim,60);
 Assert(Vec2.Distance(p.Position,new Vec2(1.03f,0))<.12f && p.Order==UnitOrderKind.None,"increased speed overshot short order");
});
Test("left mouse never kicks while keyboard hold and release produce kick edges",()=> {
 var gesture=new GameplayGesture();var input=new PlayerCommand();gesture.Apply(ref input,CharacterId.Marine,new GameplayButtons{OnField=true,LeftDown=true});
 Assert(!input.KickPressed && !input.KickReleased,"left mouse still shoots");
 gesture.Apply(ref input,CharacterId.Marine,new GameplayButtons{OnField=true,KickDown=true,KickHeld=true});Assert(input.KickPressed && input.KickHeld,"V hold did not charge");
 input=input.WithoutEdges();gesture.Apply(ref input,CharacterId.Marine,new GameplayButtons{OnField=true,KickUp=true});Assert(input.KickReleased && !input.KickHeld,"V release did not shoot");
 input=input.WithoutEdges();gesture.Apply(ref input,CharacterId.Marine,new GameplayButtons{OnField=true});Assert(!input.KickReleased,"release edge repeated");
});
Test("direction skill waits for mouse confirmation without charging a held football",()=> {
 var gesture=new GameplayGesture();var input=new PlayerCommand();gesture.Apply(ref input,CharacterId.Thor,new GameplayButtons{OnField=true,EDown=true});
 Assert(gesture.Targeting && !input.EPressed && !input.KickPressed,"E fired before target confirmation");
 gesture.Apply(ref input,CharacterId.Thor,new GameplayButtons{OnField=false,LeftDown=true});Assert(gesture.Targeting && !input.EPressed,"UI click selected a skill target");
 gesture.Apply(ref input,CharacterId.Thor,new GameplayButtons{OnField=true,LeftDown=true});Assert(!gesture.Targeting && input.EPressed && !input.KickPressed,"left confirm incorrectly kicked");
});
Test("cancel removes skill selection and keyboard charge and push remains a separate key",()=> {
 var gesture=new GameplayGesture();var input=new PlayerCommand();gesture.Apply(ref input,CharacterId.Thor,new GameplayButtons{OnField=true,EDown=true});
 gesture.Apply(ref input,CharacterId.Thor,new GameplayButtons{Cancel=true});Assert(input.Cancel && !gesture.Targeting,"cancel retained selected skill");
 input=default;gesture.Apply(ref input,CharacterId.Marine,new GameplayButtons{OnField=true,KickDown=true,KickHeld=true});
 gesture.Apply(ref input,CharacterId.Marine,new GameplayButtons{Cancel=true});input=default;gesture.Apply(ref input,CharacterId.Marine,new GameplayButtons{OnField=true,KickUp=true,PushDown=true});
 Assert(input.TacklePressed && !input.KickReleased && !input.KickPressed,"push or canceled charge affected kick");
});
string reportDir = args.Length > 0 ? args[0] : Path.GetFullPath("reports");
Directory.CreateDirectory(reportDir);
File.WriteAllText(Path.Combine(reportDir, "core-tests.json"), JsonSerializer.Serialize(new { passed = failed == 0, total = results.Count, failed, results }, new JsonSerializerOptions { WriteIndented = true }));
return failed == 0 ? 0 : 1;
