using System.Collections;
using Soccer.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Soccer.Runtime
{
    public sealed partial class RuntimeSmokeProbe
    {
        private static float Luminance(Color c)
        {
            float Linear(float value)=>value<=.04045f ? value/12.92f : Mathf.Pow((value+.055f)/1.055f,2.4f);
            return .2126f*Linear(c.r)+.7152f*Linear(c.g)+.0722f*Linear(c.b);
        }
        private static float Contrast(Color a,Color b)
        {float x=Luminance(a),y=Luminance(b);return (Mathf.Max(x,y)+.05f)/(Mathf.Min(x,y)+.05f);}
        private IEnumerator CaptureRefinements()
        {
            app.GoHome();app.UI.ShowCommunityLogin();yield return new WaitForSecondsRealtime(.2f);
            foreach(string name in new[]{"community-account","community-password","community-name"})
            {
                var input=app.UI.Root.Q<TextField>(name).Q(className:"unity-base-field__input");
                Check(Contrast(input.resolvedStyle.color,input.resolvedStyle.backgroundColor)>=4.5f,"readable account input foreground and background: "+name);
            }
            app.UI.Root.Q<TextField>("community-account").value="WindowsTest";app.UI.Root.Q<TextField>("community-password").value="masked-test";
            yield return Capture("refinement-account-contrast");app.UI.HideCommunityLogin();app.OpenParty();yield return new WaitForSecondsRealtime(.2f);
            foreach(string name in new[]{"party-ready","party-normal","party-ranked"})
            {
                var button=app.UI.Root.Q<Button>(name);bool enabled=button.enabledSelf;
                foreach(bool state in new[]{true,false})
                {button.SetEnabled(state);yield return null;Check(Contrast(button.resolvedStyle.color,button.resolvedStyle.backgroundColor)>=4.5f && button.resolvedStyle.opacity>.99f,"party button contrast in "+(state ? "available" : "disabled")+" state: "+name);}
                button.SetEnabled(enabled);
            }
            yield return Capture("refinement-party-contrast");app.GoHome();app.OpenSelection(MatchMode.BotMatch);app.StartMatch();app.SetPaused(true);
            foreach(int local in new[]{0,3})
            {
                app.View.LocalPlayerId=local;app.View.Bind(app.Simulation.State,app.Characters);app.View.Render(app.Simulation.State,app.Simulation.Rules,1,false,false);
                Check(app.View.PlayerViews.Count==6,"six health bars are checked for local slot "+local);
                foreach(var view in app.View.PlayerViews)
                {
                    var bars=view.ResourcesUI;var color=bars.HealthColor;
                    Check(bars.Allied ? color.g>color.r*2 : color.r>color.g*2,"health bar is green for allies and red for enemies with local slot "+local);
                }
                yield return Capture("refinement-health-local-"+local);
            }
            app.View.LocalPlayerId=0;app.GoHome();
        }
    }
}
