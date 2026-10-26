using EntityStates;
using HarmonyLib;
using HG;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using MonoMod.Utils;
using NoAllyAttackBlock.Caching;
using NoAllyAttackBlock.Utils;
using NoAllyAttackBlock.Utils.Extensions;
using RoR2;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using UnityEngine;

using Path = System.IO.Path;

namespace NoAllyAttackBlock.Patches
{
    internal static class CharacterRaycastPatches
    {
        private static readonly string _cacheDirectory = CacheHelper.GetCacheDirectory("CharacterRaycastPatches");

        private static readonly LayerMask _layersToPatch = LayerIndex.entityPrecise.mask | LayerIndex.playerBody.mask | LayerIndex.enemyBody.mask;

        [SystemInitializer]
        private static void Init()
        {
            // EntityStateCatalog does not have a SystemInitializer, so it can't be used as a dependency for our initializer
            RoR2Application.onLoad += onLoad;

            Directory.CreateDirectory(_cacheDirectory);
        }

        private static void onLoad()
        {
            Log.Debug($"Collecting state types");

            Stopwatch stopwatch = Stopwatch.StartNew();

            Dictionary<Assembly, HashSet<Type>> entityStateTypesByAssembly = [];
            int numFoundStateTypes = 0;

            // No reason to try to avoid doing this/caching it, it is incredibly cheap for what it looks like (~3ms for only RoR2.dll states)
            for (int i = 0; i < EntityStateCatalog.stateIndexToType.Length; i++)
            {
                Type stateType = EntityStateCatalog.stateIndexToType[i];
                while (stateType != null &&
                       typeof(EntityState).IsAssignableFrom(stateType) &&
                       entityStateTypesByAssembly.GetOrAddNew(stateType.Assembly).Add(stateType))
                {
                    stateType = stateType.BaseType;
                    numFoundStateTypes++;
                }
            }

            Log.Debug($"Found {numFoundStateTypes} state types across {entityStateTypesByAssembly.Count} assemblies ({stopwatch.Elapsed.TotalMilliseconds:F0}ms)");
            stopwatch.Restart();

            int numAppliedHooks = 0;

            if (entityStateTypesByAssembly.Count > 0)
            {
                foreach ((Assembly assembly, HashSet<Type> entityStateTypes) in entityStateTypesByAssembly)
                {
                    numAppliedHooks += tryPatchEntityStateTypes(assembly, entityStateTypes);
                }

                Log.Debug($"Applied {numAppliedHooks} raycast method hook(s) ({stopwatch.Elapsed.TotalMilliseconds:F0}ms)");
            }
        }

