using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;

namespace DontPullMeMorePlayers;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class Plugin : BasePlugin
{
    public const string PluginGuid = "andrewpalww.dontpullme.moreplayers";
    public const string PluginName = "Dont Pull Me More Players";
    public const string PluginVersion = "1.0.5";
    public const int MaxPlayers = 8;

    internal static ManualLogSource? ModLog;
    private Harmony? _harmony;

    public override void Load()
    {
        ModLog = Log;
        Log.LogInfo($"{PluginName} v{PluginVersion} loading. Target player limit: {MaxPlayers}.");

        _harmony = new Harmony(PluginGuid);
        int patched = 0;

        patched += PatchLobbyManager(_harmony);
        patched += PatchLobbyData(_harmony);
        patched += PatchSteamMatchmaking(_harmony);
        patched += PatchFishySteamworks(_harmony);
        patched += PatchRopeSystem(_harmony);

        Log.LogInfo($"{PluginName}: installed {patched} Harmony patches.");
        Log.LogInfo("v1.0.5 adds dynamic RopeStack expansion for 5-8 player sessions.");
    }

    private static int PatchLobbyManager(Harmony harmony)
    {
        int count = 0;
        foreach (Type type in FindTypes(t => t.FullName == "Heathen.SteamworksIntegration.LobbyManager"))
        {
            foreach (MethodInfo method in DeclaredMethods(type))
            {
                if (method.Name == "Create")
                {
                    int idx = FindIntParameter(method);
                    if (idx >= 0)
                        count += PatchIntArgument(harmony, method, idx);
                }
                else if (method.Name == "set_MaxMembers" && HasSingleIntParameter(method))
                {
                    count += PatchIntArgument(harmony, method, 0);
                }
                else if (method.Name == "get_MaxMembers" && method.ReturnType == typeof(int))
                {
                    count += Patch(harmony, method, postfixName: nameof(Patches.MinimumEightResult));
                }
                else if (method.Name == "get_Full" && method.ReturnType == typeof(bool))
                {
                    count += Patch(harmony, method, postfixName: nameof(Patches.ForceNotFull));
                }
            }
        }
        return count;
    }

    private static int PatchLobbyData(Harmony harmony)
    {
        int count = 0;
        foreach (Type type in FindTypes(t => t.FullName == "Heathen.SteamworksIntegration.LobbyData"))
        {
            foreach (MethodInfo method in DeclaredMethods(type))
            {
                if (method.Name == "CreatePrivateSession")
                {
                    int idx = FindIntParameter(method);
                    if (idx >= 0)
                        count += PatchIntArgument(harmony, method, idx);
                }
                else if (method.Name == "get_MaxMembers" && method.ReturnType == typeof(int))
                {
                    count += Patch(harmony, method, postfixName: nameof(Patches.MinimumEightResult));
                }
                else if (method.Name == "get_Full" && method.ReturnType == typeof(bool))
                {
                    count += Patch(harmony, method, postfixName: nameof(Patches.ForceNotFull));
                }
            }
        }
        return count;
    }

    private static int PatchSteamMatchmaking(Harmony harmony)
    {
        int count = 0;
        foreach (Type type in FindTypes(t =>
                     (t.FullName?.Contains("Steamworks", StringComparison.OrdinalIgnoreCase) ?? false) &&
                     (t.FullName?.Contains("Matchmaking", StringComparison.OrdinalIgnoreCase) ?? false)))
        {
            foreach (MethodInfo method in DeclaredMethods(type))
            {
                if (method.Name == "CreateLobby" || method.Name == "SetLobbyMemberLimit")
                {
                    int idx = FindIntParameter(method);
                    if (idx >= 0)
                        count += PatchIntArgument(harmony, method, idx);
                }
                else if (method.Name == "GetLobbyMemberLimit" && method.ReturnType == typeof(int))
                {
                    count += Patch(harmony, method, postfixName: nameof(Patches.MinimumEightResult));
                }
            }
        }
        return count;
    }

    private static int PatchFishySteamworks(Harmony harmony)
    {
        int count = 0;
        foreach (Type type in FindTypes(t =>
                     t.FullName?.StartsWith("FishySteamworks", StringComparison.OrdinalIgnoreCase) == true &&
                     !(t.IsInterface)))
        {
            foreach (MethodInfo method in DeclaredMethods(type))
            {
                if (method.Name == "SetMaximumClients" && HasSingleIntParameter(method))
                {
                    count += PatchIntArgument(harmony, method, 0);
                }
                else if (method.Name == "StartConnection")
                {
                    int idx = FindIntParameter(method);
                    if (idx >= 0)
                        count += PatchIntArgument(harmony, method, idx);
                }
                else if (method.Name == "GetMaximumClients" && method.ReturnType == typeof(int))
                {
                    count += Patch(harmony, method, postfixName: nameof(Patches.MinimumEightResult));
                }
            }
        }
        return count;
    }


