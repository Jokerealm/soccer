using Soccer.Core;
using UnityEngine.UIElements;

namespace Soccer.Runtime
{
    public sealed class ShopUI
    {
        private readonly AppController app;private readonly VisualElement root;private bool skins;
        public ShopUI(AppController controller,VisualElement ui)
        {
            app=controller;root=ui;
            root.Q<Button>("shop-heroes-tab").clicked+=()=>{skins=false;app.Audio.PlayClick();Refresh();};
            root.Q<Button>("shop-skins-tab").clicked+=()=>{skins=true;app.Audio.PlayClick();Refresh();};
        }
        private void Text(string name,string value)=>root.Q<Label>(name).text=value;
        public void Refresh()
        {
            string coins=app.Wallet==null ? "—" : app.Wallet.Coins.ToString("N0");
            Text("home-coins","金币  "+coins);Text("shop-coins",coins);Text("shop-profile",app.WalletProfile);
            Text("shop-status",app.WalletStatus.Length>0 ? app.WalletStatus : "余额仅在当前设备保存");
            root.Q("shop-heroes").EnableInClassList("hidden",skins);root.Q("shop-skins").EnableInClassList("hidden",!skins);
            root.Q("shop-heroes-tab").EnableInClassList("shop-tab-selected",!skins);root.Q("shop-skins-tab").EnableInClassList("shop-tab-selected",skins);
        }
    }
}
