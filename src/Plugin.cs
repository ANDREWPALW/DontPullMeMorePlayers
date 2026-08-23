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
    public const string PluginVersion = "1.0.6";
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
        patched += PatchLobbyVisuals(_harmony);

        Log.LogInfo($"{PluginName}: installed {patched} Harmony patches.");
        Log.LogInfo("v1.0.6 fixes typed rope cloning and expands lobby character slots for players 5-8.");
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

    private static int PatchLobbyVisuals(Harmony harmony)
    {
        int count = 0;
        foreach (Type type in FindTypes(t => t.FullName == "UILobbyPanel"))
        {
            foreach (MethodInfo method in DeclaredMethods(type))
            {
                if (method.Name == "Start" || method.Name == "OpenPanel" || method.Name == "LobbyCreated")
                    count += Patch(harmony, method, postfixName: nameof(Patches.LobbyPanelAfterSetup));
                else if (method.Name == "LobbyJoinSuccess" || method.Name == "OtherUserJoined" || method.Name == "UserJoined")
                    count += Patch(harmony, method, prefixName: nameof(Patches.LobbyPanelBeforeMemberEvent));
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
    // Stock RopeStack = 3 ObiRope segments + 4 edge transforms, which is exactly
    // enough for 4 players.  For N players the chain needs N-1 ropes and N edges.
    // v1.0.6 clones the *typed* IL2CPP component via generic Object.Instantiate<T>,
    // avoiding the v1.0.5 UnityEngine.Object -> ObiRope conversion failure.
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

            if (players <= 4) return;

            int requiredRopes = Math.Max(3, players - 1);
            int requiredEdges = Math.Max(4, players);

            object? template = GetMember(manager, "ropeStack");
            object? current = GetMember(manager, "_currentRopeStack");

            if (template != null) EnsureStackCapacity(template, requiredRopes, requiredEdges, "template");
            if (current != null && !ReferenceEquals(current, template))
                EnsureStackCapacity(current, requiredRopes, requiredEdges, "current");
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

    private static void EnsureStackCapacity(object stack, int requiredRopes, int requiredEdges, string label)
    {
        ExpandReferenceArrayMember(stack, "ropes", requiredRopes, label, reposition: false);
        ExpandReferenceArrayMember(stack, "ropeEdgeColliders", requiredEdges, label, reposition: false);
    }

    // ---- Lobby standing-character extension -------------------------------
    // UILobbyPanel.lobbyPlayer is Il2CppReferenceArray<LobbyPlayer> and ships with
    // four scene slots.  Steam can now admit players 5-8, so we create extra lobby
    // character slots before member-join handling chooses a free slot.
    public static void LobbyPanelAfterSetup(object __instance)
        => EnsureLobbyPlayerSlots(__instance, "setup");

    public static void LobbyPanelBeforeMemberEvent(object __instance)
        => EnsureLobbyPlayerSlots(__instance, "member-event");

    private static void EnsureLobbyPlayerSlots(object panel, string reason)
    {
        try
        {
            object? array = GetMember(panel, "lobbyPlayer");
            if (array == null) return;

            int oldLength = GetArrayLength(array);
            if (oldLength < 0 || oldLength >= Plugin.MaxPlayers) return;

            if (oldLength == 0)
            {
                Plugin.ModLog?.LogWarning($"LOBBY {reason}: lobbyPlayer array is empty; cannot create extra visual slots.");
                return;
            }

            if (ExpandReferenceArrayValue(array, Plugin.MaxPlayers, out object? expanded, reposition: true, logicalName: "lobbyPlayer"))
            {
                if (expanded != null && SetMember(panel, "lobbyPlayer", expanded))
                    Plugin.ModLog?.LogInfo($"LOBBY EXPANDED standing-character slots: {oldLength} -> {Plugin.MaxPlayers}");
                else
                    Plugin.ModLog?.LogWarning($"LOBBY {reason}: expanded lobbyPlayer array could not be assigned.");
            }
        }
        catch (Exception ex)
        {
            Plugin.ModLog?.LogWarning($"LOBBY {reason}: slot expansion failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void ExpandReferenceArrayMember(object owner, string memberName, int targetLength, string label, bool reposition)
    {
        object? array = GetMember(owner, memberName);
        if (array == null) return;

        int oldLength = GetArrayLength(array);
        if (oldLength <= 0 || oldLength >= targetLength) return;

        if (!ExpandReferenceArrayValue(array, targetLength, out object? expanded, reposition, $"{label}.{memberName}"))
            return;

        if (expanded != null && SetMember(owner, memberName, expanded))
            Plugin.ModLog?.LogInfo($"ROPE EXPANDED {label}.{memberName}: {oldLength} -> {targetLength}");
        else
            Plugin.ModLog?.LogWarning($"ROPE {label}.{memberName}: expanded array could not be assigned.");
    }

    private static bool ExpandReferenceArrayValue(object array, int targetLength, out object? expanded, bool reposition, string logicalName)
    {
        expanded = null;
        int oldLength = GetArrayLength(array);
        if (oldLength <= 0 || oldLength >= targetLength) return false;

        Type arrayType = array.GetType();
        MethodInfo? getter = arrayType.GetMethod("get_Item", BindingFlags.Public | BindingFlags.Instance);
        MethodInfo? setter = arrayType.GetMethod("set_Item", BindingFlags.Public | BindingFlags.Instance);
        if (getter == null || setter == null)
        {
            Plugin.ModLog?.LogWarning($"{logicalName}: array indexer not found ({arrayType.FullName}).");
            return false;
        }

        object? source = null;
        object? previous = null;
        for (int i = oldLength - 1; i >= 0 && source == null; --i)
        {
            source = getter.Invoke(array, new object[] { i });
            if (source != null && i > 0)
                previous = getter.Invoke(array, new object[] { i - 1 });
        }
        if (source == null)
        {
            Plugin.ModLog?.LogWarning($"{logicalName}: no source element to clone.");
            return false;
        }

        expanded = CreateIl2CppArray(arrayType, targetLength);
        if (expanded == null)
        {
            Plugin.ModLog?.LogWarning($"{logicalName}: could not allocate {arrayType.FullName}[{targetLength}].");
            return false;
        }

        for (int i = 0; i < oldLength; ++i)
            setter.Invoke(expanded, new object?[] { i, getter.Invoke(array, new object[] { i }) });

        for (int i = oldLength; i < targetLength; ++i)
        {
            object? clone = CloneUnityObjectTyped(source);
            if (clone == null || !setter.GetParameters()[1].ParameterType.IsInstanceOfType(clone))
            {
                Plugin.ModLog?.LogWarning($"{logicalName}: typed clone failed at index {i}. Expected {setter.GetParameters()[1].ParameterType.FullName}, got {clone?.GetType().FullName ?? "null"}.");
                return false;
            }

            if (reposition)
                RepositionClone(previous, source, clone, i - oldLength + 1);

            ResetLobbyCloneIfNeeded(clone);
            setter.Invoke(expanded, new object?[] { i, clone });
            previous = source;
            source = clone;
        }

        return true;
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
            }
            catch { }
        }
        return null;
    }

    private static object? CloneUnityObjectTyped(object original)
    {
        try
        {
            Type? unityObject = FindLoadedType("UnityEngine.Object");
            if (unityObject == null) return null;

            object? transform = GetTransform(original);
            object? parent = transform == null ? null : GetPropertyValue(transform, "parent");

            // Prefer generic Instantiate<T>; its managed return value is T (ObiRope,
            // Transform, LobbyPlayer, ...), not the base UnityEngine.Object wrapper.
            foreach (MethodInfo def in unityObject.GetMethods(BindingFlags.Public | BindingFlags.Static)
                         .Where(m => m.Name == "Instantiate" && m.IsGenericMethodDefinition))
            {
                MethodInfo method;
                try { method = def.MakeGenericMethod(original.GetType()); }
                catch { continue; }

                ParameterInfo[] p = method.GetParameters();
                try
                {
                    if (p.Length == 2 && parent != null && p[1].ParameterType.FullName == "UnityEngine.Transform")
                    {
                        object? result = method.Invoke(null, new[] { original, parent });
                        if (result != null && original.GetType().IsInstanceOfType(result)) return result;
                    }
                    else if (p.Length == 1)
                    {
                        object? result = method.Invoke(null, new[] { original });
                        if (result != null && original.GetType().IsInstanceOfType(result)) return result;
                    }
                }
                catch { }
            }

            Plugin.ModLog?.LogWarning($"Typed Unity clone unavailable for {original.GetType().FullName}.");
            return null;
        }
        catch (Exception ex)
        {
            Plugin.ModLog?.LogWarning($"Typed Unity clone failed for {original.GetType().FullName}: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private static object? GetTransform(object value)
    {
        try
        {
            PropertyInfo? p = value.GetType().GetProperty("transform", BindingFlags.Public | BindingFlags.Instance);
            if (p != null) return p.GetValue(value);
        }
        catch { }

        if (value.GetType().FullName == "UnityEngine.Transform") return value;
        return null;
    }

    private static object? GetPropertyValue(object owner, string name)
    {
        try { return owner.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(owner); }
        catch { return null; }
    }

    private static void RepositionClone(object? previous, object source, object clone, int step)
    {
        try
        {
            object? srcTransform = GetTransform(source);
            object? cloneTransform = GetTransform(clone);
            object? prevTransform = previous == null ? null : GetTransform(previous);
            if (srcTransform == null || cloneTransform == null) return;

            PropertyInfo? lp = srcTransform.GetType().GetProperty("localPosition", BindingFlags.Public | BindingFlags.Instance);
            PropertyInfo? cloneLp = cloneTransform.GetType().GetProperty("localPosition", BindingFlags.Public | BindingFlags.Instance);
            if (lp == null || cloneLp == null || !cloneLp.CanWrite) return;

            object? srcPos = lp.GetValue(srcTransform);
            object? prevPos = prevTransform == null ? null : lp.GetValue(prevTransform);
            if (srcPos == null) return;

            float sx = ReadFloatMember(srcPos, "x");
            float sy = ReadFloatMember(srcPos, "y");
            float sz = ReadFloatMember(srcPos, "z");
            float dx = 1.6f, dy = 0f, dz = 0f;

            if (prevPos != null)
            {
                dx = sx - ReadFloatMember(prevPos, "x");
                dy = sy - ReadFloatMember(prevPos, "y");
                dz = sz - ReadFloatMember(prevPos, "z");
                float mag2 = dx * dx + dy * dy + dz * dz;
                if (mag2 < 0.04f || mag2 > 25f) { dx = 1.6f; dy = 0f; dz = 0f; }
            }

            Type vectorType = srcPos.GetType();
            ConstructorInfo? ctor = vectorType.GetConstructor(new[] { typeof(float), typeof(float), typeof(float) });
            if (ctor == null) return;
            object newPos = ctor.Invoke(new object[] { sx + dx, sy + dy, sz + dz });
            cloneLp.SetValue(cloneTransform, newPos);
        }
        catch (Exception ex)
        {
            Plugin.ModLog?.LogWarning($"LOBBY clone reposition failed: {ex.Message}");
        }
    }

    private static float ReadFloatMember(object value, string name)
    {
        Type t = value.GetType();
        try
        {
            FieldInfo? f = t.GetField(name, BindingFlags.Public | BindingFlags.Instance);
            if (f?.GetValue(value) is float ff) return ff;
            PropertyInfo? p = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (p?.GetValue(value) is float pf) return pf;
        }
        catch { }
        return 0f;
    }

    private static void ResetLobbyCloneIfNeeded(object clone)
    {
        // Only LobbyPlayer exposes UserLeft(). Calling it on a freshly cloned empty
        // slot is harmless; if expansion happens late, it prevents duplicating the
        // 4th player's user data/skin into the new slot.
        if (clone.GetType().Name != "LobbyPlayer") return;
        try
        {
            MethodInfo? userLeft = clone.GetType().GetMethod("UserLeft", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
            userLeft?.Invoke(clone, null);
        }
        catch (Exception ex)
        {
            Plugin.ModLog?.LogWarning($"LOBBY cloned slot reset failed: {ex.Message}");
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
