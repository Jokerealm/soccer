using System.Collections.Generic;
using Soccer.Core;
using UnityEngine;

namespace Soccer.Runtime
{
    public sealed class MatchView : MonoBehaviour
    {
        public Transform ActorRoot;
        public Sprite BallSprite;
        public Sprite ShadowSprite;
        public Sprite CyanMarker;
        public Sprite AmberMarker;
        private readonly List<PlayerView> players = new List<PlayerView>();
        private readonly List<LineRenderer> effects = new List<LineRenderer>();
        private readonly List<LineRenderer> projectiles = new List<LineRenderer>();
        private readonly List<BombView> bombs = new List<BombView>();
        public int ActiveBombViews => bombs.FindAll(b => b.gameObject.activeSelf).Count;
        private SpriteRenderer ball;
        private BallModelView ballModel;
        public BallModelView BallModel => ballModel;
        public OpeningShieldView OpeningShield { get; private set; }
        private ArenaParticles combatFx;
        public KickEffectsView KickEffects { get; private set; }
        private PremiumArenaView arena;
        public int ActiveFieldParticles => combatFx == null ? 0 : combatFx.ActiveCount;
        public IReadOnlyList<PlayerView> PlayerViews => players;
        private SpriteRenderer ballShadow;
        private LineRenderer leftCone;
        private LineRenderer rightCone;
        private LineRenderer preparation;
        private LineRenderer shotGuide;
        private LineRenderer orderMarker, navigationLine, restartCircle;
        private readonly List<Pulse> pulses = new List<Pulse>();
        private readonly List<DamageNumber> numbers = new List<DamageNumber>();
        public int ActiveDamageNumbers => numbers.FindAll(n => n.Age < 0.55f).Count;
        public bool FeedbackEnabled { get; private set; } = true;
        public int LocalPlayerId { get; set; }
        private sealed class Pulse
        {
            public LineRenderer Line;
            public Vec2 Center;
            public Color Color;
            public float Age, Duration, Radius;
        }
        private sealed class DamageNumber
        {
            public TextMesh Text;
            public Vec2 Center;
            public float Age = 1, Amount;
            public int TargetId;
            public Color Color;
        }

        private void Awake()
        {
            FeedbackEnabled = PlayerPrefs.GetInt("soccer.feedback", 1) != 0;
            arena=gameObject.AddComponent<PremiumArenaView>();
            var fx=new GameObject("Combat Particle Pool"); fx.transform.SetParent(transform,false);
            combatFx=fx.AddComponent<ArenaParticles>(); combatFx.Initialize();
            var kicks=new GameObject("Football feedback pool");kicks.transform.SetParent(transform,false);
            KickEffects=kicks.AddComponent<KickEffectsView>();KickEffects.Initialize();
        }
        public void SetFeedbackEnabled(bool enabled)
        { FeedbackEnabled = enabled; PlayerPrefs.SetInt("soccer.feedback", enabled ? 1 : 0); PlayerPrefs.Save(); if (!enabled) ResetFeedback(); }
        public void ResetFeedback()
        {
            combatFx?.Clear();
            KickEffects?.Clear();
            foreach (var pulse in pulses) { pulse.Age = pulse.Duration; pulse.Line.enabled = false; }
            foreach (var player in players) player.ClearFeedback();
            foreach (var number in numbers) { number.Age = 1; number.Text.gameObject.SetActive(false); }
        }

