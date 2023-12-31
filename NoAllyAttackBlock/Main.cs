using BepInEx;
using BepInEx.Configuration;
using RoR2;
using RoR2.Projectile;
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

        public static ConfigEntry<bool> EnablePassThroughForEnemies;

        static bool shouldEnablePassThrough(TeamIndex teamIndex)
        {
            return EnablePassThroughForEnemies.Value || teamIndex == TeamIndex.Player;
        }

        static bool shouldIgnoreAttackCollision(HurtBox victimHurtBox, GameObject attacker)
        {
            if (attacker &&
                attacker.TryGetComponent(out TeamComponent attackerTeamComponent) &&
                shouldEnablePassThrough(attackerTeamComponent.teamIndex))
            {
                if (victimHurtBox && victimHurtBox.healthComponent)
                {
                    if (!FriendlyFireManager.ShouldDirectHitProceed(victimHurtBox.healthComponent, attackerTeamComponent.teamIndex))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        void Awake()
        {
            Stopwatch stopwatch = Stopwatch.StartNew();

            Log.Init(Logger);

            EnablePassThroughForEnemies = Config.Bind(new ConfigDefinition("General", "Enable Pass-Through For Enemies"), false, new ConfigDescription("If enabled, enemy attacks will pass through other enemies"));

            if (RiskOfOptionsCompat.Active)
                RiskOfOptionsCompat.Run();

            On.RoR2.BulletAttack.DefaultFilterCallbackImplementation += BulletAttack_DefaultFilterCallbackImplementation;
            On.RoR2.Projectile.ProjectileController.Start += ProjectileController_Start;

            stopwatch.Stop();
            Log.Info_NoCallerPrefix($"Initialized in {stopwatch.Elapsed.TotalSeconds:F2} seconds");
        }

        static bool BulletAttack_DefaultFilterCallbackImplementation(On.RoR2.BulletAttack.orig_DefaultFilterCallbackImplementation orig, BulletAttack bulletAttack, ref BulletAttack.BulletHit hitInfo)
        {
            return orig(bulletAttack, ref hitInfo) && !shouldIgnoreAttackCollision(hitInfo.hitHurtBox, bulletAttack.owner);
        }

        static void ProjectileController_Start(On.RoR2.Projectile.ProjectileController.orig_Start orig, ProjectileController self)
        {
            orig(self);

#pragma warning disable Publicizer001 // Accessing a member that was not originally public
            Collider[] projectileColliders = self.myColliders;
#pragma warning restore Publicizer001 // Accessing a member that was not originally public
            if (projectileColliders == null || projectileColliders.Length == 0)
                return;

            TeamFilter projectileTeamFilter = self.teamFilter;
            if (!projectileTeamFilter)
                return;

            TeamIndex projectileTeam = projectileTeamFilter.teamIndex;
            if (!shouldEnablePassThrough(projectileTeam))
                return;

            // Yeah this is a quadruple-nested foreach, what about it?
            foreach (CharacterBody body in CharacterBody.readOnlyInstancesList)
            {
                if (!body.healthComponent)
                    continue;

                if (!FriendlyFireManager.ShouldSplashHitProceed(body.healthComponent, projectileTeam))
                {
                    if (!body.hurtBoxGroup || body.hurtBoxGroup.hurtBoxes == null)
                        continue;

                    foreach (HurtBox hurtBox in body.hurtBoxGroup.hurtBoxes)
                    {
                        foreach (Collider hurtBoxCollider in hurtBox.GetComponents<Collider>())
                        {
                            foreach (Collider projectileCollider in projectileColliders)
                            {
                                Physics.IgnoreCollision(projectileCollider, hurtBoxCollider, true);
                            }
                        }
                    }
                }
            }
        }
    }
}
