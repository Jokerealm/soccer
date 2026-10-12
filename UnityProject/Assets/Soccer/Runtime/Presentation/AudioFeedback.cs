using System.Collections.Generic;
using Soccer.Core;
using UnityEngine;

namespace Soccer.Runtime
{
    /// <summary>独立音乐/UI/比赛/蓄力通道；只订阅状态和事件，不参与规则裁决。</summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class AudioFeedback : MonoBehaviour
    {
        // 兼容已有场景绑定；新音效从 Resources 加载，普通构建不必重建场景。
        public AudioClip Click;
        public AudioClip Kick;
        public AudioClip Goal;
        public float MusicVolume { get; private set; }
        public float EffectsVolume { get; private set; }
        public bool Muted { get; private set; }
        public float SavedMasterVolume => Mathf.Clamp01(PlayerPrefs.GetFloat("soccer.volume", 0.6f));
        public bool MusicPlaying => music != null && (music[0].isPlaying || music[1].isPlaying);
        public bool ChargePlaying => charge != null && charge.isPlaying;
        public int ActiveVoices
        {
            get { int count = 0; foreach (var voice in voices) if (voice.isPlaying) count++; return count; }
        }
        private readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        private readonly AudioSource[] voices = new AudioSource[12];
        private readonly float[] voiceGain = new float[12];
        private readonly int[] voicePriority = new int[12];
        private readonly float[] voiceStarted = new float[12];
        private AudioSource ui, charge;
        private AudioSource[] music;
        private AppPage page;
        private bool paused, fullCharge, preview;
        private float musicBlend, duck = 1, hoverAt, rejectAt;
        private int countdown = -1;
        private MatchPhase previousPhase = MatchPhase.Finished;
        private float previousQ = -1, previousE = -1, previousTackle = -1, previousKick = -1;
        private float chargeLevel;
        private int previousAmmo = -1;

        private AudioClip Clip(string name)
        {
            if (!clips.TryGetValue(name, out var result))
            { result = Resources.Load<AudioClip>("SoccerAudio/" + name); clips[name] = result; }
            return result;
        }

        private void Awake()
        {
            ui = GetComponent<AudioSource>(); Configure(ui);
            charge = NewSource("Charge"); charge.loop = true; charge.clip = Clip("charge-loop");
            for (int i = 0; i < voices.Length; i++) voices[i] = NewSource("Match Voice " + i);
            music = new[] { NewSource("Menu Music"), NewSource("Match Music") };
            music[0].clip = Clip("menu-loop"); music[1].clip = Clip("match-loop");
            foreach (var source in music) { source.loop = true; source.priority = 200; source.volume = 0; if (source.clip != null) source.Play(); }
            Click = Clip("ui-click") ?? Click; Kick = Clip("kick") ?? Kick; Goal = Clip("goal") ?? Goal;
            MusicVolume = Mathf.Clamp01(PlayerPrefs.GetFloat("soccer.music", 0.55f));
            EffectsVolume = Mathf.Clamp01(PlayerPrefs.GetFloat("soccer.effects", 0.8f));
            Muted = PlayerPrefs.GetInt("soccer.mute", 0) != 0;
            ApplyMaster(); ApplyGains();
        }

        private static void Configure(AudioSource source)
        { source.playOnAwake = false; source.spatialBlend = 0; source.dopplerLevel = 0; source.ignoreListenerPause = true; }
        private AudioSource NewSource(string label)
        {
            var child = new GameObject(label); child.transform.SetParent(transform, false);
            var source = child.AddComponent<AudioSource>(); Configure(source); return source;
        }

        public void SetVolume(float value)
        { PlayerPrefs.SetFloat("soccer.volume", Mathf.Clamp01(value)); ApplyMaster(); PlayerPrefs.Save(); }
        public void SetMusicVolume(float value)
        { MusicVolume = Mathf.Clamp01(value); PlayerPrefs.SetFloat("soccer.music", MusicVolume); ApplyGains(); PlayerPrefs.Save(); }
        public void SetEffectsVolume(float value)
        { EffectsVolume = Mathf.Clamp01(value); PlayerPrefs.SetFloat("soccer.effects", EffectsVolume); ApplyGains(); PlayerPrefs.Save(); }
        public void SetMuted(bool value)
        { Muted = value; PlayerPrefs.SetInt("soccer.mute", value ? 1 : 0); ApplyMaster(); PlayerPrefs.Save(); }
        private void ApplyMaster() => AudioListener.volume = Muted ? 0 : SavedMasterVolume;

        public void SetContext(AppPage currentPage, bool isPaused)
        {
            page = currentPage; paused = isPaused;
            if (page != AppPage.Match || paused) StopGameplay();
        }

        public void ResetMatch()
        {
            StopGameplay(); countdown = -1; previousPhase = MatchPhase.Finished;
            previousQ = previousE = previousTackle = previousKick = -1; fullCharge = preview = false;
            previousAmmo = -1;
        }

        public void StopGameplay()
        { charge.Stop(); foreach (var source in voices) source.Stop(); chargeLevel = 0; fullCharge = preview = false; }

        private void Update()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            musicBlend = Mathf.MoveTowards(musicBlend, page == AppPage.Match ? 1 : 0, dt / 0.65f);
            duck = Mathf.MoveTowards(duck, paused || !Application.isFocused ? 0.25f : 1, dt * 3);
            ApplyGains();
        }

