using EntityStates;
using HarmonyLib;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using MonoMod.Cil;
using RoR2;
using System;
using System.Reflection;
using UnityEngine;

namespace NoAllyAttackBlock.Patches
{
    static class CharacterRaycastPatches
    {
        static readonly LayerMask _layersToPatch = LayerIndex.entityPrecise.mask | LayerIndex.playerBody.mask | LayerIndex.enemyBody.mask;

        [SystemInitializer]
        static void Init()
        {
            MethodInfo Physics_Raycast_Ray_RaycastHit_float_int_QueryTriggerInteraction = typeof(Physics).GetMethod(nameof(Physics.Raycast), [typeof(Ray), typeof(RaycastHit).MakeByRefType(), typeof(float), typeof(int), typeof(QueryTriggerInteraction)]);
            if (Physics_Raycast_Ray_RaycastHit_float_int_QueryTriggerInteraction != null)
            {
                // UnityEngine.Physics.Raycast(Ray, out RaycastHit, float, int, QueryTriggerInteraction):
                // EntityStates.Halcyonite.WhirlwindRush.HandleIdentifySafePathForward
                // EntityStates.HermitCrab.FireMortar.Fire
                // EntityStates.MiniMushroom.SporeGrenade.FireGrenade
                // EntityStates.Scorchling.ScorchlingLavaBomb.Spit

                ILContext.Manipulator replaceRaycastManipulator = getReplaceRaycastManipulator(Physics_Raycast_Ray_RaycastHit_float_int_QueryTriggerInteraction);

                IL.EntityStates.Halcyonite.WhirlwindRush.HandleIdentifySafePathForward += replaceRaycastManipulator;
                IL.EntityStates.HermitCrab.FireMortar.Fire += replaceRaycastManipulator;
                IL.EntityStates.MiniMushroom.SporeGrenade.FireGrenade += replaceRaycastManipulator;
                IL.EntityStates.Scorchling.ScorchlingLavaBomb.Spit += replaceRaycastManipulator;
            }
            else
            {
                Log.Error("Failed to find method UnityEngine.Physics.Raycast(Ray, out RaycastHit, float, int, QueryTriggerInteraction)");
            }

            MethodInfo Physics_Raycast_Ray_RaycastHit_float_int = typeof(Physics).GetMethod(nameof(Physics.Raycast), [typeof(Ray), typeof(RaycastHit).MakeByRefType(), typeof(float), typeof(int)]);
            if (Physics_Raycast_Ray_RaycastHit_float_int != null)
            {
                // UnityEngine.Physics.Raycast(Ray, out RaycastHit, float, int):
                // EntityStates.Commando.CommandoWeapon.FireShrapnel.OnEnter
                // EntityStates.Drone.DroneWeapon.FireTwinRocket.FireProjectile
                // EntityStates.FalseSon.LaserFatherBurst.FireBurstLaser
                // EntityStates.FalseSonBoss.LunarGazeCharge.Update
                // EntityStates.GolemMonster.ChargeLaser.Update
                // EntityStates.GolemMonster.FireLaser.OnEnter
                // EntityStates.Halcyonite.ChargeTriLaser.Update
                // EntityStates.Halcyonite.TriLaser.FireTriLaser
                // EntityStates.TitanMonster.ChargeMegaLaser.Update

                ILContext.Manipulator replaceRaycastManipulator = getReplaceRaycastManipulator(Physics_Raycast_Ray_RaycastHit_float_int);

                IL.EntityStates.Commando.CommandoWeapon.FireShrapnel.OnEnter += replaceRaycastManipulator;
                IL.EntityStates.Drone.DroneWeapon.FireTwinRocket.FireProjectile += replaceRaycastManipulator;
                IL.EntityStates.FalseSon.LaserFatherBurst.FireBurstLaser += replaceRaycastManipulator;
                IL.EntityStates.FalseSonBoss.LunarGazeCharge.Update += replaceRaycastManipulator;
                IL.EntityStates.GolemMonster.ChargeLaser.Update += replaceRaycastManipulator;
                IL.EntityStates.GolemMonster.FireLaser.OnEnter += replaceRaycastManipulator;
                IL.EntityStates.Halcyonite.ChargeTriLaser.Update += replaceRaycastManipulator;
                IL.EntityStates.Halcyonite.TriLaser.FireTriLaser += replaceRaycastManipulator;
                IL.EntityStates.TitanMonster.ChargeMegaLaser.Update += replaceRaycastManipulator;
            }
            else
            {
                Log.Error("Failed to find method UnityEngine.Physics.Raycast(Ray, out RaycastHit, float, int)");
            }
        }

