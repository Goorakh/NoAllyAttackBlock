using BepInEx.Bootstrap;
using RiskOfOptions;
using RiskOfOptions.Options;
using System;
using System.IO;
using UnityEngine;

namespace NoAllyAttackBlock
{
    static class RiskOfOptionsCompat
    {
        public static bool Active => Chainloader.PluginInfos.ContainsKey("com.rune580.riskofoptions");

        public static void Run()
        {
            const string GUID = Main.PluginGUID;
            const string NAME = "Disable Ally Attack Collision";

            ModSettingsManager.AddOption(new CheckBoxOption(Main.EnablePassThroughForEnemies), GUID, NAME);
            
            FileInfo iconFile = null;

            DirectoryInfo dir = new DirectoryInfo(Path.GetDirectoryName(Main.Instance.Info.Location));
            do
            {
                FileInfo[] files = dir.GetFiles("icon.png", SearchOption.TopDirectoryOnly);
                if (files != null && files.Length > 0)
                {
                    iconFile = files[0];
                    break;
                }

                dir = dir.Parent;
            } while (dir != null && !string.Equals(dir.Name, "plugins", StringComparison.OrdinalIgnoreCase));

            if (iconFile != null)
            {
                Texture2D iconTexture = new Texture2D(256, 256);
                if (iconTexture.LoadImage(File.ReadAllBytes(iconFile.FullName)))
                {
                    Sprite iconSprite = Sprite.Create(iconTexture, new Rect(0f, 0f, iconTexture.width, iconTexture.height), new Vector2(0.5f, 0.5f));
                    iconSprite.name = $"{Main.PluginName}Icon";

                    ModSettingsManager.SetModIcon(iconSprite, GUID, NAME);
                }
            }
        }
    }
}