        private void ApplyGains()
        {
            if (ui == null) return;
            ui.volume = EffectsVolume;
            for (int i = 0; i < voices.Length; i++) voices[i].volume = voiceGain[i] * EffectsVolume;
            charge.volume = EffectsVolume * (0.035f + chargeLevel * 0.065f);
            music[0].volume = (1 - musicBlend) * MusicVolume * duck * 0.32f;
            music[1].volume = musicBlend * MusicVolume * duck * 0.30f;
        }

        public void PlayClick() { if (Click != null) ui.PlayOneShot(Click, 0.4f); }
        public void PlayHover()
        {
            if (Time.unscaledTime < hoverAt) return;
            hoverAt = Time.unscaledTime + 0.08f;
            var clip = Clip("ui-hover"); if (clip != null) ui.PlayOneShot(clip, 0.1f);
        }
        public void PlayCancel() { var clip = Clip("ui-cancel"); if (clip != null) ui.PlayOneShot(clip, 0.18f); }
        public void PlayFinish() => Play(Clip("finish"), 0.42f, 100);
        public void PlayKick() => Play(Kick, 0.55f, 80);
        public void PlayGoal() => Play(Goal, 0.65f, 100);

        private void Play(AudioClip clip, float gain, int priority, float pan = 0, float pitch = 1)
        {
            if (clip == null || EffectsVolume <= 0 || Muted) return;
            int slot = -1;
            for (int i = 0; i < voices.Length; i++) if (!voices[i].isPlaying) { slot = i; break; }
            if (slot < 0)
            {
                for (int i = 0; i < voices.Length; i++)
                    if (voicePriority[i] <= priority && (slot < 0 || voicePriority[i] < voicePriority[slot] ||
                        (voicePriority[i] == voicePriority[slot] && voiceStarted[i] < voiceStarted[slot]))) slot = i;
            }
            if (slot < 0) return;
            var source = voices[slot]; source.Stop(); source.clip = clip;
            voiceGain[slot] = gain; voicePriority[slot] = priority; voiceStarted[slot] = Time.unscaledTime;
            source.volume = gain * EffectsVolume; source.panStereo = pan; source.pitch = pitch;
            source.priority = 128 - priority; source.Play();
        }

