using Soccer.Core;
using UnityEngine.UIElements;

namespace Soccer.Runtime
{
    public sealed class NicknameUI
    {
        private readonly AppController app;private readonly VisualElement root;
        public NicknameUI(AppController app,VisualElement root)
        {
            this.app=app;this.root=root;
            var field=root.Q<TextField>("nickname-value");field.maxLength=PlayerNickname.MaxWireLength;
            field.RegisterValueChangedCallback(_=>Refresh());
            root.Q<Button>("nickname-save").clicked+=()=>{app.Audio.PlayClick();app.SaveNickname(field.value);};
            root.Q<Button>("nickname-cancel").clicked+=()=>{app.Audio.PlayClick();app.CancelNickname();};
        }
        public void Begin()
        {root.Q<TextField>("nickname-value").SetValueWithoutNotify(app.FirstNickname ? "" : app.PlayerName);Refresh();root.schedule.Execute(()=>root.Q<TextField>("nickname-value").Focus());}
        public void Refresh()
        {
            root.Q<Label>("nickname-title").text=app.FirstNickname ? "给自己起个名字" : "修改名字";
            root.Q<Label>("nickname-count").text=PlayerNickname.Count(root.Q<TextField>("nickname-value").value)+" / 6";
            root.Q<Label>("nickname-status").text=app.NicknameStatus;
            root.Q("nickname-cancel").EnableInClassList("hidden",app.FirstNickname);
            root.Q<Button>("nickname-save").text=app.FirstNickname ? "进入游戏" : "保存";
        }
    }
}