        static ILContext.Manipulator getReplaceRaycastManipulator(MethodInfo raycastMethod)
        {
            void manipulator(ILContext il)
            {
                ILCursor c = new ILCursor(il);

                int patchCount = 0;

                Mono.Collections.Generic.Collection<VariableDefinition> localVariables = il.Method.Body.Variables;

                VariableDefinition rayVar = null;
                VariableDefinition hitInfoVar = null;
                VariableDefinition maxDistanceVar = null;
                VariableDefinition layerMaskVar = null;
                VariableDefinition queryTriggerInteractionVar = null;

                ParameterInfo[] raycastParameters = raycastMethod.GetParameters();
                VariableDefinition[] raycastParameterLocals = new VariableDefinition[raycastParameters.Length];
                for (int i = 0; i < raycastParameters.Length; i++)
                {
                    Type parameterType = raycastParameters[i].ParameterType;
                    string parameterName = raycastParameters[i].Name;

                    VariableDefinition variableDefinition = new VariableDefinition(il.Import(parameterType));
                    localVariables.Add(variableDefinition);
                    raycastParameterLocals[i] = variableDefinition;

                    if (parameterType == typeof(Ray))
                    {
                        rayVar = variableDefinition;
                    }
                    else if (parameterType == typeof(RaycastHit).MakeByRefType())
                    {
                        hitInfoVar = variableDefinition;
                    }
                    else if (parameterType == typeof(float))
                    {
                        if (string.Equals(parameterName, "maxDistance", StringComparison.OrdinalIgnoreCase))
                        {
                            maxDistanceVar = variableDefinition;
                        }
                    }
                    else if (parameterType == typeof(int))
                    {
                        if (string.Equals(parameterName, "layerMask", StringComparison.OrdinalIgnoreCase))
                        {
                            layerMaskVar = variableDefinition;
                        }
                    }
                    else if (parameterType == typeof(QueryTriggerInteraction))
                    {
                        queryTriggerInteractionVar = variableDefinition;
                    }
                }

                if (rayVar == null)
                {
                    Log.Error("Raycast method is missing ray parameter");
                    return;
                }

                if (hitInfoVar == null)
                {
                    hitInfoVar = new VariableDefinition(il.Import(typeof(RaycastHit)).MakeByReferenceType());
                    localVariables.Add(hitInfoVar);
                }

                if (maxDistanceVar == null)
                {
                    maxDistanceVar = new VariableDefinition(il.Import(typeof(float)));
                    localVariables.Add(maxDistanceVar);

                    c.Emit(OpCodes.Ldc_R4, float.PositiveInfinity);
                    c.Emit(OpCodes.Stloc, maxDistanceVar);
                }

                if (layerMaskVar == null)
                {
                    layerMaskVar = new VariableDefinition(il.Import(typeof(int)));
                    localVariables.Add(layerMaskVar);

                    c.Emit(OpCodes.Ldc_I4, Physics.DefaultRaycastLayers);
                    c.Emit(OpCodes.Stloc, layerMaskVar);
                }

                if (queryTriggerInteractionVar == null)
                {
                    queryTriggerInteractionVar = new VariableDefinition(il.Import(typeof(QueryTriggerInteraction)));
                    localVariables.Add(queryTriggerInteractionVar);

                    c.Emit(OpCodes.Ldc_I4, (int)QueryTriggerInteraction.UseGlobal);
                    c.Emit(OpCodes.Stloc, queryTriggerInteractionVar);
                }

                VariableDefinition replacementRaycastResultVar = new VariableDefinition(il.Import(typeof(bool)));
                localVariables.Add(replacementRaycastResultVar);

                while (c.TryGotoNext(MoveType.Before, x => x.MatchCallOrCallvirt(raycastMethod)))
                {
                    c.MoveAfterLabels();

                    for (int i = raycastParameterLocals.Length - 1; i >= 0; i--)
                    {
                        c.Emit(OpCodes.Stloc, raycastParameterLocals[i]);
                    }

                    c.Emit(OpCodes.Ldarg_0);

                    c.Emit(OpCodes.Ldloc, rayVar);
                    c.Emit(OpCodes.Ldloc, hitInfoVar);
                    c.Emit(OpCodes.Ldloc, maxDistanceVar);
                    c.Emit(OpCodes.Ldloc, layerMaskVar);
                    c.Emit(OpCodes.Ldloc, queryTriggerInteractionVar);

                    c.Emit(OpCodes.Ldloca, replacementRaycastResultVar);

                    c.EmitDelegate(tryReplaceRaycast);
                    bool tryReplaceRaycast(object instance, Ray ray, ref RaycastHit hitInfo, float maxDistance, int layerMask, QueryTriggerInteraction queryTriggerInteraction, out bool raycastResult)
                    {
                        if ((layerMask & _layersToPatch) == 0)
                        {
                            Log.Warning($"Raycast on layers {layerMask} does not match required layers");

                            raycastResult = false;
                            return false;
                        }

                        GameObject bodyObject;
                        switch (instance)
                        {
                            case null:
                                raycastResult = false;
                                return false;
                            case EntityState entityState:
                                bodyObject = entityState.characterBody?.gameObject;
                                break;
                            default:
                                Log.Error($"Instance type {instance.GetType().FullName} is not implemented");

                                raycastResult = false;
                                return false;
                        }

                        raycastResult = Util.CharacterRaycast(bodyObject, ray, out hitInfo, maxDistance, layerMask, queryTriggerInteraction);
                        return true;
                    }

                    ILLabel originalRaycastCallLabel = c.DefineLabel();
                    c.Emit(OpCodes.Brfalse, originalRaycastCallLabel);

                    c.Emit(OpCodes.Ldloc, replacementRaycastResultVar);

                    ILLabel afterOriginalRaycastLabel = c.DefineLabel();
                    c.Emit(OpCodes.Br, afterOriginalRaycastLabel);

                    c.MarkLabel(originalRaycastCallLabel);
                    c.MoveAfterLabels();

                    for (int i = 0; i < raycastParameterLocals.Length; i++)
                    {
                        c.Emit(OpCodes.Ldloc, raycastParameterLocals[i]);
                    }

                    // Skip over original call
                    c.Index++;

                    c.MarkLabel(afterOriginalRaycastLabel);

                    patchCount++;
                }

                if (patchCount == 0)
                {
                    Log.Warning($"Found 0 patch locations for raycast method {raycastMethod.FullDescription()} in {il.Method.FullName}");
                }
                else
                {
#if DEBUG
                    Log.Debug($"Found {patchCount} patch location(s) for raycast method {raycastMethod.FullDescription()} in {il.Method.FullName}");
#endif
                }
            }

            return manipulator;
        }
    }
}
