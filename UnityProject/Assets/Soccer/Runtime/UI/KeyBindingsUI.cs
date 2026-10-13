using UnityEngine;
using UnityEngine.UIElements;

namespace Soccer.Runtime
{
    public sealed class KeyBindingsUI
    {
        private readonly VisualElement root;
        private readonly GameKeyBindings bindings;
        private KeyBindingDefinition editing;
        private int beganFrame, navigationGuardFrame = -1;
        public bool Capturing => editing != null;
        public bool BlocksNavigation => Capturing || Time.frameCount <= navigationGuardFrame;
        public KeyBindingsUI(VisualElement ui, GameKeyBindings keys)
        {
            root = ui; bindings = keys;
            string group = ""; var list = root.Q("keybinding-list");
            foreach (var definition in GameKeyBindings.Definitions)
            {
                if (group != definition.Group) { group = definition.Group; var heading = new Label(group); heading.AddToClassList("binding-group"); list.Add(heading); }
                var row = new VisualElement(); row.AddToClassList("binding-row");
                var label = new Label(definition.Name); label.AddToClassList("grow"); row.Add(label);
                var button = new Button(() => Begin(definition)) {name = "binding-" + definition.Id}; button.AddToClassList("binding-key"); row.Add(button); list.Add(row);
            }
            root.RegisterCallback<KeyDownEvent>(Capture,TrickleDown.TrickleDown);
            root.Q<Button>("keys-reset").clicked += () => { Cancel(); bindings.RestoreDefaults(); Status("已恢复默认按键"); };
            root.Q<Button>("settings-general-tab").clicked += () => Show(false);
            root.Q<Button>("settings-keys-tab").clicked += () => Show(true);
            bindings.Changed += Refresh; Refresh(); Show(false);
        }
        public void Show(bool keys)
        {
            Cancel();
            root.Q("settings-general").EnableInClassList("hidden",keys);
            root.Q("settings-keys").EnableInClassList("hidden",!keys);
            root.Q("settings-general-tab").EnableInClassList("settings-tab-selected",!keys);
            root.Q("settings-keys-tab").EnableInClassList("settings-tab-selected",keys);
        }
        private void Begin(KeyBindingDefinition definition)
        {
            Cancel(); editing = definition; beganFrame = Time.frameCount;
            root.Q<Button>("binding-" + definition.Id).text = "按下新键…";
            root.Q("binding-" + definition.Id).AddToClassList("binding-capturing");
            Status("修改「" + definition.Name + "」 · Esc 取消"); root.Focus();
        }
        private void Capture(KeyDownEvent evt)
        {
            if (!Capturing) return;
            evt.StopImmediatePropagation(); evt.PreventDefault(); navigationGuardFrame = Time.frameCount + 1;
            if (Time.frameCount <= beganFrame) return;
            if (evt.keyCode == KeyCode.Escape) { Cancel(); Status("已取消修改"); return; }
            if (!bindings.TryAssign(editing.Action,evt.keyCode,out string message)) { Status(message + " Esc 取消"); return; }
            Cancel(); Status(message);
        }
        public void Cancel() { editing = null; Refresh(); }
        private void Status(string text) => root.Q<Label>("keys-status").text = text;
        private void Refresh()
        {
            foreach (var definition in GameKeyBindings.Definitions)
            { var button = root.Q<Button>("binding-" + definition.Id); button.text = bindings.Label(definition.Action); button.RemoveFromClassList("binding-capturing"); }
        }
    }
}