    private static int PatchRopeSystem(Harmony harmony)
    {
        int count = 0;
        foreach (Type type in FindTypes(t =>
                     t.FullName == "GameAssets.Scripts.Managers.RopeManagerFishnet"))
        {
            foreach (MethodInfo method in DeclaredMethods(type))
            {
                if (method.Name == "HandleServerCharacterSpawned")
                    count += Patch(harmony, method, postfixName: nameof(Patches.RopeManagerAfterPlayerSpawn));
                else if (method.Name == "RebuildRopeChain")
                    count += Patch(harmony, method, prefixName: nameof(Patches.RopeManagerBeforeRebuild),
                                                   postfixName: nameof(Patches.RopeManagerAfterRebuild));
                else if (method.Name == "SceneManager_OnLoadEnd")
                    count += Patch(harmony, method, postfixName: nameof(Patches.RopeManagerAfterSceneLoad));
                else if (method.Name == "DelayedRopeSetup")
                    count += Patch(harmony, method, prefixName: nameof(Patches.RopeManagerBeforeDelayedSetup));
            }
        }
        return count;
    }

    private static int PatchIntArgument(Harmony harmony, MethodInfo target, int intIndex)
    {
        string? prefix = intIndex switch
        {
            0 => nameof(Patches.ForceInt0),
            1 => nameof(Patches.ForceInt1),
            2 => nameof(Patches.ForceInt2),
            3 => nameof(Patches.ForceInt3),
            _ => null
        };

        if (prefix is null)
        {
            ModLog?.LogWarning($"Cannot patch int argument #{intIndex} for {FormatMethod(target)}.");
            return 0;
        }

        return Patch(harmony, target, prefixName: prefix);
    }

    private static int Patch(Harmony harmony, MethodInfo target, string? prefixName = null, string? postfixName = null)
    {
        try
        {
            HarmonyMethod? prefix = prefixName is null ? null : new HarmonyMethod(AccessTools.Method(typeof(Patches), prefixName));
            HarmonyMethod? postfix = postfixName is null ? null : new HarmonyMethod(AccessTools.Method(typeof(Patches), postfixName));
            harmony.Patch(target, prefix: prefix, postfix: postfix);
            ModLog?.LogInfo($"Patched {FormatMethod(target)}");
            return 1;
        }
        catch (Exception ex)
        {
            ModLog?.LogWarning($"Failed to patch {FormatMethod(target)}: {ex.GetType().Name}: {ex.Message}");
            return 0;
        }
    }

    private static string FormatMethod(MethodInfo target) =>
        $"{target.DeclaringType?.FullName}.{target.Name}({string.Join(", ", target.GetParameters().Select(p => p.ParameterType.Name))})";

    private static int FindIntParameter(MethodInfo method)
    {
        ParameterInfo[] p = method.GetParameters();
        for (int i = 0; i < p.Length; i++)
            if (p[i].ParameterType == typeof(int)) return i;
        return -1;
    }

    private static bool HasSingleIntParameter(MethodInfo method)
    {
        ParameterInfo[] p = method.GetParameters();
        return p.Length == 1 && p[0].ParameterType == typeof(int);
    }

    private static IEnumerable<Type> FindTypes(Func<Type, bool> predicate)
    {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            foreach (Type type in SafeTypes(assembly))
            {
                bool match;
                try { match = predicate(type); }
                catch { continue; }
                if (match) yield return type;
            }
        }
    }

    private static IEnumerable<Type> SafeTypes(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.OfType<Type>(); }
        catch { return Array.Empty<Type>(); }
    }

    private static IEnumerable<MethodInfo> DeclaredMethods(Type type)
    {
        try
        {
            return type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
                                   BindingFlags.Instance | BindingFlags.Static |
                                   BindingFlags.DeclaredOnly);
        }
        catch { return Array.Empty<MethodInfo>(); }
    }
}

internal static class Patches
{
    private static void Raise(ref int value, string argument)
    {
        if (value < Plugin.MaxPlayers)
        {
            int old = value;
            value = Plugin.MaxPlayers;
            Plugin.ModLog?.LogInfo($"FORCED {argument}: {old} -> {value}");
        }
    }

    // Positional Harmony arguments are used deliberately instead of object[] __args.
    // This is substantially more reliable with BepInEx 6 + Il2CppInterop/HarmonySupport.
    public static void ForceInt0(ref int __0) => Raise(ref __0, "int arg #0");
    public static void ForceInt1(ref int __1) => Raise(ref __1, "int arg #1");
    public static void ForceInt2(ref int __2) => Raise(ref __2, "int arg #2");
    public static void ForceInt3(ref int __3) => Raise(ref __3, "int arg #3");