        public int LocalPlayerId { get; set; }
        public void PlayEvent(MatchEvent e, MatchState state)
        {
            bool local = e.ActorId == LocalPlayerId || e.TargetId == LocalPlayerId;
            float gain = local ? 0.6f : 0.22f;
            int priority = local ? 80 : 20;
            float pan = e.ActorId >= 0 ? Mathf.Clamp(state.Players[e.ActorId].Position.X / 24, -0.5f, 0.5f) : 0;
            switch (e.Kind)
            {
                case MatchEventKind.Kick:
                    Play(e.Shot == ShotPreparation.Power ? Clip("kick-power") : Kick,
                        gain * Mathf.Lerp(0.7f, 1, e.Strength), priority, pan, Mathf.Lerp(1.12f, 0.92f, e.Strength)); break;
                case MatchEventKind.Catch: Play(Clip("catch"), gain * 0.5f, priority, pan); break;
                case MatchEventKind.Tackle: Play(Clip("tackle"), gain * 0.65f, priority, pan); break;
                case MatchEventKind.Steal: Play(Clip("steal"), gain * 0.8f, priority, pan); break;
                case MatchEventKind.Push: Play(Clip("impact"), gain, priority, pan); break;
                case MatchEventKind.Explosion: Play(Clip("impact"), gain * 0.9f, priority, pan, 0.7f); break;
                case MatchEventKind.Weapon:
                    string weapon = StarStrikerLoadouts.IsReference(state.Players[e.ActorId].Stats.Id) ? "combat-hit" : state.Players[e.ActorId].Stats.Id == CharacterId.WindShuttle ? "combat-duang" :
                        state.Players[e.ActorId].Stats.Id == CharacterId.IronGuard ? "combat-pan" : "combat-rice";
                    Play(Clip(weapon), gain * 0.5f, priority, pan); break;
                case MatchEventKind.Damage:
                    Play(Clip(e.Strength > 0 ? "combat-hit" : "combat-shield"), gain * 0.65f, priority, pan); break;
                case MatchEventKind.Stunned: Play(Clip("arc-q"), gain * 0.65f, priority, pan); break;
                case MatchEventKind.Knockout: Play(Clip("combat-ko"), gain * 0.75f, priority, pan); break;
                case MatchEventKind.Respawn: Play(Clip("ready"), gain * 0.5f, priority, pan); break;
                case MatchEventKind.Support: Play(Clip("catch"), gain * 0.3f, priority, pan); break;
                case MatchEventKind.Protected: if (local) Play(Clip("bounce"), 0.18f, 60); break;
                case MatchEventKind.Miss: if (e.ActorId == LocalPlayerId) Play(Clip("tackle"), 0.1f, 30, pitch: 0.8f); break;
                case MatchEventKind.Bounce:
                    Play(Clip("bounce"), 0.12f + Mathf.Clamp01(e.Strength) * 0.17f, 10,
                        Mathf.Clamp(e.Position.X / 24, -0.5f, 0.5f)); break;
                case MatchEventKind.Skill:
                    if (StarStrikerLoadouts.IsReference(state.Players[e.ActorId].Stats.Id))
                    { Play(Clip(e.Slot == SkillSlot.Q ? "wind-q" : "iron-q"), gain * 0.7f, priority, pan); break; }
                    if (state.Players[e.ActorId].Stats.Loadout != null && e.Slot == SkillSlot.Q)
                    {
                        string control = state.Players[e.ActorId].Stats.Id == CharacterId.WindShuttle ? "combat-duang" :
                            state.Players[e.ActorId].Stats.Id == CharacterId.IronGuard ? "combat-pan" : "combat-rice";
                        Play(Clip(control), gain * 0.85f, priority, pan, 0.8f); break;
                    }
                    string prefix = state.Players[e.ActorId].Stats.Id == CharacterId.WindShuttle ? "wind" :
                        state.Players[e.ActorId].Stats.Id == CharacterId.IronGuard ? "iron" : "arc";
                    Play(Clip(prefix + (e.Slot == SkillSlot.Q ? "-q" : "-e")), gain * 0.85f, priority, pan); break;
                case MatchEventKind.Goal: PlayGoal(); break;
                case MatchEventKind.ActionRejected:
                    if (e.ActorId == LocalPlayerId && Time.unscaledTime >= rejectAt)
                    { rejectAt = Time.unscaledTime + 0.18f; Play(Clip("ui-reject"), 0.16f, 70); } break;
            }
        }

        public void Observe(MatchState state, MatchRules rules)
        {
            if (paused || page != AppPage.Match) return;
            if (state.Phase == MatchPhase.Countdown)
            {
                int next = Mathf.Max(1, Mathf.CeilToInt(state.PhaseRemaining));
                if (next != countdown) { countdown = next; Play(Clip("countdown"), 0.25f, 90); }
            }
            else countdown = -1;
            if (state.Phase == MatchPhase.Playing && previousPhase == MatchPhase.Countdown)
                Play(Clip("whistle"), 0.32f, 90);
            previousPhase = state.Phase;
            var p = state.Players[LocalPlayerId];
            if (previousAmmo >= 0 && p.Ammo > previousAmmo && !p.IsDead && !p.RespawnedThisTick)
                Play(Clip("ready"), 0.06f, 30);
            previousAmmo = p.Ammo;
            if (state.Phase == MatchPhase.Playing &&
                ((previousQ > 0 && p.QCooldown <= 0) || (previousE > 0 && p.ECooldown <= 0) ||
                 (previousTackle > 0 && p.TackleCooldown <= 0) || (previousKick > 0 && p.KickCooldown <= 0))) Play(Clip("ready"), 0.1f, 40);
            previousQ = p.QCooldown; previousE = p.ECooldown; previousTackle = p.TackleCooldown; previousKick = p.KickCooldown;
            bool charging = state.Phase == MatchPhase.Playing && p.Charging;
            if (charging)
            {
                float maximum = p.Preparation == ShotPreparation.Power ? p.Stats.PowerChargeSeconds : rules.ChargeSeconds;
                chargeLevel = Mathf.Clamp01(p.ChargeTime / maximum);
                charge.pitch = Mathf.Lerp(0.75f, 1.6f, chargeLevel);
                if (!charge.isPlaying && charge.clip != null) charge.Play();
                if (chargeLevel >= 1 && !fullCharge) { fullCharge = true; Play(Clip("ready"), 0.22f, 70); }
            }
            else { charge.Stop(); chargeLevel = 0; fullCharge = false; }
            if (p.PreviewingE && !preview) Play(Clip("ui-hover"), 0.14f, 50);
            preview = p.PreviewingE;
        }
    }
}
