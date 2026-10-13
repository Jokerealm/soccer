using Soccer.Core;
using UnityEngine;

namespace Soccer.Runtime
{
    /// <summary>Menu actors use the playable models and live arena, without creating a match simulation.</summary>
    public sealed class MenuStageView : MonoBehaviour
    {
        private AppController app;
        private Transform actors, football;
        private readonly Transform[] models = new Transform[2];
        private readonly float[] yaw = { -5, 18 };
        public bool Visible => actors != null && actors.gameObject.activeSelf;
        public int ActorCount => actors == null ? 0 : models.Length;
        public void Initialize(AppController controller)
        {
            app = controller;
            actors = new GameObject("Menu lineup").transform; actors.SetParent(transform, false);
            for (int i = 0; i < models.Length; i++)
            {
                var go = new GameObject(i == 0 ? "Marine menu model" : "Thor menu model");
                go.transform.SetParent(actors, false);
                go.AddComponent<PlayerModelView>().Initialize(app.Characters[i].CreateStats(), i == 0 ? Team.Cyan : Team.Amber);
                go.transform.localPosition = i == 0 ? new Vector3(3.7f, 0, .5f) : new Vector3(6.3f, 0, -.4f);
                go.transform.localScale = Vector3.one * (i == 0 ? 1.9f : 2.1f);
                models[i] = go.transform;
            }
            var lightObject = new GameObject("Menu portrait fill"); lightObject.transform.SetParent(actors, false);
            var fill = lightObject.AddComponent<Light>(); fill.type = LightType.Directional;
            fill.color = new Color(.78f, .88f, 1); fill.intensity = 1.05f;
            lightObject.transform.rotation = Quaternion.LookRotation(new Vector3(-3, -5, -7));
            var ball = new GameObject("Menu football"); ball.transform.SetParent(actors, false);
            ball.AddComponent<BallModelView>().Initialize(); football = ball.transform;
            ball.transform.localPosition = new Vector3(4.8f, .42f, 1.25f); ball.transform.localScale = Vector3.one * 1.55f;
        }
        public void SetVisible(bool visible)
        {
            actors.gameObject.SetActive(visible);
            app.View.ActorRoot.gameObject.SetActive(!visible);
            app.WorldCamera.GetComponent<ArenaCamera>().SetMenuMode(visible);
        }
        public void SetLineupVisible(bool visible)
        { foreach (var model in models) model.gameObject.SetActive(visible); football.gameObject.SetActive(visible); }
        private void Update()
        {
            if (!Visible) return;
            for (int i = 0; i < models.Length; i++)
            {
                float time = Time.unscaledTime * .55f + i * 1.7f;
                models[i].localRotation = Quaternion.Euler(0, yaw[i] + Mathf.Sin(time) * 1.2f, 0);
                models[i].GetChild(0).localRotation = Quaternion.Euler(Mathf.Sin(time) * .6f, 0, 0);
            }
        }
    }
}