    public static void MinimumEightResult(ref int __result)
    {
        if (__result < Plugin.MaxPlayers)
        {
            int old = __result;
            __result = Plugin.MaxPlayers;
            Plugin.ModLog?.LogInfo($"FORCED integer result: {old} -> {__result}");
        }
    }

    // Steam/FishySteamworks still provides the hard ceiling of 8. This bypass only prevents
    // game/UI-side 'lobby full' checks from rejecting players 5-8 before Steam gets a chance.
    public static void ForceNotFull(ref bool __result)
    {
        if (__result)
        {
            __result = false;
            Plugin.ModLog?.LogInfo("FORCED lobby Full result: true -> false");
        }
    }

    // ---- Rope extension for players 5-8 ------------------------------------
    // Dont Pull Me keeps its rope visuals/physics in RopeStack arrays.
    // The stock prefab is sized for the original party.  Before the game rebuilds
    // the chain we clone the final stock segment until both arrays have room for 8.
    public static void RopeManagerAfterPlayerSpawn(object __instance)
        => PrepareRopeManager(__instance, "player-spawn");

    public static void RopeManagerBeforeRebuild(object __instance)
        => PrepareRopeManager(__instance, "before-rebuild");

    public static void RopeManagerAfterRebuild(object __instance)
        => LogRopeState(__instance, "after-rebuild");

    public static void RopeManagerAfterSceneLoad(object __instance)
        => PrepareRopeManager(__instance, "scene-load");

    public static void RopeManagerBeforeDelayedSetup(object __instance)
        => PrepareRopeManager(__instance, "delayed-setup");