        public void Bind(MatchState state, CharacterDefinition[] roster)
        {
            Clear();
            KickEffects.LocalPlayerId = LocalPlayerId;
            arena.Dynamics?.ResetMatch();
            foreach (var p in state.Players)
            {
                var definition = System.Array.Find(roster, d => d.Stats.Id == p.Stats.Id);
                var go = new GameObject($"{p.Id} {p.Stats.Name} ({p.Team}){(p.Id == LocalPlayerId ? " Local" : " AI")}");
                go.transform.SetParent(ActorRoot, false);
                var view = go.AddComponent<PlayerView>();
                view.Initialize(definition, p.Team, state.Players[LocalPlayerId].Team, p.Id == LocalPlayerId, CyanMarker, AmberMarker, ShadowSprite);
                players.Add(view);
            }
            ball = CreateBall("Ball Anchor", BallSprite, 0.48f, 700);
            ball.enabled = false;
            var model = new GameObject("3D Football"); model.transform.SetParent(ActorRoot,false);
            ballModel = model.AddComponent<BallModelView>(); ballModel.Initialize();
            OpeningShield=new GameObject("Opening football shield").AddComponent<OpeningShieldView>(); OpeningShield.Initialize(ActorRoot);
            ballShadow = CreateBall("Ball Shadow", ShadowSprite, 0.55f, 698);
            ballShadow.transform.localRotation = Quaternion.Euler(90,0,0);
            ballShadow.transform.localScale = new Vector3(0.55f, 0.35f, 1);
            ballShadow.color = new Color(1,1,1,0.38f);
            leftCone = ViewLines.Create(ActorRoot, "Carrier Rear Left Boundary", 0.025f, new Color(1, 0.8f, 0.3f, 0.7f), 650);
            rightCone = ViewLines.Create(ActorRoot, "Carrier Rear Right Boundary", 0.025f, new Color(1, 0.8f, 0.3f, 0.7f), 650);
            preparation = ViewLines.Create(ActorRoot, "Skill Preview", 0.3f, new Color(0.26f, 0.82f, 0.78f, 0.3f), 20);
            shotGuide = ViewLines.Create(ActorRoot, "Shot Direction Preview", 0.035f, new Color(1, 0.9f, 0.5f, 0.45f), 45);
            shotGuide.enabled = false;
            orderMarker = ViewLines.Create(ActorRoot, "Right click destination", .035f, new Color(.5f, 1, .65f, .8f), 46);
            navigationLine = ViewLines.Create(ActorRoot, "Navigation route", .025f, new Color(.5f, 1, .65f, .4f), 46);
            restartCircle = ViewLines.Create(ActorRoot, "Set piece exclusion", .045f, new Color(1, .78f, .35f, .65f), 46);
            orderMarker.enabled = navigationLine.enabled = restartCircle.enabled = false;
        }

        private void RenderOrders(MatchState state, MatchRules rules, bool debug)
        {
            var p = state.Players[LocalPlayerId];
            bool ordered = !p.IsDead && p.Order != UnitOrderKind.None;
            orderMarker.enabled = ordered;
            if (ordered)
            {
                var point = p.Order == UnitOrderKind.ChaseBall ? state.Ball.Position : p.OrderPosition;
                if (p.OrderTargetId >= 0 && (p.Order == UnitOrderKind.Attack || p.Order == UnitOrderKind.Follow)) point = state.Players[p.OrderTargetId].Position;
                var color = p.Order == UnitOrderKind.Attack ? new Color(1,.35f,.2f,.8f) : new Color(.5f,1,.65f,.8f);
                orderMarker.startColor = orderMarker.endColor = color;
                Circle(orderMarker,point,p.Order == UnitOrderKind.Attack ? .7f : .35f);
            }
            navigationLine.enabled = false;
            if (navigationLine.enabled)
            {
                navigationLine.positionCount = p.NavigationPath.Count + 1;
                navigationLine.SetPosition(0,ArenaProjection.ToWorld(p.Position,.035f));
                for (int i=0;i<p.NavigationPath.Count;i++) navigationLine.SetPosition(i+1,ArenaProjection.ToWorld(p.NavigationPath[i],.035f));
            }
            restartCircle.enabled = state.Phase == MatchPhase.SetPiece;
            if (restartCircle.enabled) Circle(restartCircle,state.RestartPosition,rules.SetPieceDistance);
        }
        private static void Circle(LineRenderer line,Vec2 center,float radius)
        {
            line.positionCount=65;
            for(int i=0;i<65;i++)
            {
                float angle=i*Mathf.PI*2/64;
                line.SetPosition(i,ArenaProjection.ToWorld(center+new Vec2(Mathf.Cos(angle),Mathf.Sin(angle))*radius,.04f));
            }
        }

        private SpriteRenderer CreateBall(string label, Sprite sprite, float size, int order)
        {
            var go = new GameObject(label);
            go.transform.SetParent(ActorRoot, false);
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = order;
            renderer.transform.localScale = Vector3.one * size;
            return renderer;
        }

