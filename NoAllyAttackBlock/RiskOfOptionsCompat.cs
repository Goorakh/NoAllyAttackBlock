using BepInEx.Bootstrap;
using RiskOfOptions;
using RiskOfOptions.Options;
using UnityEngine;

namespace NoAllyAttackBlock
{
    static class RiskOfOptionsCompat
    {
        public static bool Active => Chainloader.PluginInfos.ContainsKey("com.rune580.riskofoptions");

        static Sprite _icon;

        public static void Run()
        {
            const string GUID = Main.PluginGUID;
            const string NAME = "Disable Ally Attack Collision";

            if (!_icon)
            {
                Texture2D iconTexture = new Texture2D(256, 256);
                if (iconTexture.LoadImage(Properties.Resources.icon))
                {
                    _icon = Sprite.Create(iconTexture, new Rect(0f, 0f, iconTexture.width, iconTexture.height), Vector2.one / 2f);
                }
            }

            if (_icon)
            {
                ModSettingsManager.SetModIcon(_icon, GUID, NAME);
            }

            ModSettingsManager.AddOption(new CheckBoxOption(Main.EnablePassThroughForEnemies), GUID, NAME);
        }
    }
}
