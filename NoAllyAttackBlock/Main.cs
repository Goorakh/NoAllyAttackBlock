using BepInEx;
using BepInEx.Configuration;
using RoR2;
using RoR2.Projectile;
using System;
using System.Diagnostics;
using UnityEngine;

namespace NoAllyAttackBlock
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInDependency("com.rune580.riskofoptions", BepInDependency.DependencyFlags.SoftDependency)]
    public class Main : BaseUnityPlugin
    {
        public const string PluginGUID = PluginAuthor + "." + PluginName;
        public const string PluginAuthor = "Gorakh";
        public const string PluginName = "NoAllyAttackBlock";
        public const string PluginVersion = "1.1.0";

        public static Main Instance { get; private set; }

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

            On.RoR2.BulletAttack.DefaultFilterCallbackImplementation += BulletAttack_DefaultFilterCallbackImplementation;
            On.RoR2.Projectile.ProjectileController.Awake += ProjectileController_Awake;

            stopwatch.Stop();
            Log.Message_NoCallerPrefix($"Initialized in {stopwatch.Elapsed.TotalSeconds:F2} seconds");
        }

        void OnDestroy()
        {
            On.RoR2.BulletAttack.DefaultFilterCallbackImplementation -= BulletAttack_DefaultFilterCallbackImplementation;
            On.RoR2.Projectile.ProjectileController.Awake -= ProjectileController_Awake;

            IgnoreAttackers.Dispose();
            IgnoreVictims.Dispose();

            Instance = SingletonHelper.Unassign(Instance, this);
        }

        static bool BulletAttack_DefaultFilterCallbackImplementation(On.RoR2.BulletAttack.orig_DefaultFilterCallbackImplementation orig, BulletAttack bulletAttack, ref BulletAttack.BulletHit hitInfo)
        {
            bool result = orig(bulletAttack, ref hitInfo);

            try
            {
                if (ShouldIgnoreAttackCollision(hitInfo.hitHurtBox?.healthComponent, bulletAttack.owner))
                    return false;
            }
            catch (Exception e)
            {
                Log.Error_NoCallerPrefix(e);
            }

            return result;
        }

        static void ProjectileController_Awake(On.RoR2.Projectile.ProjectileController.orig_Awake orig, ProjectileController self)
        {
            orig(self);

            self.gameObject.AddComponent<ProjectileIgnoreCollisions>();
        }
    }
}