    private static void PrepareRopeManager(object manager, string reason)
    {
        try
        {
            int players = GetCollectionCount(GetMember(manager, "_players"));
            if (players > 0)
                Plugin.ModLog?.LogInfo($"ROPE {reason}: players={players}");

            // Do not touch the original 1-4 player behaviour.
            if (players <= 4) return;

            object? template = GetMember(manager, "ropeStack");
            object? current = GetMember(manager, "_currentRopeStack");

            if (template != null) EnsureStackCapacity(template, "template");
            if (current != null && !ReferenceEquals(current, template))
                EnsureStackCapacity(current, "current");
        }
        catch (Exception ex)
        {
            Plugin.ModLog?.LogWarning($"ROPE {reason}: capacity preparation failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void LogRopeState(object manager, string reason)
    {
        try
        {
            int players = GetCollectionCount(GetMember(manager, "_players"));
            object? current = GetMember(manager, "_currentRopeStack");
            int ropes = current == null ? -1 : GetArrayLength(GetMember(current, "ropes"));
            int edges = current == null ? -1 : GetArrayLength(GetMember(current, "ropeEdgeColliders"));
            Plugin.ModLog?.LogInfo($"ROPE {reason}: players={players}, currentRopes={ropes}, edgeColliders={edges}");
        }
        catch (Exception ex)
        {
            Plugin.ModLog?.LogWarning($"ROPE {reason}: state logging failed: {ex.Message}");
        }
    }

    private static void EnsureStackCapacity(object stack, string label)
    {
        ExpandReferenceArrayMember(stack, "ropes", Plugin.MaxPlayers, label);
        ExpandReferenceArrayMember(stack, "ropeEdgeColliders", Plugin.MaxPlayers, label);
    }

    private static void ExpandReferenceArrayMember(object owner, string memberName, int targetLength, string label)
    {
        object? array = GetMember(owner, memberName);
        if (array == null) return;

        int oldLength = GetArrayLength(array);
        if (oldLength <= 0 || oldLength >= targetLength) return;

        Type arrayType = array.GetType();
        MethodInfo? getter = arrayType.GetMethod("get_Item", BindingFlags.Public | BindingFlags.Instance);
        MethodInfo? setter = arrayType.GetMethod("set_Item", BindingFlags.Public | BindingFlags.Instance);
        if (getter == null || setter == null)
        {
            Plugin.ModLog?.LogWarning($"ROPE {label}.{memberName}: array indexer not found ({arrayType.FullName}).");
            return;
        }

        object? source = null;
        for (int i = oldLength - 1; i >= 0 && source == null; --i)
            source = getter.Invoke(array, new object[] { i });
        if (source == null)
        {
            Plugin.ModLog?.LogWarning($"ROPE {label}.{memberName}: no source element to clone.");
            return;
        }

        object? expanded = CreateIl2CppArray(arrayType, targetLength);
        if (expanded == null)
        {
            Plugin.ModLog?.LogWarning($"ROPE {label}.{memberName}: could not allocate {arrayType.FullName}[{targetLength}].");
            return;
        }

        for (int i = 0; i < oldLength; ++i)
            setter.Invoke(expanded, new object?[] { i, getter.Invoke(array, new object[] { i }) });

        for (int i = oldLength; i < targetLength; ++i)
        {
            object? clone = CloneUnityObject(source);
            if (clone == null)
            {
                Plugin.ModLog?.LogWarning($"ROPE {label}.{memberName}: clone failed at index {i}; keeping source reference as fallback.");
                clone = source;
            }
            setter.Invoke(expanded, new object?[] { i, clone });
            source = clone;
        }

        if (SetMember(owner, memberName, expanded))
            Plugin.ModLog?.LogInfo($"ROPE EXPANDED {label}.{memberName}: {oldLength} -> {targetLength}");
        else
            Plugin.ModLog?.LogWarning($"ROPE {label}.{memberName}: expanded array could not be assigned.");
    }

    private static object? CreateIl2CppArray(Type arrayType, int length)
    {
        foreach (ConstructorInfo ctor in arrayType.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            ParameterInfo[] p = ctor.GetParameters();
            if (p.Length != 1) continue;
            try
            {
                if (p[0].ParameterType == typeof(int)) return ctor.Invoke(new object[] { length });
                if (p[0].ParameterType == typeof(long)) return ctor.Invoke(new object[] { (long)length });
                if (p[0].ParameterType == typeof(nint)) continue; // pointer constructor, not a length constructor
            }
            catch { }
        }
        return null;
    }

    private static object? CloneUnityObject(object original)
    {
        try
        {
            Type? unityObject = FindLoadedType("UnityEngine.Object");
            if (unityObject == null) return null;

            object? parent = null;
            PropertyInfo? transformProp = original.GetType().GetProperty("transform", BindingFlags.Public | BindingFlags.Instance);
            object? transform = transformProp?.GetValue(original);
            if (transform != null)
                parent = transform.GetType().GetProperty("parent", BindingFlags.Public | BindingFlags.Instance)?.GetValue(transform);

            MethodInfo? instantiate2 = unityObject.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => !m.IsGenericMethodDefinition && m.Name == "Instantiate" &&
                                     m.GetParameters().Length == 2 &&
                                     m.GetParameters()[0].ParameterType.FullName == "UnityEngine.Object" &&
                                     m.GetParameters()[1].ParameterType.FullName == "UnityEngine.Transform");
            if (instantiate2 != null && parent != null)
                return instantiate2.Invoke(null, new[] { original, parent });

            MethodInfo? instantiate1 = unityObject.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => !m.IsGenericMethodDefinition && m.Name == "Instantiate" &&
                                     m.GetParameters().Length == 1 &&
                                     m.GetParameters()[0].ParameterType.FullName == "UnityEngine.Object");
            return instantiate1?.Invoke(null, new[] { original });
        }
        catch (Exception ex)
        {
            Plugin.ModLog?.LogWarning($"ROPE clone failed: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private static Type? FindLoadedType(string fullName)
    {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                Type? direct = assembly.GetType(fullName, false);
                if (direct != null) return direct;
            }
            catch { }
        }
        return null;
    }

    private static object? GetMember(object owner, string name)
    {
        Type t = owner.GetType();
        try
        {
            PropertyInfo? p = t.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            if (p != null && p.CanRead) return p.GetValue(owner);
        }
        catch { }
        try
        {
            FieldInfo? f = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            if (f != null) return f.GetValue(owner);
        }
        catch { }
        return null;
    }

    private static bool SetMember(object owner, string name, object value)
    {
        Type t = owner.GetType();
        try
        {
            PropertyInfo? p = t.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            if (p != null && p.CanWrite) { p.SetValue(owner, value); return true; }
        }
        catch { }
        try
        {
            FieldInfo? f = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            if (f != null) { f.SetValue(owner, value); return true; }
        }
        catch { }
        return false;
    }

    private static int GetCollectionCount(object? collection)
    {
        if (collection == null) return 0;
        try
        {
            object? value = collection.GetType().GetProperty("Count", BindingFlags.Public | BindingFlags.Instance)?.GetValue(collection);
            if (value is int i) return i;
        }
        catch { }
        return 0;
    }

    private static int GetArrayLength(object? array)
    {
        if (array == null) return -1;
        try
        {
            object? value = array.GetType().GetProperty("Length", BindingFlags.Public | BindingFlags.Instance)?.GetValue(array);
            if (value is int i) return i;
            if (value is long l) return checked((int)l);
        }
        catch { }
        return -1;
    }

}
