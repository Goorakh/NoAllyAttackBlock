using RoR2;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace NoAllyAttackBlock.Patches
{
    static class CharacterRaycastCollisionHooks
    {
        static bool _isGeneratingPingInfo;

        static bool raycastHookEnabled
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => !_isGeneratingPingInfo;
        }

        [SystemInitializer]
        static void Init()
        {
            On.RoR2.PingerController.GeneratePingInfo += PingerController_GeneratePingInfo;

            On.RoR2.Util.HandleCharacterPhysicsCastResults += Util_HandleCharacterPhysicsCastResults;
        }

        static bool PingerController_GeneratePingInfo(On.RoR2.PingerController.orig_GeneratePingInfo orig, Ray aimRay, GameObject bodyObject, out PingerController.PingInfo result)
        {
            _isGeneratingPingInfo = true;

            try
            {
                return orig(aimRay, bodyObject, out result);
            }
            finally
            {
                _isGeneratingPingInfo = false;
            }
        }

        static void tryRemoveIgnoredRaycastHits(GameObject bodyObject, RaycastHit[] hits, ref int hitsLength)
        {
            if (!bodyObject || hits == null || hits.Length == 0 || !raycastHookEnabled)
                return;

            int numRemovedHits = 0;

            // Filters the hits array in O(n) time.
            // This "removes" hits that should be ignored by shifting all elements back
            // by how many removed elements have been encountered.
            // It leaves a number of duplicate and/or garbage entries at the end of the array,
            // but since the size is adjusted, this does not matter

            int currentHitIndex = 0;
            while (currentHitIndex < hitsLength - numRemovedHits)
            {
                if (numRemovedHits > 0)
                {
                    hits[currentHitIndex] = hits[currentHitIndex + numRemovedHits];
                }

                Transform currentHit = hits[currentHitIndex].transform;

                HealthComponent victim = null;
                if (currentHit.TryGetComponent(out HurtBox hurtBox))
                {
                    victim = hurtBox.healthComponent;
                }
                else if (currentHit.TryGetComponent(out HealthComponent healthComponent))
                {
                    victim = healthComponent;
                }

                bool removedHit = victim && NoAllyAttackBlockPlugin.ShouldIgnoreAttackCollision(victim, bodyObject);

                if (removedHit)
                {
                    numRemovedHits++;
                }
                else
                {
                    currentHitIndex++;
                }
            }

            hitsLength -= numRemovedHits;
        }

        static bool Util_HandleCharacterPhysicsCastResults(On.RoR2.Util.orig_HandleCharacterPhysicsCastResults orig, GameObject bodyObject, Ray ray, int numHits, RaycastHit[] hits, out RaycastHit hitInfo)
        {
            tryRemoveIgnoredRaycastHits(bodyObject, hits, ref numHits);
            return orig(bodyObject, ray, numHits, hits, out hitInfo);
        }
    }
}
