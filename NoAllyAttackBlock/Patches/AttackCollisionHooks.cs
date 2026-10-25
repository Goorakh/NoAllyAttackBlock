using EntityStates.Engi.EngiWeapon;
using RoR2;
using System;

namespace NoAllyAttackBlock.Patches
{
    static class AttackCollisionHooks
    {
        [SystemInitializer]
        static void Init()
        {
            On.RoR2.BulletAttack.DefaultFilterCallbackImplementation += BulletAttack_DefaultFilterCallbackImplementation;
        }

        static bool BulletAttack_DefaultFilterCallbackImplementation(On.RoR2.BulletAttack.orig_DefaultFilterCallbackImplementation orig, BulletAttack bulletAttack, ref BulletAttack.BulletHit hitInfo)
        {
            bool result = orig(bulletAttack, ref hitInfo);

            try
            {
                HurtBox hitHurtBox = hitInfo.hitHurtBox;
                HealthComponent hitHealthComponent = hitHurtBox ? hitHurtBox.healthComponent : null;
                CharacterBody hitBody = hitHealthComponent ? hitHealthComponent.body : null;

                bool shouldIgnore = NoAllyAttackBlockPlugin.ShouldIgnoreAttackCollision(hitHealthComponent, bulletAttack.owner);

                if (bulletAttack.damageType.attackIdentityIndex == EngiLaserCutter.EngiHealMechanicalAttackIdentityIndex)
                {
                    switch (NoAllyAttackBlockPlugin.EngineerLaserCutterMode.Value)
                    {
                        case EngineerLaserCutterCollisionMode.OnlyCollideMechanical:
                            if (hitBody && (hitBody.bodyFlags & CharacterBody.BodyFlags.Mechanical) != 0)
                            {
                                // NOTE: This runs for *all* hits, just in this case it doesn't make a difference for non-ally hits
                                shouldIgnore = false;
                            }

                            break;
                        case EngineerLaserCutterCollisionMode.NeverCollide:
                            // Do nothing, ShouldIgnoreAttackCollision already decides if collision should be ignored
                            break;
                    }
                }

                if (shouldIgnore)
                {
                    return false;
                }
            }
            catch (Exception e)
            {
                Log.Error_NoCallerPrefix(e);
            }

            return result;
        }
    }
}
