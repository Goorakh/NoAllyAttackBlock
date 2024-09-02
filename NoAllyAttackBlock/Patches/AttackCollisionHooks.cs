using RoR2;
using RoR2.Projectile;
using System;

namespace NoAllyAttackBlock.Patches
{
    static class AttackCollisionHooks
    {
        [SystemInitializer]
        static void Init()
        {
            On.RoR2.BulletAttack.DefaultFilterCallbackImplementation += BulletAttack_DefaultFilterCallbackImplementation;
            On.RoR2.Projectile.ProjectileController.Awake += ProjectileController_Awake;
        }

        static bool BulletAttack_DefaultFilterCallbackImplementation(On.RoR2.BulletAttack.orig_DefaultFilterCallbackImplementation orig, BulletAttack bulletAttack, ref BulletAttack.BulletHit hitInfo)
        {
            bool result = orig(bulletAttack, ref hitInfo);

            try
            {
                if (NoAllyAttackBlockPlugin.ShouldIgnoreAttackCollision(hitInfo.hitHurtBox?.healthComponent, bulletAttack.owner))
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
