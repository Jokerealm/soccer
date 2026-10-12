using Soccer.Core;
using UnityEngine;

namespace Soccer.Runtime
{
    /// <summary>定量短寿命火花，只响应比赛事件，不影响碰撞或模拟随机数。</summary>
    public sealed class ArenaParticles : MonoBehaviour
    {
        private ParticleSystem system;
        public int ActiveCount => system==null ? 0 : system.particleCount;
        public void Initialize()
        {
            system=gameObject.AddComponent<ParticleSystem>();
            var main=system.main; main.playOnAwake=false; main.loop=false; main.maxParticles=256;
            main.simulationSpace=ParticleSystemSimulationSpace.World; main.startLifetime=.4f; main.startSize=.1f;
            var emission=system.emission; emission.enabled=false;
            var shape=system.shape; shape.enabled=false;
            var color=system.colorOverLifetime; color.enabled=true;
            var gradient=new Gradient(); gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},new[]{new GradientAlphaKey(1,0),new GradientAlphaKey(0,1)}); color.color=gradient;
            var velocity=system.velocityOverLifetime; velocity.enabled=false;
            var renderer=GetComponent<ParticleSystemRenderer>(); renderer.sharedMaterial=Resources.Load<Material>("SceneSurface/Combat-Sparks");
            renderer.renderMode=ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows=false; renderer.sortingOrder=850;
            system.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        }
        public void Clear() {if(system!=null) system.Clear();}
        public void SetAnimating(bool animate)
        { if(system==null) return; if(animate && !system.isPlaying) system.Play(); else if(!animate && !system.isPaused) system.Pause(); }
        public void Event(MatchEvent e,MatchState state)
        {
            Vec2 ground; float height=.5f; int count; Color color; float speed=1.3f;
            switch(e.Kind)
            {
                case MatchEventKind.Damage: ground=state.Players[e.TargetId].Position; count=9; color=new Color(1,.79f,.39f); break;
                case MatchEventKind.Kick: ground=state.Ball.Position; height=.27f; count=12+Mathf.RoundToInt(e.Strength*8); speed=1.3f+e.Strength; color=new Color(1,.9f,.63f); break;
                case MatchEventKind.Skill: ground=state.Players[e.ActorId].Position; height=.7f; count=12; color=new Color(.38f,1,.83f); break;
                case MatchEventKind.Explosion: ground=e.Position; height=.25f; count=36; speed=4; color=new Color(1,.64f,.19f); break;
                case MatchEventKind.Knockout: ground=state.Players[e.TargetId].Position; count=20; color=new Color(1,.56f,.26f); break;
                case MatchEventKind.Respawn: ground=state.Players[e.ActorId].Position; count=24; color=new Color(.37f,1,.8f); break;
                case MatchEventKind.Steal: ground=state.Players[e.ActorId].Position; count=10; color=new Color(1,.84f,.46f); break;
                default: return;
            }
            if(system==null) return;
            Vector3 center=ArenaProjection.ToWorld(ground,height);
            for(int i=0;i<count;i++)
            {
                float angle=i*Mathf.PI*2/count+Random.Range(-.2f,.2f);
                var emit=new ParticleSystem.EmitParams {
                    position=center,velocity=new Vector3(Mathf.Cos(angle)*speed,Random.Range(.4f,1.4f),Mathf.Sin(angle)*speed),
                    startColor=color,startLifetime=Random.Range(.18f,.46f),startSize=Random.Range(.06f,.13f)};
                system.Emit(emit,1);
            }
        }
    }
}
