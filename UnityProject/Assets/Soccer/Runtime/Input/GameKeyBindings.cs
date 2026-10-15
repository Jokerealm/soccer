using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Soccer.Runtime
{
    public enum GameAction { Kick, Push, SkillQ, SkillE, Special, Cancel, Stop, StopAlternate, Queue, Pause, Reset, OwnBall, TargetBall, Debug, Corner, FreeKick, SignalWheel, CameraSelf }
    public sealed class KeyBindingDefinition
    {
        public readonly GameAction Action; public readonly string Id, Name, Group; public readonly KeyCode Default;
        public KeyBindingDefinition(GameAction action, string id, string name, string group, KeyCode key)
        { Action = action; Id = id; Name = name; Group = group; Default = key; }
    }
    public interface IPlayerInputState
    {
        bool Down(KeyCode key); bool Held(KeyCode key); bool Up(KeyCode key);
        bool MouseDown(int button); Vector3 MousePosition { get; }
        float ScrollDelta {get;}
    }
    public sealed class UnityPlayerInputState : IPlayerInputState
    {
        public static readonly UnityPlayerInputState Instance = new UnityPlayerInputState();
        public bool Down(KeyCode key) => Input.GetKeyDown(key);
        public bool Held(KeyCode key) => Input.GetKey(key);
        public bool Up(KeyCode key) => Input.GetKeyUp(key);
        public bool MouseDown(int button) => Input.GetMouseButtonDown(button);
        public Vector3 MousePosition => Input.mousePosition;
        public float ScrollDelta => Input.mouseScrollDelta.y;
    }
    /// <summary>Local bindings translate physical keys into actions; match commands and network packets stay unchanged.</summary>
    public sealed class GameKeyBindings
    {
        public static readonly KeyBindingDefinition[] Definitions = {
            new KeyBindingDefinition(GameAction.SignalWheel,"signal-wheel","快捷交流（按住）","视角与交流",KeyCode.LeftAlt),
            new KeyBindingDefinition(GameAction.CameraSelf,"camera-self","视角回到自己","视角与交流",KeyCode.BackQuote),
            new KeyBindingDefinition(GameAction.Kick,"kick","踢球 / 蓄力","比赛",KeyCode.V),
            new KeyBindingDefinition(GameAction.Push,"push","推球 / 铲球","比赛",KeyCode.Space),
            new KeyBindingDefinition(GameAction.SkillQ,"q","角色技能 1","比赛",KeyCode.Q),
            new KeyBindingDefinition(GameAction.SkillE,"e","角色技能 2","比赛",KeyCode.E),
            new KeyBindingDefinition(GameAction.Special,"special","提前引爆","比赛",KeyCode.G),
            new KeyBindingDefinition(GameAction.Cancel,"cancel","取消动作","比赛",KeyCode.C),
            new KeyBindingDefinition(GameAction.Stop,"stop","停止移动","指令",KeyCode.S),
            new KeyBindingDefinition(GameAction.StopAlternate,"stop-alternate","停止（备用）","指令",KeyCode.H),
            new KeyBindingDefinition(GameAction.Queue,"queue","追加指令（按住）","指令",KeyCode.LeftShift),
            new KeyBindingDefinition(GameAction.Pause,"pause","暂停 / 比赛菜单","指令",KeyCode.Escape),
            new KeyBindingDefinition(GameAction.Reset,"reset","重置球场","训练",KeyCode.R),
            new KeyBindingDefinition(GameAction.OwnBall,"own-ball","自己持球","训练",KeyCode.F),
            new KeyBindingDefinition(GameAction.TargetBall,"target-ball","目标持球","训练",KeyCode.T),
            new KeyBindingDefinition(GameAction.Debug,"debug","判定显示","训练",KeyCode.F1),
            new KeyBindingDefinition(GameAction.Corner,"corner","角球练习","训练",KeyCode.F2),
            new KeyBindingDefinition(GameAction.FreeKick,"free-kick","任意球练习","训练",KeyCode.F3)
        };
        private const string Prefix = "soccer.keybinding.";
        private readonly Dictionary<GameAction,KeyCode> bindings = new Dictionary<GameAction,KeyCode>();
        private static readonly Regex HintKeys = new Regex(@"(?<![A-Za-z0-9])(?:Shift|Esc|空格|F1|F2|F3|V|Q|E|G|C|S|H|R|F|T)(?![A-Za-z0-9])");
        public event Action Changed;
        public GameKeyBindings() { ReloadSaved(); }
        public void ReloadSaved()
        {
            var seen = new HashSet<KeyCode>(); bool valid = true;
            foreach (var definition in Definitions)
            {
                var key = Normalize((KeyCode)PlayerPrefs.GetInt(Prefix + definition.Id, (int)definition.Default));
                if (!Allowed(definition.Action,key) || !seen.Add(key)) valid = false;
                bindings[definition.Action] = key;
            }
            if (!valid) foreach (var definition in Definitions) bindings[definition.Action] = definition.Default;
            Changed?.Invoke();
        }
        public KeyCode Key(GameAction action) => bindings[action];
        public string Label(GameAction action) => Name(Key(action));
        public bool Down(GameAction action, IPlayerInputState source = null) => (source ?? UnityPlayerInputState.Instance).Down(Key(action));
        public bool Up(GameAction action, IPlayerInputState source = null) => (source ?? UnityPlayerInputState.Instance).Up(Key(action));
        public bool Held(GameAction action, IPlayerInputState source = null)
        {
            var input = source ?? UnityPlayerInputState.Instance; var key = Key(action);
            return input.Held(key) || key == KeyCode.LeftShift && input.Held(KeyCode.RightShift) ||
                key == KeyCode.LeftControl && input.Held(KeyCode.RightControl) || key == KeyCode.LeftAlt && input.Held(KeyCode.RightAlt);
        }
        public bool TryAssign(GameAction action, KeyCode candidate, out string message)
        {
            candidate = Normalize(candidate);
            if (!Allowed(action,candidate)) { message = "此键保留给界面或编队操作；1–9 与 Ctrl 用于观察编队。"; return false; }
            foreach (var definition in Definitions)
                if (definition.Action != action && Key(definition.Action) == candidate)
                { message = "已用于「" + definition.Name + "」，请换一个按键。"; return false; }
            bindings[action] = candidate;
            foreach (var definition in Definitions) PlayerPrefs.SetInt(Prefix + definition.Id,(int)Key(definition.Action));
            PlayerPrefs.Save(); message = "已保存"; Changed?.Invoke(); return true;
        }
        public void RestoreDefaults()
        {
            foreach (var definition in Definitions) { bindings[definition.Action] = definition.Default; PlayerPrefs.DeleteKey(Prefix + definition.Id); }
            PlayerPrefs.Save(); Changed?.Invoke();
        }
        public string FormatHint(string text) => HintKeys.Replace(text ?? "", match =>
        {
            foreach (var definition in Definitions)
                if (Name(definition.Default) == match.Value) return Label(definition.Action);
            return match.Value;
        });
        public static KeyCode Normalize(KeyCode key) => key == KeyCode.RightShift ? KeyCode.LeftShift : key == KeyCode.RightControl ? KeyCode.LeftControl : key == KeyCode.RightAlt ? KeyCode.LeftAlt : key;
        private static bool Allowed(GameAction action, KeyCode key)
        {
            if (key == KeyCode.Escape) return action == GameAction.Pause;
            if (key >= KeyCode.Alpha1 && key <= KeyCode.Alpha9 || key >= KeyCode.Keypad1 && key <= KeyCode.Keypad9) return false;
            if (key == KeyCode.LeftShift || key == KeyCode.LeftControl || key == KeyCode.LeftAlt) return key == KeyCode.LeftAlt ? action == GameAction.SignalWheel : key == KeyCode.LeftShift && action == GameAction.Queue;
            return key >= KeyCode.A && key <= KeyCode.Z || key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9 ||
                key >= KeyCode.F1 && key <= KeyCode.F12 || key >= KeyCode.Keypad0 && key <= KeyCode.Keypad9 ||
                key == KeyCode.Space || key == KeyCode.BackQuote || key == KeyCode.Minus || key == KeyCode.Equals ||
                key == KeyCode.LeftBracket || key == KeyCode.RightBracket || key == KeyCode.Semicolon || key == KeyCode.Quote ||
                key == KeyCode.Comma || key == KeyCode.Period || key == KeyCode.Slash || key == KeyCode.Backslash ||
                key == KeyCode.UpArrow || key == KeyCode.DownArrow || key == KeyCode.LeftArrow || key == KeyCode.RightArrow ||
                key == KeyCode.Home || key == KeyCode.End || key == KeyCode.PageUp || key == KeyCode.PageDown || key == KeyCode.Insert || key == KeyCode.Delete;
        }
        public static string Name(KeyCode key)
        {
            if (key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9) return ((int)key - (int)KeyCode.Alpha0).ToString();
            switch (key)
            {
                case KeyCode.Space:return "空格"; case KeyCode.Escape:return "Esc";
                case KeyCode.LeftShift:case KeyCode.RightShift:return "Shift";
                case KeyCode.LeftControl:case KeyCode.RightControl:return "Ctrl";
                case KeyCode.LeftAlt:case KeyCode.RightAlt:return "Alt";
                case KeyCode.UpArrow:return "↑"; case KeyCode.DownArrow:return "↓"; case KeyCode.LeftArrow:return "←"; case KeyCode.RightArrow:return "→";
                case KeyCode.BackQuote:return "`"; case KeyCode.Minus:return "-"; case KeyCode.Equals:return "=";
                case KeyCode.LeftBracket:return "["; case KeyCode.RightBracket:return "]"; case KeyCode.Semicolon:return ";";
                case KeyCode.Quote:return "'"; case KeyCode.Comma:return ","; case KeyCode.Period:return ".";
                case KeyCode.Slash:return "/"; case KeyCode.Backslash:return "\\";
                default:return key.ToString();
            }
        }
    }
}
