using BepInEx;
using BepInEx.Configuration;
using NoAllyAttackBlock.Utils;
using RoR2;
using System.Diagnostics;
using UnityEngine;

namespace NoAllyAttackBlock
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInDependency(RiskOfOptions.PluginInfo.PLUGIN_GUID, BepInDependency.DependencyFlags.SoftDependency)]
    public class NoAllyAttackBlockPlugin : BaseUnityPlugin
    {
        public const string PluginGUID = PluginAuthor + "." + PluginName;
        public const string PluginAuthor = "Gorakh";
        public const string PluginName = "NoAllyAttackBlock";
        public const string PluginVersion = "1.2.2";

        public static NoAllyAttackBlockPlugin Instance { get; private set; }

        public static ConfigEntry<bool> EnablePassThroughForEnemies { get; private set; }

        public static ConfigEntry<bool> IgnoreStickProjectiles { get; private set; }

        public static ParsedBodyListConfig IgnoreAttackers { get; private set; }

        public static ParsedBodyListConfig IgnoreVictims { get; private set; }

        public static bool ShouldIgnoreAttackCollision(HealthComponent victim, GameObject attacker)
        {
            if (!victim || !attacker)
                return false;

            if (attacker.TryGetComponent(out CharacterBody attackerBody))
            {
                if (IgnoreAttackers.Contains(attackerBody.bodyIndex))
                    return false;

                if (!attackerBody.isPlayerControlled && attackerBody.teamComponent.teamIndex != TeamIndex.Player)
                {
                    if (!EnablePassThroughForEnemies.Value)
                        return false;
                }
            }

            if (victim.body)
            {
                if (IgnoreVictims.Contains(victim.body.bodyIndex))
                    return false;
            }

            TeamIndex attackerTeam = TeamComponent.GetObjectTeam(attacker);
            if (attackerTeam == TeamIndex.None || FriendlyFireManager.ShouldDirectHitProceed(victim, attackerTeam))
                return false;

            return true;
        }

        void Awake()
        {
            Stopwatch stopwatch = Stopwatch.StartNew();

            Instance = SingletonHelper.Assign(Instance, this);

            Log.Init(Logger);

            EnablePassThroughForEnemies = Config.Bind(new ConfigDefinition("General", "Enable Pass-Through For Enemies"), false, new ConfigDescription("If enabled, enemy attacks will pass through other enemies"));

            IgnoreStickProjectiles = Config.Bind(new ConfigDefinition("General", "Exclude Sticking Projectiles"), false, new ConfigDescription("If enabled, projectiles that can stick to characters (loader grapple, engi spider mines, etc.) will always have collision enabled"));

            ConfigEntry<string> ignoreAttackersConfig = Config.Bind(new ConfigDefinition("General", "Exclude Projectiles From"), "", new ConfigDescription("A comma-separated list of characters to exclude from the mod. Any attack owned by one of these characters will not ignore collisions with allies. Both internal and English display names are accepted, with whitespace and commas removed."));
            IgnoreAttackers = new ParsedBodyListConfig(ignoreAttackersConfig);

            ConfigEntry<string> ignoreVictimsConfig = Config.Bind(new ConfigDefinition("General", "Never Ignore Collisions With"), "", new ConfigDescription("A comma-separated list of characters to never ignore collisions with. Any character in this list will always have collision with incoming ally attacks. Both internal and English display names are accepted, with whitespace and commas removed."));
            IgnoreVictims = new ParsedBodyListConfig(ignoreVictimsConfig);

            if (RiskOfOptionsCompat.Active)
                RiskOfOptionsCompat.Run();

            stopwatch.Stop();
            Log.Message_NoCallerPrefix($"Initialized in {stopwatch.Elapsed.TotalSeconds:F2} seconds");
        }

        void OnDestroy()
        {
            IgnoreAttackers.Dispose();
            IgnoreVictims.Dispose();

            Instance = SingletonHelper.Unassign(Instance, this);
        }
    }
}
