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
        public const string PluginVersion = "1.0.1";

        public static Main Instance { get; private set; }

        public static ConfigEntry<bool> EnablePassThroughForEnemies { get; private set; }

        public static bool ShouldEnablePassThrough(CharacterBody attacker)
        {
            return EnablePassThroughForEnemies.Value || (attacker && (attacker.isPlayerControlled || attacker.teamComponent.teamIndex == TeamIndex.Player));
        }

        public static bool ShouldIgnoreAttackCollision(HealthComponent victim, GameObject attacker)
        {
            if (!victim || !attacker)
                return false;

            bool passThroughEnabled = true;
            if (attacker.TryGetComponent(out CharacterBody attackerBody))
            {
                passThroughEnabled = ShouldEnablePassThrough(attackerBody);
            }

            if (!passThroughEnabled)
                return false;

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

            if (RiskOfOptionsCompat.Active)
                RiskOfOptionsCompat.Run();

            On.RoR2.BulletAttack.DefaultFilterCallbackImplementation += BulletAttack_DefaultFilterCallbackImplementation;
            On.RoR2.Projectile.ProjectileController.Awake += ProjectileController_Awake;

            stopwatch.Stop();
            Log.Info_NoCallerPrefix($"Initialized in {stopwatch.Elapsed.TotalSeconds:F2} seconds");
        }

        void OnDestroy()
        {
            On.RoR2.BulletAttack.DefaultFilterCallbackImplementation -= BulletAttack_DefaultFilterCallbackImplementation;
            On.RoR2.Projectile.ProjectileController.Awake -= ProjectileController_Awake;

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
