using UnityEngine;
using UnityEngine.UIElements;

namespace Soccer.Runtime
{
    /// <summary>显式传入 --soccer-input-probe 时记录原生输入与 UI 指针事件，便于区分设备、窗口和控件命中问题。</summary>
    public sealed class RuntimeInputProbe : MonoBehaviour
    {
        private GameUI ui;
        private void Start()
        {
            ui = GetComponent<AppController>().UI;
            ui.Root.RegisterCallback<PointerDownEvent>(e => Debug.Log($"SOCCER_POINTER_DOWN target={((VisualElement)e.target).name} position={e.position}"), TrickleDown.TrickleDown);
            ui.Root.RegisterCallback<PointerUpEvent>(e => Debug.Log($"SOCCER_POINTER_UP target={((VisualElement)e.target).name} position={e.position}"), TrickleDown.TrickleDown);
            Debug.Log($"SOCCER_INPUT_PROBE focused={Application.isFocused} screen={Screen.width}x{Screen.height}");
        }
        private void Update()
        {
            for (int button = 0; button < 2; button++)
            {
                if (!Input.GetMouseButtonDown(button) && !Input.GetMouseButtonUp(button)) continue;
                Vector2 point = RuntimePanelUtils.ScreenToPanel(ui.Root.panel, new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y));
                var picked = ui.Root.panel.Pick(point);
                Debug.Log($"SOCCER_NATIVE_MOUSE button={button} down={Input.GetMouseButtonDown(button)} up={Input.GetMouseButtonUp(button)} screen={Input.mousePosition} panel={point} picked={picked?.name} focused={Application.isFocused}");
            }
            if (Input.anyKeyDown) Debug.Log($"SOCCER_NATIVE_KEY input={Input.inputString} mouse={Input.mousePosition} focused={Application.isFocused}");
        }
    }
}