        private static int tryPatchEntityStateTypes(Assembly assembly, HashSet<Type> entityStateTypes)
        {
            AssemblyCacheHeader assemblyHeader = AssemblyCacheHeader.FromAssembly(assembly);

            string cacheFilePath = Path.Combine(_cacheDirectory, assembly.FullName);

            List<MethodInfo> methodsToPatch = [];

            // Methods which we could not extract the necessary info on, such as if the method body references a type in an assembly which is not loaded,
            // we need to hold on to these methods in order to be able to re-check it if the conditions are right in the future.
            List<MethodInfo> unverifiedMethods = [];

            bool cacheInvalidated = false;
            if (!tryReadFromAssemblyCache(assembly, cacheFilePath, methodsToPatch, unverifiedMethods))
            {
                getMethodsToPatch(assembly, entityStateTypes, methodsToPatch, unverifiedMethods);
                cacheInvalidated = true;
            }

            for (int i = unverifiedMethods.Count - 1; i >= 0; i--)
            {
                MethodInfo unverifiedMethod = unverifiedMethods[i];
                bool shouldApplyPatch = false;
                try
                {
                    _ = unverifiedMethod.DeclaringType;
                    _ = unverifiedMethod.GetMethodBody();

                    shouldApplyPatch = shouldApplyRaycastManipulatorPatch(unverifiedMethod);
                }
                catch (Exception)
                {
                    // Method still does not resolve, keep it in the list for next time
                    continue;
                }

                // This method used to throw an exception when accessed (likely due to a missing assembly reference), but now it did, check if we should be patching it.
                if (shouldApplyPatch)
                {
                    // We should patch it!
                    methodsToPatch.Add(unverifiedMethod);
                }

                // Regardless of if the patch should apply or not, the method is now properly checked, and we need to update the cache
                unverifiedMethods.RemoveAt(i);
                cacheInvalidated = true;
            }

            for (int i = methodsToPatch.Count - 1; i >= 0; i--)
            {
                MethodInfo methodToPatch = methodsToPatch[i];
                try
                {
                    _ = methodToPatch.DeclaringType;
                    _ = methodToPatch.GetMethodBody();
                }
                catch (Exception e)
                {
                    Log.Info_NoCallerPrefix($"Failed to resolve patch target {methodToPatch.Name} ({assembly.FullName}): {e}");

                    // This method no longer resolves, add it back into the unverified list and update the cache
                    unverifiedMethods.Add(methodToPatch);
                    methodsToPatch.RemoveAt(i);
                    cacheInvalidated = true;
                    continue;
                }

                ILHook hook = null;
                try
                {
                    hook = new ILHook(methodToPatch, replaceRaycastManipulator, new ILHookConfig { ManualApply = true });
                    hook.Apply();
                }
                catch (Exception e)
                {
                    Log.Warning($"Failed to apply raycast hook to {methodToPatch.DeclaringType.FullName}.{methodToPatch.Name} ({assembly.FullName}): {e.Message}");

                    hook?.Dispose();
                    hook = null;
                }
            }

            if (cacheInvalidated)
            {
                writeAssemblyToCache(assembly, cacheFilePath, methodsToPatch, unverifiedMethods);
            }

            return methodsToPatch.Count;
        }

        private static void getMethodsToPatch(Assembly assembly, HashSet<Type> entityStateTypes, List<MethodInfo> destMethodsToPatch, List<MethodInfo> destUnverifiedMethods)
        {
            foreach (Type stateType in entityStateTypes)
            {
                if (stateType.IsGenericTypeDefinition)
                {
                    continue;
                }

                foreach (MethodInfo method in stateType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    try
                    {
                        // The IsGenericMethod call sometimes causes a crash if accessed on a method where an assembly reference can't be resolved,
                        // the DeclaringType getter throws an exception instead, so do that first to catch it before trying to check IsGenericMethod
                        _ = method.DeclaringType;
                        if (method.IsGenericMethod || !method.HasMethodBody())
                        {
                            continue;
                        }

                        if (shouldApplyRaycastManipulatorPatch(method))
                        {
                            destMethodsToPatch.Add(method);
                        }
                    }
                    catch (Exception e)
                    {
                        Log.Debug_NoCallerPrefix($"Caught exception while checking method {stateType.FullName}.{method.Name} in {assembly.FullName}, adding to unverified methods list: {e}");
                        destUnverifiedMethods.Add(method);
                    }
                }
            }
        }