        public void Clear()
        {
            combatFx?.Clear();
            KickEffects?.Clear();
            arena.Dynamics?.ResetMatch();
            foreach (Transform child in ActorRoot) Destroy(child.gameObject);
            players.Clear(); effects.Clear(); projectiles.Clear(); bombs.Clear(); pulses.Clear(); numbers.Clear();
        }

        public void Render(MatchState state, MatchRules rules, float alpha, bool debug, bool animate, Vec2 localAim = default, bool targetSelection = false)
        {
            arena.Dynamics?.Render(state,animate);
            if(arena.Dynamics!=null) arena.Details?.Render(arena.Dynamics.PresentationTime);
            OpeningShield?.Render(state,rules,animate);
            combatFx?.SetAnimating(animate);
            for (int i = 0; i < players.Count; i++)
            {
                var p = state.Players[i];
                var display = p.PreviousPosition + (p.Position - p.PreviousPosition) * alpha;
                players[i].Render(p, display, i == LocalPlayerId, state.Ball.OwnerId == i, debug, rules, animate,
                    i == LocalPlayerId && p.Dash == DashKind.None && localAim.LengthSquared > 0.001f ? localAim.Normalized : p.Facing, FeedbackEnabled);
            }
            var b = state.Ball;
            while (bombs.Count < state.Bombs.Count)
            {
                var go = new GameObject("Remote Bomb Warning"); go.transform.SetParent(ActorRoot, false);
                var view = go.AddComponent<BombView>(); view.Initialize(); bombs.Add(view);
            }
            for (int i = 0; i < bombs.Count; i++)
            {
                bombs[i].gameObject.SetActive(i < state.Bombs.Count);
                if (i < state.Bombs.Count) bombs[i].Render(state.Bombs[i], state.Ball);
            }
            var ballPosition = b.PreviousPosition + (b.Position - b.PreviousPosition) * alpha;
            float ballHeight = b.FlightMode==BallFlightMode.SkillOverhead ? Mathf.Lerp(b.PreviousHeight,b.Height,alpha) : 0;
            ballModel.Render(ballPosition, animate,ballHeight,rules.BallRadius);
            RenderOrders(state,rules,debug);
            ball.transform.position = ballModel.transform.position;
            ballShadow.transform.position = ArenaProjection.ToWorld(ballPosition,0.008f);
            float shadowScale = 1+ballHeight*0.16f;
            ballShadow.transform.localScale = new Vector3(0.55f*shadowScale,0.35f*shadowScale,1);
            ballShadow.color = new Color(1,1,1,0.38f/(1+ballHeight*0.65f));
            KickEffects?.Render(state,rules,ballModel.transform.position,animate,FeedbackEnabled);
            while (effects.Count < state.Effects.Count)
                effects.Add(ViewLines.Create(ActorRoot, "Field Effect", 0.1f, Color.white, 30));
            for (int i = 0; i < effects.Count; i++)
            {
                var line = effects[i];
                line.enabled = i < state.Effects.Count;
                if (!line.enabled) continue;
                var e = state.Effects[i];
                bool lane = e.Kind == FieldEffectKind.SpeedLane;
                var direction = lane ? e.Direction : new Vec2(-e.Direction.Y, e.Direction.X);
                var start = lane ? e.Position : e.Position - direction * e.Length / 2;
                var end = lane ? e.Position + direction * e.Length : e.Position + direction * e.Length / 2;
                line.SetPosition(0, ArenaProjection.ToWorld(start));
                line.SetPosition(1, ArenaProjection.ToWorld(end));
                line.startWidth = line.endWidth = lane ? e.Width * ArenaProjection.GroundYScale : 0.14f;
                var color = e.Team == Team.Cyan ? new Color(0.26f, 0.82f, 0.78f) : new Color(0.96f, 0.66f, 0.27f);
                color.a = e.Windup > 0 ? 0.2f : lane ? 0.4f : 0.95f;
                line.startColor = line.endColor = color;
            }
            var local = state.Players[LocalPlayerId];
            while (projectiles.Count < state.Projectiles.Count)
            {
                var line = ViewLines.Create(ActorRoot, "Combat Projectile", 0.09f, Color.white, 720);
                line.positionCount = 17; projectiles.Add(line);
            }
            for (int i = 0; i < projectiles.Count; i++)
            {
                var line = projectiles[i]; line.enabled = i < state.Projectiles.Count;
                if (!line.enabled) continue;
                var shot = state.Projectiles[i];
                var color = shot.Slot == SkillSlot.Q ? new Color(1, 0.38f, 0.78f) :
                    shot.Team == Team.Cyan ? new Color(0.35f, 1, 0.93f) : new Color(1, 0.68f, 0.3f);
                line.startColor = line.endColor = color;
                for (int n = 0; n < 17; n++)
                {
                    float angle = n * Mathf.PI * 2 / 16;
                    line.SetPosition(n, ArenaProjection.ToWorld(shot.Position) + ArenaProjection.VisualUp * 0.35f +
                        ArenaProjection.VisualOffset(Mathf.Cos(angle), Mathf.Sin(angle)) * shot.Radius);
                }
            }
            var aim = local.Dash == DashKind.None && localAim.LengthSquared > 0.001f ? localAim.Normalized : local.Facing;
            var carrier = state.Owner;
            leftCone.enabled = rightCone.enabled = carrier != null && !carrier.IsDead && debug && state.Mode == MatchMode.Training;
            if (leftCone.enabled)
            {
                var origin = ArenaProjection.ToWorld(carrier.Position,.03f);
                float angle = rules.RearPushCarryAngleDegrees * Mathf.Deg2Rad;
                leftCone.SetPosition(0, origin);
                leftCone.SetPosition(1, origin + ArenaProjection.ToWorld(Vec2.Rotate(-carrier.Facing, angle) * rules.TackleDistance));
                rightCone.SetPosition(0, origin);
                rightCone.SetPosition(1, origin + ArenaProjection.ToWorld(Vec2.Rotate(-carrier.Facing, -angle) * rules.TackleDistance));
            }
            preparation.enabled = local.PreviewingE || targetSelection;
            if (preparation.enabled)
            {
                preparation.SetPosition(0, ArenaProjection.ToWorld(local.Position));
                float reach = local.Stats.Id == CharacterId.Thor ? local.Stats.ReferenceSkills.ThorDashDistance : local.Stats.LaneLength;
                preparation.SetPosition(1, ArenaProjection.ToWorld(local.Position + aim * reach));
                preparation.startWidth = preparation.endWidth = local.Stats.LaneWidth * ArenaProjection.GroundYScale;
            }
            RenderShotGuide(state, rules, alpha, aim);
            RenderPulses(animate ? Mathf.Min(Time.unscaledDeltaTime, 0.1f) : 0);
            RenderNumbers(animate ? Mathf.Min(Time.unscaledDeltaTime, 0.1f) : 0);
        }

