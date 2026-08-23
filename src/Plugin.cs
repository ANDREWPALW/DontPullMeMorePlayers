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
    public const string PluginVersion = "1.0.8";
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
        Log.LogInfo("v1.0.8 keeps the proven 8-player network patch and adds transactional rope/lobby expansion with typed IL2CPP re-wrapping and safe fallbacks.");
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
                    if (idx >= 0) count += PatchIntArgument(harmony, method, idx);
                }
                else if (method.Name == "set_MaxMembers" && HasSingleIntParameter(method))
                    count += PatchIntArgument(harmony, method, 0);
                else if (method.Name == "get_MaxMembers" && method.ReturnType == typeof(int))
                    count += Patch(harmony, method, postfixName: nameof(Patches.MinimumEightResult));
                else if (method.Name == "get_Full" && method.ReturnType == typeof(bool))
                    count += Patch(harmony, method, postfixName: nameof(Patches.ForceNotFull));
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
                    if (idx >= 0) count += PatchIntArgument(harmony, method, idx);
                }
                else if (method.Name == "get_MaxMembers" && method.ReturnType == typeof(int))
                    count += Patch(harmony, method, postfixName: nameof(Patches.MinimumEightResult));
                else if (method.Name == "get_Full" && method.ReturnType == typeof(bool))
                    count += Patch(harmony, method, postfixName: nameof(Patches.ForceNotFull));
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
                    if (idx >= 0) count += PatchIntArgument(harmony, method, idx);
                }
                else if (method.Name == "GetLobbyMemberLimit" && method.ReturnType == typeof(int))
                    count += Patch(harmony, method, postfixName: nameof(Patches.MinimumEightResult));
            }
        }
        return count;
    }

    private static int PatchFishySteamworks(Harmony harmony)
    {
        int count = 0;
        foreach (Type type in FindTypes(t =>
                     t.FullName?.StartsWith("FishySteamworks", StringComparison.OrdinalIgnoreCase) == true &&
                     !t.IsInterface))
        {
            foreach (MethodInfo method in DeclaredMethods(type))
            {
                if (method.Name == "SetMaximumClients" && HasSingleIntParameter(method))
                    count += PatchIntArgument(harmony, method, 0);
                else if (method.Name == "StartConnection")
                {
                    int idx = FindIntParameter(method);
                    if (idx >= 0) count += PatchIntArgument(harmony, method, idx);
                }
                else if (method.Name == "GetMaximumClients" && method.ReturnType == typeof(int))
                    count += Patch(harmony, method, postfixName: nameof(Patches.MinimumEightResult));
            }
        }
        return count;
    }

    private static int PatchRopeSystem(Harmony harmony)
    {
        int count = 0;
        foreach (Type type in FindTypes(t => t.FullName == "GameAssets.Scripts.Managers.RopeManagerFishnet"))
        {
            foreach (MethodInfo method in DeclaredMethods(type))
            {
                if (method.Name == "RebuildRopeChain")
                    count += Patch(harmony, method, prefixName: nameof(Patches.RopeBeforeRebuild), postfixName: nameof(Patches.RopeAfterRebuild));
                else if (method.Name == "HandleServerCharacterSpawned")
                    count += Patch(harmony, method, postfixName: nameof(Patches.RopeAfterPlayerSpawn));
                else if (method.Name == "SceneManager_OnLoadEnd")
                    count += Patch(harmony, method, postfixName: nameof(Patches.RopeAfterSceneLoad));
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
                if (method.Name == "OtherUserJoined")
                    count += Patch(harmony, method, prefixName: nameof(Patches.LobbyBeforeOtherUserJoined));
                else if (method.Name == "LobbyJoinSuccess")
                    count += Patch(harmony, method, prefixName: nameof(Patches.LobbyBeforeJoinSuccess));
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
        if (prefix is null) return 0;
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
        for (int i = 0; i < p.Length; i++) if (p[i].ParameterType == typeof(int)) return i;
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

    public static void ForceNotFull(ref bool __result)
    {
        if (__result)
        {
            __result = false;
            Plugin.ModLog?.LogInfo("FORCED lobby Full result: true -> false");
        }
    }

    // ---------------------------------------------------------------------
    // Rope support. Stock Don't Pull Me has 3 rope actors + 4 edge points.
    // N players require N-1 rope actors + N edge points.
    // We only alter the TEMPLATE immediately before stock RebuildRopeChain.
    // Both arrays are prepared transactionally; partial states such as 3/8
    // (the v1.0.7 failure) are never committed.
    // ---------------------------------------------------------------------
    public static bool RopeBeforeRebuild(object __instance)
    {
        int players = GetCollectionCount(GetMember(__instance, "_players"));
        if (players <= 4) return true; // absolutely no rope changes for stock 1-4 play.
        players = Math.Min(players, Plugin.MaxPlayers);

        if (EnsureRopeTemplateCapacity(__instance, players))
        {
            Plugin.ModLog?.LogInfo($"ROPE READY before stock rebuild: players={players}, ropes={players - 1}, edges={players}.");
            return true;
        }

        // Safety fallback: never let a failed mod expansion call stock RebuildRopeChain
        // with 5 players and a 3/4 stack. That is exactly what caused the NRE/fall loop.
        Plugin.ModLog?.LogError($"ROPE expansion failed for {players} players. Skipping unsafe rebuild and teleporting players with the last valid stock stack.");
        TryInvokeNoArgs(__instance, "TeleportPlayersToSpawn");
        return false;
    }

    public static void RopeAfterRebuild(object __instance)
    {
        try
        {
            int players = Math.Min(GetCollectionCount(GetMember(__instance, "_players")), Plugin.MaxPlayers);
            if (players <= 4) return;

            object? current = GetMember(__instance, "_currentRopeStack");
            if (current == null)
            {
                Plugin.ModLog?.LogWarning("ROPE after-rebuild: current stack is null.");
                return;
            }

            // Extra template children are created inactive to avoid Obi registration while
            // editing the template. The stock current stack has now been instantiated, so
            // activate exactly the rope actors and edge points actually used by this party.
            ActivateArrayGameObjects(GetMember(current, "ropes"), players - 1);
            ActivateArrayGameObjects(GetMember(current, "ropeEdgeColliders"), players);

            int ropes = GetArrayLength(GetMember(current, "ropes"));
            int edges = GetArrayLength(GetMember(current, "ropeEdgeColliders"));
            Plugin.ModLog?.LogInfo($"ROPE after-rebuild: players={players}, currentRopes={ropes}, edgeColliders={edges}; used extras activated.");
        }
        catch (Exception ex)
        {
            Plugin.ModLog?.LogWarning($"ROPE after-rebuild activation failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    public static void RopeAfterPlayerSpawn(object __instance)
    {
        int players = GetCollectionCount(GetMember(__instance, "_players"));
        if (players >= 5) Plugin.ModLog?.LogInfo($"ROPE player-spawn: players={players}");
    }

    public static void RopeAfterSceneLoad(object __instance)
    {
        int players = GetCollectionCount(GetMember(__instance, "_players"));
        if (players >= 5) Plugin.ModLog?.LogInfo($"ROPE scene-load: players={players}; capacity will be checked by stock RebuildRopeChain.");
    }

    private static bool EnsureRopeTemplateCapacity(object manager, int players)
    {
        try
        {
            object? template = GetMember(manager, "ropeStack");
            if (template == null) return false;

            object? ropesArray = GetMember(template, "ropes");
            object? edgesArray = GetMember(template, "ropeEdgeColliders");
            if (ropesArray == null || edgesArray == null) return false;

            int targetRopes = Math.Max(1, players - 1);
            int targetEdges = Math.Max(2, players);
            int oldRopes = GetArrayLength(ropesArray);
            int oldEdges = GetArrayLength(edgesArray);
            if (oldRopes < 1 || oldEdges < 2) return false;
            if (oldRopes >= targetRopes && oldEdges >= targetEdges) return true;

            var createdObjects = new List<object>();
            object? newRopes = oldRopes >= targetRopes
                ? ropesArray
                : BuildExpandedArray(ropesArray, targetRopes, createdObjects, "rope");
            if (newRopes == null)
            {
                DestroyCreatedObjects(createdObjects);
                return false;
            }

            object? newEdges = oldEdges >= targetEdges
                ? edgesArray
                : BuildExpandedArray(edgesArray, targetEdges, createdObjects, "edge");
            if (newEdges == null)
            {
                DestroyCreatedObjects(createdObjects);
                return false;
            }

            // Commit only after BOTH arrays have been fully built.
            bool ropesSet = oldRopes >= targetRopes || SetMember(template, "ropes", newRopes);
            bool edgesSet = oldEdges >= targetEdges || SetMember(template, "ropeEdgeColliders", newEdges);
            if (!ropesSet || !edgesSet)
            {
                // If one setter failed, restore original arrays before returning.
                SetMember(template, "ropes", ropesArray);
                SetMember(template, "ropeEdgeColliders", edgesArray);
                DestroyCreatedObjects(createdObjects);
                return false;
            }

            Plugin.ModLog?.LogInfo($"ROPE TEMPLATE transaction committed: ropes {oldRopes}->{GetArrayLength(newRopes)}, edges {oldEdges}->{GetArrayLength(newEdges)}.");
            return true;
        }
        catch (Exception ex)
        {
            Plugin.ModLog?.LogWarning($"ROPE template transaction failed: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private static object? BuildExpandedArray(object oldArray, int targetLength, List<object> createdObjects, string kind)
    {
        int oldLength = GetArrayLength(oldArray);
        if (oldLength <= 0 || oldLength >= targetLength) return oldArray;

        Type arrayType = oldArray.GetType();
        MethodInfo? getter = arrayType.GetMethod("get_Item", BindingFlags.Public | BindingFlags.Instance);
        MethodInfo? setter = arrayType.GetMethod("set_Item", BindingFlags.Public | BindingFlags.Instance);
        if (getter == null || setter == null) return null;

        object? expanded = CreateIl2CppArray(arrayType, targetLength);
        if (expanded == null) return null;

        for (int i = 0; i < oldLength; i++)
            setter.Invoke(expanded, new object?[] { i, getter.Invoke(oldArray, new object[] { i }) });

        Type expectedType = setter.GetParameters()[1].ParameterType;
        object? source = getter.Invoke(oldArray, new object[] { oldLength - 1 });
        object? previous = oldLength > 1 ? getter.Invoke(oldArray, new object[] { oldLength - 2 }) : null;
        if (source == null) return null;

        for (int i = oldLength; i < targetLength; i++)
        {
            object? clone = CloneComponentOrTransform(source, expectedType, out object? cloneGo);
            if (clone == null || !expectedType.IsInstanceOfType(clone) || cloneGo == null)
            {
                Plugin.ModLog?.LogWarning($"ROPE {kind} clone failed at index {i}; expected {expectedType.FullName}.");
                return null;
            }

            // Extra template children stay inactive. RopeAfterRebuild activates the
            // corresponding cloned objects inside the instantiated CURRENT stack.
            TrySetActive(cloneGo, false);
            RepositionClone(previous, source, clone, 1);
            setter.Invoke(expanded, new object?[] { i, clone });
            createdObjects.Add(cloneGo);
            previous = source;
            source = clone;
        }
        return expanded;
    }

    // ---------------------------------------------------------------------
    // Lobby standing characters. UILobbyPanel.OtherUserJoined uses LINQ First()
    // to find a free LobbyPlayer. With only 4 serialized slots it throws on #5.
    // Create one typed LobbyPlayer slot BEFORE the stock method runs.
    // ---------------------------------------------------------------------
    public static bool LobbyBeforeOtherUserJoined(object __instance)
    {
        object? array = GetMember(__instance, "lobbyPlayer");
        if (array == null) return true;
        int slots = GetArrayLength(array);
        int members = GetLobbyMemberCount(__instance, null);
        int target = Math.Min(Plugin.MaxPlayers, Math.Max(slots + 1, members + 1));
        if (target > slots && !EnsureLobbySlots(__instance, target, "other-user-joined"))
        {
            Plugin.ModLog?.LogError("LOBBY: no free visual slot could be created; suppressing stock OtherUserJoined to avoid Sequence.First crash. Network connection remains intact.");
            return false;
        }
        return true;
    }

    public static void LobbyBeforeJoinSuccess(object __instance, object __0)
    {
        object? array = GetMember(__instance, "lobbyPlayer");
        if (array == null) return;
        int slots = GetArrayLength(array);
        int members = GetLobbyMemberCount(__instance, __0);
        if (members <= 0) members = 4;
        int target = Math.Clamp(members, 4, Plugin.MaxPlayers);
        if (target > slots) EnsureLobbySlots(__instance, target, "join-success");
    }

    private static bool EnsureLobbySlots(object panel, int targetLength, string reason)
    {
        try
        {
            object? oldArray = GetMember(panel, "lobbyPlayer");
            if (oldArray == null) return false;
            int oldLength = GetArrayLength(oldArray);
            targetLength = Math.Clamp(targetLength, oldLength, Plugin.MaxPlayers);
            if (targetLength <= oldLength) return true;

            Type arrayType = oldArray.GetType();
            MethodInfo? getter = arrayType.GetMethod("get_Item", BindingFlags.Public | BindingFlags.Instance);
            MethodInfo? setter = arrayType.GetMethod("set_Item", BindingFlags.Public | BindingFlags.Instance);
            if (getter == null || setter == null) return false;
            Type expectedType = setter.GetParameters()[1].ParameterType;

            object? expanded = CreateIl2CppArray(arrayType, targetLength);
            if (expanded == null) return false;
            for (int i = 0; i < oldLength; i++)
                setter.Invoke(expanded, new object?[] { i, getter.Invoke(oldArray, new object[] { i }) });

            object? source = getter.Invoke(oldArray, new object[] { oldLength - 1 });
            object? previous = oldLength > 1 ? getter.Invoke(oldArray, new object[] { oldLength - 2 }) : null;
            if (source == null) return false;

            var created = new List<object>();
            for (int i = oldLength; i < targetLength; i++)
            {
                object? clone = CloneComponentOrTransform(source, expectedType, out object? cloneGo);
                if (clone == null || !expectedType.IsInstanceOfType(clone) || cloneGo == null)
                {
                    DestroyCreatedObjects(created);
                    Plugin.ModLog?.LogWarning($"LOBBY {reason}: typed LobbyPlayer clone failed at slot {i}.");
                    return false;
                }

                RepositionClone(previous, source, clone, 1);
                ResetLobbySlot(clone, cloneGo);
                setter.Invoke(expanded, new object?[] { i, clone });
                created.Add(cloneGo);
                previous = source;
                source = clone;
            }

            if (!SetMember(panel, "lobbyPlayer", expanded))
            {
                DestroyCreatedObjects(created);
                return false;
            }

            Plugin.ModLog?.LogInfo($"LOBBY EXPANDED standing-character slots: {oldLength} -> {targetLength} ({reason}).");
            return true;
        }
        catch (Exception ex)
        {
            Plugin.ModLog?.LogWarning($"LOBBY {reason}: slot expansion failed: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private static void ResetLobbySlot(object lobbyPlayer, object cloneGo)
    {
        // Reset the copied occupant to the exact stock empty-slot state as closely as possible.
        // UserLeft() in the game clears its occupant id and deactivates the GameObject.
        try
        {
            MethodInfo? userLeft = lobbyPlayer.GetType().GetMethod("UserLeft", BindingFlags.Public | BindingFlags.Instance);
            if (userLeft != null)
            {
                userLeft.Invoke(lobbyPlayer, null);
                return;
            }
        }
        catch { }

        ResetMemberToDefault(lobbyPlayer, "userData");
        ResetMemberToDefault(lobbyPlayer, "lobbyMemeberData");
        TrySetActive(cloneGo, false);
    }

    // ---------------------------- IL2CPP helpers ---------------------------
    private static object? CloneComponentOrTransform(object source, Type expectedType, out object? cloneGo)
    {
        cloneGo = null;
        try
        {
            object? sourceGo = GetPropertyValue(source, "gameObject");
            if (sourceGo == null) return null;
            object? sourceTransform = GetPropertyValue(sourceGo, "transform");
            object? parent = sourceTransform == null ? null : GetPropertyValue(sourceTransform, "parent");

            object? cloneBase = InstantiateUnityObject(sourceGo, parent);
            if (cloneBase == null) return null;
            cloneGo = RewrapIl2Cpp(cloneBase, sourceGo.GetType()) ?? cloneBase;

            if (expectedType.FullName == "UnityEngine.Transform")
            {
                object? t = GetPropertyValue(cloneGo, "transform");
                return t == null ? null : RewrapIl2Cpp(t, expectedType);
            }

            object? componentBase = InvokeGetComponent(cloneGo, expectedType);
            if (componentBase == null) return null;
            return RewrapIl2Cpp(componentBase, expectedType);
        }
        catch (Exception ex)
        {
            Plugin.ModLog?.LogWarning($"Typed IL2CPP clone failed for {source.GetType().FullName}: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private static object? RewrapIl2Cpp(object value, Type targetType)
    {
        if (targetType.IsInstanceOfType(value)) return value;
        try
        {
            PropertyInfo? pointerProperty = null;
            for (Type? t = value.GetType(); t != null && pointerProperty == null; t = t.BaseType)
                pointerProperty = t.GetProperty("Pointer", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);

            if (pointerProperty?.GetValue(value) is not IntPtr ptr || ptr == IntPtr.Zero) return null;
            ConstructorInfo? ctor = targetType.GetConstructor(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                                                               binder: null,
                                                               types: new[] { typeof(IntPtr) },
                                                               modifiers: null);
            return ctor?.Invoke(new object[] { ptr });
        }
        catch (Exception ex)
        {
            Plugin.ModLog?.LogWarning($"IL2CPP re-wrap {value.GetType().FullName} -> {targetType.FullName} failed: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private static object? InvokeGetComponent(object gameObject, Type componentType)
    {
        foreach (MethodInfo m in gameObject.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            if (m.Name != "GetComponent" || m.IsGenericMethodDefinition) continue;
            ParameterInfo[] p = m.GetParameters();
            if (p.Length != 1 || p[0].ParameterType != typeof(Type)) continue;
            try { return m.Invoke(gameObject, new object[] { componentType }); }
            catch { }
        }
        return null;
    }

    private static object? InstantiateUnityObject(object original, object? parent)
    {
        Type? unityObject = FindLoadedType("UnityEngine.Object");
        if (unityObject == null) return null;

        // Prefer the ordinary non-generic UnityEngine.Object overload. Il2CppInterop can
        // return a base Object wrapper; RewrapIl2Cpp handles the concrete type afterwards.
        foreach (MethodInfo m in unityObject.GetMethods(BindingFlags.Public | BindingFlags.Static)
                     .Where(m => m.Name == "Instantiate" && !m.IsGenericMethodDefinition))
        {
            ParameterInfo[] p = m.GetParameters();
            try
            {
                if (p.Length == 2 && parent != null &&
                    p[0].ParameterType.FullName == "UnityEngine.Object" &&
                    p[1].ParameterType.FullName == "UnityEngine.Transform")
                    return m.Invoke(null, new[] { original, parent });
            }
            catch { }
        }

        // Fallback to generic overload if the non-generic overload was stripped.
        foreach (MethodInfo def in unityObject.GetMethods(BindingFlags.Public | BindingFlags.Static)
                     .Where(m => m.Name == "Instantiate" && m.IsGenericMethodDefinition))
        {
            try
            {
                MethodInfo m = def.MakeGenericMethod(original.GetType());
                ParameterInfo[] p = m.GetParameters();
                if (p.Length == 2 && parent != null && p[1].ParameterType.FullName == "UnityEngine.Transform")
                    return m.Invoke(null, new[] { original, parent });
                if (p.Length == 1) return m.Invoke(null, new[] { original });
            }
            catch { }
        }
        return null;
    }

    private static void ActivateArrayGameObjects(object? array, int usedCount)
    {
        if (array == null || usedCount <= 0) return;
        int length = GetArrayLength(array);
        MethodInfo? getter = array.GetType().GetMethod("get_Item", BindingFlags.Public | BindingFlags.Instance);
        if (getter == null) return;
        int n = Math.Min(length, usedCount);
        for (int i = 0; i < n; i++)
        {
            object? item = getter.Invoke(array, new object[] { i });
            object? go = item == null ? null : GetPropertyValue(item, "gameObject");
            if (go != null) TrySetActive(go, true);
        }
    }

    private static void DestroyCreatedObjects(IEnumerable<object> objects)
    {
        Type? unityObject = FindLoadedType("UnityEngine.Object");
        MethodInfo? destroy = unityObject?.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(m => m.Name == "Destroy" && m.GetParameters().Length == 1);
        if (destroy == null) return;
        foreach (object o in objects)
        {
            try { destroy.Invoke(null, new[] { o }); } catch { }
        }
    }

    private static void TrySetActive(object gameObject, bool active)
    {
        try
        {
            MethodInfo? m = gameObject.GetType().GetMethod("SetActive", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(bool) }, null);
            m?.Invoke(gameObject, new object[] { active });
        }
        catch { }
    }

    private static void TryInvokeNoArgs(object owner, string methodName)
    {
        try
        {
            MethodInfo? m = owner.GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);
            m?.Invoke(owner, null);
        }
        catch (Exception ex)
        {
            Plugin.ModLog?.LogWarning($"Fallback {methodName} failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static int GetLobbyMemberCount(object panel, object? lobbyCandidate)
    {
        object? lobby = lobbyCandidate ?? GetMember(panel, "lobby");
        if (lobby == null) return 0;
        foreach (string name in new[] { "MemberCount", "memberCount" })
        {
            try
            {
                PropertyInfo? p = lobby.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (p?.GetValue(lobby) is int i && i >= 0) return i;
            }
            catch { }
        }
        try
        {
            MethodInfo? m = lobby.GetType().GetMethod("get_MemberCount", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (m?.Invoke(lobby, null) is int i) return i;
        }
        catch { }
        return 0;
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

    private static void ResetMemberToDefault(object owner, string name)
    {
        Type t = owner.GetType();
        try
        {
            PropertyInfo? p = t.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (p != null && p.CanWrite)
            {
                p.SetValue(owner, p.PropertyType.IsValueType ? Activator.CreateInstance(p.PropertyType) : null);
                return;
            }
        }
        catch { }
        try
        {
            FieldInfo? f = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (f != null) f.SetValue(owner, f.FieldType.IsValueType ? Activator.CreateInstance(f.FieldType) : null);
        }
        catch { }
    }

    private static void RepositionClone(object? previous, object source, object clone, int step)
    {
        try
        {
            object? src = GetTransform(source);
            object? dst = GetTransform(clone);
            object? prev = previous == null ? null : GetTransform(previous);
            if (src == null || dst == null) return;
            PropertyInfo? srcLp = src.GetType().GetProperty("localPosition", BindingFlags.Public | BindingFlags.Instance);
            PropertyInfo? dstLp = dst.GetType().GetProperty("localPosition", BindingFlags.Public | BindingFlags.Instance);
            if (srcLp == null || dstLp == null || !dstLp.CanWrite) return;
            object? srcPos = srcLp.GetValue(src);
            object? prevPos = prev == null ? null : srcLp.GetValue(prev);
            if (srcPos == null) return;

            float sx = ReadFloatMember(srcPos, "x"), sy = ReadFloatMember(srcPos, "y"), sz = ReadFloatMember(srcPos, "z");
            float dx = 1.6f, dy = 0f, dz = 0f;
            if (prevPos != null)
            {
                dx = sx - ReadFloatMember(prevPos, "x");
                dy = sy - ReadFloatMember(prevPos, "y");
                dz = sz - ReadFloatMember(prevPos, "z");
                float mag2 = dx * dx + dy * dy + dz * dz;
                if (mag2 < 0.04f || mag2 > 25f) { dx = 1.6f; dy = 0f; dz = 0f; }
            }
            ConstructorInfo? ctor = srcPos.GetType().GetConstructor(new[] { typeof(float), typeof(float), typeof(float) });
            if (ctor == null) return;
            dstLp.SetValue(dst, ctor.Invoke(new object[] { sx + dx * step, sy + dy * step, sz + dz * step }));
        }
        catch { }
    }

    private static object? GetTransform(object value)
    {
        if (value.GetType().FullName == "UnityEngine.Transform") return value;
        return GetPropertyValue(value, "transform");
    }

    private static float ReadFloatMember(object value, string name)
    {
        try
        {
            FieldInfo? f = value.GetType().GetField(name, BindingFlags.Public | BindingFlags.Instance);
            if (f?.GetValue(value) is float ff) return ff;
            PropertyInfo? p = value.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (p?.GetValue(value) is float pf) return pf;
        }
        catch { }
        return 0f;
    }

    private static Type? FindLoadedType(string fullName)
    {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                Type? t = assembly.GetType(fullName, false);
                if (t != null) return t;
            }
            catch { }
        }
        return null;
    }

    private static object? GetPropertyValue(object owner, string name)
    {
        try { return owner.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(owner); }
        catch { return null; }
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
            object? v = collection.GetType().GetProperty("Count", BindingFlags.Public | BindingFlags.Instance)?.GetValue(collection);
            if (v is int i) return i;
        }
        catch { }
        return 0;
    }

    private static int GetArrayLength(object? array)
    {
        if (array == null) return -1;
        try
        {
            object? v = array.GetType().GetProperty("Length", BindingFlags.Public | BindingFlags.Instance)?.GetValue(array);
            if (v is int i) return i;
            if (v is long l) return checked((int)l);
        }
        catch { }
        return -1;
    }
}