        private static bool tryReadFromAssemblyCache(Assembly assembly, string cacheFilePath, List<MethodInfo> destMethodsToPatch, List<MethodInfo> destUnverifiedMethods)
        {
            if (File.Exists(cacheFilePath))
            {
                try
                {
                    using (FileStream cacheFile = File.Open(cacheFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    using (BinaryReader cacheReader = new BinaryReader(cacheFile))
                    {
                        string cacheVersion = cacheReader.ReadString();
                        // If this cache was made on an older version of the mod, our code may have changed in a way where we can no longer trust the cache at all
                        if (cacheVersion == NoAllyAttackBlockPlugin.PluginVersion)
                        {
                            AssemblyCacheHeader cacheAssemblyHeader = cacheReader.ReadAssemblyCacheHeader();
                            if (cacheAssemblyHeader.ManifestVersionId == assembly.ManifestModule.ModuleVersionId)
                            {
                                Log.Info($"Using assembly cache for {assembly.FullName}");

                                int methodsToPatchCount = cacheReader.ReadInt32();
                                ListUtils.EnsureCapacity(destMethodsToPatch, methodsToPatchCount);
                                for (int i = 0; i < methodsToPatchCount; i++)
                                {
                                    MethodInfo methodToPatch = cacheReader.ReadMethodInfo(assembly);
                                    if (methodToPatch != null)
                                    {
                                        destMethodsToPatch.Add(methodToPatch);
                                    }
                                }

                                int unverifiedMethodsCount = cacheReader.ReadInt32();
                                ListUtils.EnsureCapacity(destUnverifiedMethods, unverifiedMethodsCount);
                                for (int i = 0; i < unverifiedMethodsCount; i++)
                                {
                                    MethodInfo unverifiedMethod = cacheReader.ReadMethodInfo(assembly);
                                    if (unverifiedMethod != null)
                                    {
                                        destMethodsToPatch.Add(unverifiedMethod);
                                    }
                                }

                                return true;
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    Log.Error_NoCallerPrefix($"Exception occured reading cache file for {assembly.FullName}, it will be invalidated: {e}");
                }

                // The cache file was outdated, delete it
                Log.Info($"Assembly cache for {assembly.FullName} is out of date, it will be regenerated");
                File.Delete(cacheFilePath);
            }

            return false;
        }

        private static void writeAssemblyToCache(Assembly assembly, string cacheFilePath, List<MethodInfo> methodsToPatch, List<MethodInfo> unverifiedMethods)
        {
            Log.Info($"Creating assembly cache for {assembly.FullName}");

            try
            {
                using FileStream cacheFile = File.Open(cacheFilePath, FileMode.Create, FileAccess.Write, FileShare.None);
                using BinaryWriter cacheWriter = new BinaryWriter(cacheFile);

                cacheWriter.Write(NoAllyAttackBlockPlugin.PluginVersion);

                AssemblyCacheHeader assemblyCacheHeader = AssemblyCacheHeader.FromAssembly(assembly);
                cacheWriter.WriteAssemblyCacheHeader(assemblyCacheHeader);

                cacheWriter.Write(methodsToPatch.Count);
                foreach (MethodInfo methodToPatch in methodsToPatch)
                {
                    cacheWriter.WriteMember(methodToPatch);
                }

                cacheWriter.Write(unverifiedMethods.Count);
                foreach (MethodInfo unverifiedMethod in unverifiedMethods)
                {
                    cacheWriter.WriteMember(unverifiedMethod);
                }
            }
            catch (Exception e)
            {
                Log.Error_NoCallerPrefix($"Failed to write assembly {assembly.FullName} to cache file at {cacheFilePath}: {e}");
            }
        }

        private static bool shouldApplyRaycastManipulatorPatch(MethodBase method)
        {
            using DynamicMethodDefinition dmd = new DynamicMethodDefinition(method);
            using ILContext il = new ILContext(dmd.Definition);
            ILCursor c = new ILCursor(il);

            return c.TryGotoNext(matchRaycastMethodCall);
        }

        private static bool matchRaycastMethodCall(Instruction x)
        {
            return x.MatchCallOrCallvirt(typeof(Physics), nameof(Physics.Raycast));
        }

        private static void replaceRaycastManipulator(ILContext il)
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

                c.EmitStoreStack(allParameterVariables);

                if (hitInfoVar == null)
                {
                    hitInfoVar = variablePool.GetOrCreate(typeof(RaycastHit).MakeByRefType());

                    // a ref var needs to point to some location, otherwise nullrefs when trying to assign/read
                    VariableDefinition hitInfoDummyVar = variablePool.GetOrCreate<RaycastHit>();

                    c.Emit(OpCodes.Ldloca, hitInfoDummyVar);
                    c.Emit(OpCodes.Stloc, hitInfoVar);
                }

                VariableDefinition replacementRaycastResultVar = variablePool.GetOrCreate<bool>();

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

                variablePool.ReturnAll();

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
