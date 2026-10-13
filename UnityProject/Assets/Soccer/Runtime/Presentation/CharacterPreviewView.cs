using Soccer.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Soccer.Runtime
{
    /// <summary>An isolated lit model rendered into the selection panel, with pointer-controlled orbit and zoom.</summary>
    public sealed class CharacterPreviewView : MonoBehaviour
    {
        private const int PreviewLayer = 29;
        private AppController app;
        private VisualElement image;
        private Transform stage, model;
        private Camera previewCamera;
        private RenderTexture texture;
        private Material platformMaterial;
        private int pointer = -1;
        private Vector2 lastPointer;
        private CharacterId? character;
        private float yaw = 12, pitch = 14, distance = 4.4f;
        public bool AutoRotate { get; private set; } = true;
        public bool Visible => stage != null && stage.gameObject.activeSelf;
        public CharacterId? Character => character;
        public float OrbitYaw => yaw;
        public float OrbitPitch => pitch;
        public float Distance => distance;
        public RenderTexture Texture => texture;
        public void Initialize(AppController controller, VisualElement view)
        {
            app = controller; image = view;
            stage = new GameObject("Selection model studio").transform; stage.SetParent(transform,false); stage.position = new Vector3(0,-100,0);
            var cameraObject = new GameObject("Selection model camera"); cameraObject.transform.SetParent(stage,false);
            previewCamera = cameraObject.AddComponent<Camera>(); previewCamera.fieldOfView = 32; previewCamera.nearClipPlane = .1f; previewCamera.farClipPlane = 15;
            previewCamera.clearFlags = CameraClearFlags.SolidColor; previewCamera.backgroundColor = new Color(.035f,.066f,.09f); previewCamera.cullingMask = 1 << PreviewLayer;
            var platform = GameObject.CreatePrimitive(PrimitiveType.Cylinder); platform.name = "Model turntable"; platform.transform.SetParent(stage,false);
            platform.transform.localPosition = new Vector3(0,-.055f,0); platform.transform.localScale = new Vector3(2.25f,.055f,2.25f);
            Destroy(platform.GetComponent<Collider>()); platform.layer = PreviewLayer;
            platformMaterial = new Material(Shader.Find("Standard")) { color = new Color(.075f,.12f,.16f) };
            platformMaterial.SetFloat("_Metallic",.5f); platformMaterial.SetFloat("_Glossiness",.4f); platform.GetComponent<MeshRenderer>().sharedMaterial = platformMaterial;
            Light("Studio key",new Vector3(-3,-4,-5),new Color(.78f,.89f,1),.85f);
            Light("Studio rim",new Vector3(4,-2,4),new Color(.34f,.8f,.85f),.65f);
            image.RegisterCallback<GeometryChangedEvent>(_ => image.schedule.Execute(Resize));
            image.RegisterCallback<PointerDownEvent>(Down); image.RegisterCallback<PointerMoveEvent>(Move); image.RegisterCallback<PointerUpEvent>(Up);
            image.RegisterCallback<PointerCaptureOutEvent>(_ => pointer = -1);
            image.RegisterCallback<WheelEvent>(evt => { Zoom(evt.delta.y); evt.StopPropagation(); evt.PreventDefault(); });
            stage.gameObject.SetActive(false);
        }
        private void Light(string name, Vector3 direction, Color color, float intensity)
        {
            var go = new GameObject(name); go.transform.SetParent(stage,false); go.transform.rotation = Quaternion.LookRotation(direction);
            var light = go.AddComponent<Light>(); light.type = LightType.Directional; light.color = color; light.intensity = intensity; light.cullingMask = 1 << PreviewLayer;
            if (name == "Studio key") { light.shadows = LightShadows.Soft; light.shadowStrength = .7f; light.shadowBias = .025f; }
        }
        public void Select(CharacterDefinition definition)
        {
            if (character == definition.Stats.Id) return;
            if (model != null) { model.gameObject.SetActive(false); Destroy(model.gameObject); }
            var go = new GameObject("Selection " + definition.Stats.Name); go.transform.SetParent(stage,false);
            go.AddComponent<PlayerModelView>().Initialize(definition.CreateStats(),Team.Cyan);
            foreach (var child in go.GetComponentsInChildren<Transform>()) child.gameObject.layer = PreviewLayer;
            model = go.transform; character = definition.Stats.Id; ResetView();
        }
        public void SetVisible(bool visible)
        {
            if (!visible && pointer >= 0) { image.ReleasePointer(pointer); pointer = -1; }
            stage.gameObject.SetActive(visible);
            if (visible)
            {
                // The studio receives its own lights; field lights stay on the gameplay layers.
                foreach (var light in FindObjectsOfType<Light>())
                    if (!light.transform.IsChildOf(stage)) light.cullingMask &= ~(1 << PreviewLayer);
                Resize(); Frame();
            }
        }
        public void ToggleAutoRotate() { AutoRotate = !AutoRotate; UpdateCaption(); }
        public void ResetView() { yaw = 12; pitch = 14; distance = 4.4f; Frame(); }
        public void Orbit(Vector2 delta)
        { AutoRotate = false; yaw -= delta.x * .45f; pitch = Mathf.Clamp(pitch + delta.y * .3f,5,55); UpdateCaption(); Frame(); }
        public void Zoom(float delta) { distance = Mathf.Clamp(distance + delta * .08f,2.6f,6.8f); Frame(); }
        private void Down(PointerDownEvent evt)
        {
            if (evt.button != 0 || !Visible) return;
            pointer = evt.pointerId; lastPointer = evt.position; image.CapturePointer(pointer); evt.StopPropagation();
        }
        private void Move(PointerMoveEvent evt)
        {
            if (pointer != evt.pointerId || !image.HasPointerCapture(pointer)) return;
            Vector2 point = evt.position; Orbit(point-lastPointer); lastPointer = point; evt.StopPropagation();
        }
        private void Up(PointerUpEvent evt)
        { if (pointer == evt.pointerId) { image.ReleasePointer(pointer); pointer = -1; evt.StopPropagation(); } }
        private void UpdateCaption() => app.UI.Root.Q<Button>("preview-auto").text = AutoRotate ? "自动旋转：开" : "自动旋转：关";
        private void Resize()
        {
            if (!Visible || image.layout.width <= 1 || image.layout.height <= 1) return;
            float scale = Screen.width / Mathf.Max(1,app.UI.Root.worldBound.width);
            int width = Mathf.Clamp(Mathf.RoundToInt(image.layout.width*scale),128,2048), height = Mathf.Clamp(Mathf.RoundToInt(image.layout.height*scale),128,2048);
            if (texture != null && texture.width == width && texture.height == height) return;
            previewCamera.targetTexture = null;
            if (texture != null) { texture.Release(); Destroy(texture); }
            texture = new RenderTexture(width,height,24) {name="Selection model render",antiAliasing=4}; texture.Create();
            previewCamera.targetTexture = texture; image.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(texture)); Frame();
        }
        private void Frame()
        {
            if (previewCamera == null) return;
            var target = stage.position + Vector3.up * .91f;
            float angle = yaw * Mathf.Deg2Rad, elevation = pitch * Mathf.Deg2Rad;
            previewCamera.transform.position = target + new Vector3(Mathf.Sin(angle)*Mathf.Cos(elevation),Mathf.Sin(elevation),Mathf.Cos(angle)*Mathf.Cos(elevation))*distance;
            previewCamera.transform.LookAt(target);
        }
        private void Update() { if (Visible && AutoRotate && pointer < 0) { yaw += Time.unscaledDeltaTime * 13; Frame(); } }
        private void OnDestroy()
        { if (texture != null) { texture.Release(); Destroy(texture); } if (platformMaterial != null) Destroy(platformMaterial); }
    }
}