        private void RenderShotGuide(MatchState state, MatchRules rules, float alpha, Vec2 aim)
        {
            var p = state.Players[LocalPlayerId];
            shotGuide.enabled = p.Charging && state.Ball.OwnerId == LocalPlayerId && p.Dash == DashKind.None && state.Phase == MatchPhase.Playing;
            if (!shotGuide.enabled) return;
            float max = p.Preparation == ShotPreparation.Power ? p.Stats.PowerChargeSeconds : rules.ChargeSeconds;
            float charge = p.Charging ? Mathf.Clamp01(p.ChargeTime / max) : 0;
            float speed = Mathf.Lerp(rules.TapKickSpeed, rules.FullKickSpeed, charge) * Mathf.Sqrt(p.Stats.KickDistance *
                (p.Preparation == ShotPreparation.Power ? p.Stats.PowerDistance : 1));
            speed = Mathf.Min(speed, rules.MaxBallSpeed);
            float distance = Mathf.Min(8, speed * speed / (2 * rules.BallDeceleration));
            var position = p.PreviousPosition + (p.Position - p.PreviousPosition) * alpha + aim * (p.Stats.Radius + rules.BallRadius + rules.FootGap);
            float signed = Mathf.Atan2(Vec2.Cross(p.PreparationFacing, aim), Vec2.Dot(p.PreparationFacing, aim)) * Mathf.Rad2Deg;
            float curve = p.Preparation == ShotPreparation.Curve && Mathf.Abs(signed) > p.Stats.CurveDeadZoneDegrees
                ? Mathf.Sign(signed) * p.Stats.CurveDegrees * Mathf.Deg2Rad / p.Stats.CurveSeconds : 0;
            shotGuide.positionCount = 25;
            var direction = aim;
            float elapsed = 0;
            for (int i = 0; i < 25; i++)
            {
                shotGuide.SetPosition(i, ArenaProjection.ToWorld(position) + Vector3.up * 0.025f);
                float step = distance / 24;
                float dt = step / Mathf.Max(1, speed);
                if (elapsed < p.Stats.CurveSeconds) direction = Vec2.Rotate(direction, curve * Mathf.Min(dt, p.Stats.CurveSeconds - elapsed));
                elapsed += dt; speed = Mathf.Max(0, speed - rules.BallDeceleration * dt);
                position += direction * step;
                position.X = Mathf.Clamp(position.X, -rules.FieldWidth / 2, rules.FieldWidth / 2);
                position.Y = Mathf.Clamp(position.Y, -rules.FieldHeight / 2, rules.FieldHeight / 2);
            }
            Color color = Color.Lerp(new Color(0.26f, 0.82f, 0.78f, 0.65f), new Color(1, 0.8f, 0.3f, 0.85f), charge);
            shotGuide.startColor = color; color.a = 0.03f; shotGuide.endColor = color;
        }

