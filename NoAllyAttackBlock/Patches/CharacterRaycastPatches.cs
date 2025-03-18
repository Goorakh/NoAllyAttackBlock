using EntityStates;
using HarmonyLib;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using MonoMod.Utils;
using NoAllyAttackBlock.Utils;
using NoAllyAttackBlock.Utils.Extensions;
using RoR2;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
            // EntityStateCatalog does not have a SystemInitializer, so it can't be used as a dependency for our initializer
            RoR2Application.onLoad = (Action)Delegate.Combine(RoR2Application.onLoad, onLoad);
        }

        static void onLoad()
        {
            Log.Debug($"Collecting state types");

            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();

            HashSet<Type> allEntityStateTypes = new HashSet<Type>(EntityStateCatalog.stateIndexToType.Length);

            for (int i = 0; i < EntityStateCatalog.stateIndexToType.Length; i++)
            {
                Type stateType = EntityStateCatalog.stateIndexToType[i];
                while (stateType != null && typeof(EntityState).IsAssignableFrom(stateType) && allEntityStateTypes.Add(stateType))
                {
                    stateType = stateType.BaseType;
                }
            }

            Log.Debug($"Found {allEntityStateTypes.Count} state type(s) ({stopwatch.Elapsed.TotalMilliseconds:F0}ms)");
            stopwatch.Restart();

            int numAppliedHooks = 0;

            foreach (Type stateType in allEntityStateTypes)
            {
                try
                {
                    foreach (MethodInfo method in stateType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                    {
                        ILHook hook = null;
                        try
                        {
                            // The IsGenericMethod call sometimes causes a crash if accessed on a method where an assembly reference can't be resolved,
                            // the DeclaringType getter throws an exception instead, so do that first to catch it before trying to check IsGenericMethod
                            _ = method.DeclaringType;
                            if (method.IsGenericMethod || !method.HasMethodBody())
                                continue;

                            using DynamicMethodDefinition dmd = new DynamicMethodDefinition(method);
                            using ILContext il = new ILContext(dmd.Definition);
                            ILCursor c = new ILCursor(il);

                            if (c.TryGotoNext(matchRaycastMethodCall))
                            {
                                hook = new ILHook(method, replaceRaycastManipulator, new ILHookConfig { ManualApply = true });
                                hook.Apply();
                            }
                        }
                        catch (Exception e)
                        {
                            Log.Warning($"Failed to apply raycast hook to {method.DeclaringType.FullName}.{method.Name} ({stateType.Assembly.FullName}): {e.Message}");

                            hook?.Dispose();
                            hook = null;
                        }

                        if (hook != null)
                        {
                            numAppliedHooks++;
                        }
                    }
                }
                catch (Exception e)
                {
                    Log.Warning($"Failed to scan type for raycast hooks: {stateType.FullName} ({stateType.Assembly.FullName}): {e.Message}");
                }
            }

            Log.Debug($"Applied {numAppliedHooks} raycast method hook(s) ({stopwatch.Elapsed.TotalMilliseconds:F0}ms)");
        }

        static bool matchRaycastMethodCall(Instruction x)
        {
            return x.MatchCallOrCallvirt(typeof(Physics), nameof(Physics.Raycast));
        }

        static void replaceRaycastManipulator(ILContext il)
        {
            ILCursor c = new ILCursor(il);

            ILVariablePool variablePool = new ILVariablePool(il);

            int patchCount = 0;

            while (c.TryGotoNext(MoveType.AfterLabel, matchRaycastMethodCall))
            {
                MethodReference raycastMethod = (MethodReference)c.Next.Operand;

                VariableDefinition rayVar = null;
                VariableDefinition originVar = null;
                VariableDefinition directionVar = null;
                VariableDefinition hitInfoVar = null;
                VariableDefinition maxDistanceVar = null;
                VariableDefinition layerMaskVar = null;
                VariableDefinition queryTriggerInteractionVar = null;

                List<VariableDefinition> pooledVariables = [];
                List<VariableDefinition> allParameterVariables = [];

                foreach (ParameterDefinition parameter in raycastMethod.Parameters)
                {
                    TypeReference parameterType = parameter.ParameterType;
                    string parameterName = parameter.Name;
                    VariableDefinition parameterVariable = variablePool.GetOrCreate(parameterType);

                    if (parameterType.Is(typeof(Ray)))
                    {
                        rayVar = parameterVariable;
                    }
                    else if (parameterType.Is(typeof(Vector3)))
                    {
                        if (string.Equals(parameterName, "origin", StringComparison.OrdinalIgnoreCase))
                        {
                            originVar = parameterVariable;
                        }
                        else if (string.Equals(parameterName, "direction", StringComparison.OrdinalIgnoreCase))
                        {
                            directionVar = parameterVariable;
                        }
                    }
                    else if (parameterType.Is(typeof(RaycastHit).MakeByRefType()))
                    {
                        hitInfoVar = parameterVariable;
                    }
                    else if (parameterType.Is(typeof(float)))
                    {
                        if (string.Equals(parameterName, "maxDistance", StringComparison.OrdinalIgnoreCase))
                        {
                            maxDistanceVar = parameterVariable;
                        }
                    }
                    else if (parameterType.Is(typeof(int)))
                    {
                        if (string.Equals(parameterName, "layerMask", StringComparison.OrdinalIgnoreCase))
                        {
                            layerMaskVar = parameterVariable;
                        }
                    }
                    else if (parameterType.Is(typeof(QueryTriggerInteraction)))
                    {
                        queryTriggerInteractionVar = parameterVariable;
                    }

                    allParameterVariables.Add(parameterVariable);
                }

                pooledVariables.AddRange(allParameterVariables);

                c.EmitStoreStack(allParameterVariables);

                if (hitInfoVar == null)
                {
                    hitInfoVar = variablePool.GetOrCreate(typeof(RaycastHit).MakeByRefType());
                    pooledVariables.Add(hitInfoVar);

                    // a ref var needs to point to some location, otherwise nullrefs when trying to assign/read
                    VariableDefinition hitInfoDummyVar = variablePool.GetOrCreate<RaycastHit>();
                    pooledVariables.Add(hitInfoDummyVar);

                    c.Emit(OpCodes.Ldloca, hitInfoDummyVar);
                    c.Emit(OpCodes.Stloc, hitInfoVar);
                }

                VariableDefinition replacementRaycastResultVar = variablePool.GetOrCreate<bool>();
                pooledVariables.Add(replacementRaycastResultVar);

                c.Emit(OpCodes.Ldarg_0);

                if (rayVar != null)
                {
                    c.Emit(OpCodes.Ldloc, rayVar);
                }
                else
                {
                    c.Emit(OpCodes.Ldloc, originVar);
                    c.Emit(OpCodes.Ldloc, directionVar);
                    c.EmitDelegate(getRay);

                    static Ray getRay(Vector3 origin, Vector3 direction)
                    {
                        return new Ray(origin, direction);
                    }
                }

                c.Emit(OpCodes.Ldloc, hitInfoVar);

                if (maxDistanceVar != null)
                {
                    c.Emit(OpCodes.Ldloc, maxDistanceVar);
                }
                else
                {
                    c.Emit(OpCodes.Ldc_R4, float.PositiveInfinity);
                }

                if (layerMaskVar != null)
                {
                    c.Emit(OpCodes.Ldloc, layerMaskVar);
                }
                else
                {
                    c.Emit(OpCodes.Ldc_I4, Physics.DefaultRaycastLayers);
                }

                if (queryTriggerInteractionVar != null)
                {
                    c.Emit(OpCodes.Ldloc, queryTriggerInteractionVar);
                }
                else
                {
                    c.Emit(OpCodes.Ldc_I4, (int)QueryTriggerInteraction.UseGlobal);
                }

                c.Emit(OpCodes.Ldloca, replacementRaycastResultVar);

                c.EmitDelegate(tryReplaceRaycast);
                static bool tryReplaceRaycast(object instance, Ray ray, out RaycastHit hitInfo, float maxDistance, int layerMask, QueryTriggerInteraction queryTriggerInteraction, out bool raycastResult)
                {
                    if ((layerMask & _layersToPatch) == 0)
                    {
                        hitInfo = default;
                        raycastResult = false;
                        return false;
                    }

                    GameObject bodyObject;
                    switch (instance)
                    {
                        case null:
                            hitInfo = default;
                            raycastResult = false;
                            return false;
                        case EntityState entityState:
                            CharacterBody stateBody = entityState.characterBody;
                            bodyObject = stateBody ? stateBody.gameObject : null;
                            break;
                        default:
                            Log.Error($"Instance type {instance.GetType().FullName} is not implemented");

                            hitInfo = default;
                            raycastResult = false;
                            return false;
                    }

                    raycastResult = Util.CharacterRaycast(bodyObject, ray, out hitInfo, maxDistance, layerMask, queryTriggerInteraction);
                    return true;
                }

                c.EmitSkipMethodCall(OpCodes.Brtrue, c =>
                {
                    c.Emit(OpCodes.Ldloc, replacementRaycastResultVar);
                });

                foreach (VariableDefinition pooledVariable in pooledVariables)
                {
                    variablePool.Return(pooledVariable);
                }

                patchCount++;
                c.SearchTarget = SearchTarget.Next;
            }

            if (patchCount == 0)
            {
                Log.Warning($"Found 0 patch locations in {il.Method.FullName}");
            }
            else
            {
                Log.Debug($"Found {patchCount} patch location(s) in {il.Method.FullName}");
            }
        }
    }
}