        public void PlayEvent(MatchEvent e, MatchState state)
        {
            if(e.Kind==MatchEventKind.Goal) arena.Dynamics?.Goal((Team)e.TargetId);
            if (!FeedbackEnabled) return;
            combatFx?.Event(e,state);
            if(e.Kind==MatchEventKind.Kick) KickEffects?.Kick(e,state);
            if (e.Kind == MatchEventKind.Weapon && e.ActorId >= 0) players[e.ActorId].Recoil(state.Players[e.ActorId].AttackFacing);
            if (e.Kind == MatchEventKind.Damage && e.TargetId >= 0)
            {
                players[e.TargetId].Impact(state.Players[e.TargetId].Position - state.Players[e.ActorId].Position, e.Strength <= 0);
                ShowDamage(e);
            }
            bool hit = e.Kind == MatchEventKind.Steal || e.Kind == MatchEventKind.Push || e.Kind == MatchEventKind.Damage ||
                e.Kind == MatchEventKind.Stunned || e.Kind == MatchEventKind.Knockout;
            if (hit && e.Kind != MatchEventKind.Damage && e.Kind != MatchEventKind.Stunned && e.Kind != MatchEventKind.Knockout && e.TargetId >= 0)
                players[e.TargetId].Impact(state.Players[e.TargetId].Position - state.Players[e.ActorId].Position, false);
            if (e.Kind == MatchEventKind.Skill && e.ActorId >= 0) players[e.ActorId].Flash(new Color(0.6f, 1, 1));
            if (e.Kind != MatchEventKind.Kick && e.Kind != MatchEventKind.Catch && !hit &&
                e.Kind != MatchEventKind.Skill && e.Kind != MatchEventKind.Goal &&
                e.Kind != MatchEventKind.Respawn && e.Kind != MatchEventKind.Support && e.Kind != MatchEventKind.Explosion) return;
            var center = e.Kind == MatchEventKind.Explosion ? e.Position : e.Kind == MatchEventKind.Goal ? state.Ball.Position :
                hit || e.Kind == MatchEventKind.Support ? state.Players[e.TargetId].Position : state.Players[e.ActorId].Position;
            var color = hit ? new Color(1, 0.68f, 0.3f) : e.Kind == MatchEventKind.Kick ? new Color(1, 0.92f, 0.65f) : new Color(0.3f, 0.95f, 0.85f);
            Pulse pulse = pulses.Find(p => p.Age >= p.Duration);
            if (pulse == null)
            {
                if (pulses.Count >= 10) pulse = pulses[0];
                else
                {
                    pulse = new Pulse { Line = ViewLines.Create(ActorRoot, "Action Pulse", 0.045f, color, 710) };
                    pulse.Line.positionCount = 33; pulses.Add(pulse);
                }
            }
            pulse.Center = center; pulse.Color = color; pulse.Age = 0;
            pulse.Duration = e.Kind == MatchEventKind.Goal || e.Kind == MatchEventKind.Explosion ? 0.65f : 0.24f;
            pulse.Radius = e.Kind == MatchEventKind.Explosion ? e.Strength : e.Kind == MatchEventKind.Goal ? 2.5f :
                e.Kind == MatchEventKind.Skill && e.Slot == SkillSlot.E ? state.Players[e.ActorId].Stats.Loadout.SupportRadius : hit ? 0.9f : 0.7f;
            pulse.Line.enabled = true;
        }

        private void ShowDamage(MatchEvent e)
        {
            // 合并同一次散射的数字，避免三发命中遮住血条和球。
            DamageNumber number = numbers.Find(n => n.TargetId == e.TargetId && n.Age < 0.1f && (n.Amount > 0) == (e.Strength > 0));
            bool merge = number != null;
            if (!merge) number = numbers.Find(n => n.Age >= 0.55f);
            if (number == null)
            {
                if (numbers.Count >= 12) number = numbers[0];
                else
                {
                    var go = new GameObject("Damage Number"); go.transform.SetParent(ActorRoot, false);
                    var text = go.AddComponent<TextMesh>(); text.fontSize = 64; text.characterSize = 0.06f;
                    text.transform.localRotation = Quaternion.Euler(55,0,0);
                    go.AddComponent<CameraBillboard>();
                    text.anchor = TextAnchor.MiddleCenter; text.alignment = TextAlignment.Center;
                    go.GetComponent<MeshRenderer>().sortingOrder = 900;
                    number = new DamageNumber { Text = text }; numbers.Add(number);
                }
            }
            number.Amount = merge ? number.Amount + e.Strength : e.Strength;
            number.TargetId = e.TargetId; number.Center = e.Position; number.Age = 0;
            number.Color = e.TargetId == LocalPlayerId ? new Color(1, 0.52f, 0.4f) : e.Strength > 0 ? new Color(1, 0.95f, 0.7f) : new Color(0.65f, 0.85f, 1);
            number.Text.text = number.Amount > 0 ? Mathf.CeilToInt(number.Amount).ToString() : "SHIELD";
            number.Text.gameObject.SetActive(true);
        }

        private void RenderNumbers(float dt)
        {
            foreach (var number in numbers)
            {
                number.Age += dt; number.Text.gameObject.SetActive(number.Age < 0.55f);
                if (number.Age >= 0.55f) continue;
                number.Text.transform.position = ArenaProjection.ToWorld(number.Center) + ArenaProjection.VisualOffset(0.25f, 2.05f + number.Age * 0.65f);
                var billboard=number.Text.GetComponent<CameraBillboard>();
                billboard.Anchor=ArenaProjection.ToWorld(number.Center);billboard.Offset=new Vector2(.25f,2.05f+number.Age*.65f);
                number.Text.transform.localScale = Vector3.one * (1 + Mathf.Max(0, 1 - number.Age / 0.12f) * 0.2f);
                Color color = number.Color; color.a = Mathf.Clamp01((0.55f - number.Age) / 0.2f); number.Text.color = color;
            }
        }

        private void RenderPulses(float dt)
        {
            foreach (var pulse in pulses)
            {
                pulse.Age += dt; pulse.Line.enabled = pulse.Age < pulse.Duration;
                if (!pulse.Line.enabled) continue;
                float t = pulse.Age / pulse.Duration;
                float radius = Mathf.Lerp(0.2f, pulse.Radius, t);
                Color color = pulse.Color; color.a = (1 - t) * 0.8f;
                pulse.Line.startColor = pulse.Line.endColor = color;
                for (int i = 0; i < 33; i++)
                {
                    float angle = i * Mathf.PI * 2 / 32;
                    pulse.Line.SetPosition(i, ArenaProjection.ToWorld(pulse.Center + new Vec2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius));
                }
            }
        }
    }
}
