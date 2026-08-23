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
    public const string PluginVersion = "1.0.7";
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
        Log.LogInfo("v1.0.7 uses safe template-only rope expansion and incremental lobby visual slots.");
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
                if (method.Name == "Awake")
                    count += Patch(harmony, method, postfixName: nameof(Patches.RopeManagerAfterAwake));
                else if (method.Name == "RebuildRopeChain")
                    count += Patch(harmony, method, postfixName: nameof(Patches.RopeManagerAfterRebuild));
                else if (method.Name == "SceneManager_OnLoadEnd")
                    count += Patch(harmony, method, postfixName: nameof(Patches.RopeManagerAfterSceneLoad));
                else if (method.Name == "HandleServerCharacterSpawned")
                    count += Patch(harmony, method, postfixName: nameof(Patches.RopeManagerAfterPlayerSpawn));
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
                // Expand only when membership actually requires another visible slot.
                // This avoids instantiating four extra LobbyPlayer objects during Start().
                if (method.Name == "LobbyJoinSuccess")
                    count += Patch(harmony, method, postfixName: nameof(Patches.LobbyPanelAfterJoinSuccess));
                else if (method.Name == "OtherUserJoined")
                    count += Patch(harmony, method, prefixName: nameof(Patches.LobbyPanelBeforeOtherUserJoined));
                else if (method.Name == "UserJoined")
                    count += Patch(harmony, method, prefixName: nameof(Patches.LobbyPanelBeforeUserJoined));
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

    // ---- Safe rope extension for players 5-8 -------------------------------
    // IMPORTANT: v1.0.6 cloned active ObiRope components in _currentRopeStack.
    // Obi keeps native solver state, so cloning a live actor can crash Unity outside
    // managed exception handling. v1.0.7 never mutates or clones the live stack.
    // We extend only the serialized/template RopeStack before the live chain is built.
    public static void RopeManagerAfterAwake(object __instance)
        => PrepareRopeTemplate(__instance, "awake");

    public static void RopeManagerAfterSceneLoad(object __instance)
    {
        // Scene changes can replace the template. Only touch it if no live rope stack
        // exists yet; otherwise leave the current physical simulation completely alone.
        PrepareRopeTemplate(__instance, "scene-load");
        LogRopeState(__instance, "scene-load-state");
    }

    public static void RopeManagerAfterPlayerSpawn(object __instance)
    {
        // Diagnostics only. The game itself rebuilds the chain after spawning.
        LogRopeState(__instance, "player-spawn");
    }

    public static void RopeManagerAfterRebuild(object __instance)
        => LogRopeState(__instance, "after-rebuild");

    private static void PrepareRopeTemplate(object manager, string reason)
    {
        try
        {
            object? current = GetMember(manager, "_currentRopeStack");
            if (current != null)
            {
                Plugin.ModLog?.LogInfo($"ROPE {reason}: live stack already exists; template mutation skipped for safety.");
                return;
            }

            object? template = GetMember(manager, "ropeStack");
            if (template == null)
            {
                Plugin.ModLog?.LogWarning($"ROPE {reason}: ropeStack template is null.");
                return;
            }

            // Prepare the maximum supported template once: N players require N-1 ropes
            // and N edge transforms. The runtime stack will then be created by the game.
            int oldRopes = GetArrayLength(GetMember(template, "ropes"));
            int oldEdges = GetArrayLength(GetMember(template, "ropeEdgeColliders"));
            if (oldRopes >= Plugin.MaxPlayers - 1 && oldEdges >= Plugin.MaxPlayers)
                return;

            Plugin.ModLog?.LogInfo($"ROPE {reason}: preparing inactive template, ropes={oldRopes}, edges={oldEdges}.");

            ExpandTemplateArrayMember(template, "ropes", Plugin.MaxPlayers - 1, "template.ropes");
            ExpandTemplateArrayMember(template, "ropeEdgeColliders", Plugin.MaxPlayers, "template.ropeEdgeColliders");

            Plugin.ModLog?.LogInfo(
                $"ROPE TEMPLATE READY: ropes={GetArrayLength(GetMember(template, "ropes"))}, " +
                $"edges={GetArrayLength(GetMember(template, "ropeEdgeColliders"))}");
        }
        catch (Exception ex)
        {
            Plugin.ModLog?.LogWarning($"ROPE {reason}: safe template preparation failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void LogRopeState(object manager, string reason)
    {
        try
        {
            int players = GetCollectionCount(GetMember(manager, "_players"));
            object? template = GetMember(manager, "ropeStack");
            object? current = GetMember(manager, "_currentRopeStack");
            int templateRopes = template == null ? -1 : GetArrayLength(GetMember(template, "ropes"));
            int currentRopes = current == null ? -1 : GetArrayLength(GetMember(current, "ropes"));
            int edges = current == null ? -1 : GetArrayLength(GetMember(current, "ropeEdgeColliders"));
            Plugin.ModLog?.LogInfo($"ROPE {reason}: players={players}, templateRopes={templateRopes}, currentRopes={currentRopes}, edgeColliders={edges}");
        }
        catch (Exception ex)
        {
            Plugin.ModLog?.LogWarning($"ROPE {reason}: state logging failed: {ex.Message}");
        }
    }

    private static void ExpandTemplateArrayMember(object owner, string memberName, int targetLength, string logicalName)
    {
        object? array = GetMember(owner, memberName);
        if (array == null) return;
        int oldLength = GetArrayLength(array);
        if (oldLength <= 0 || oldLength >= targetLength) return;

        Type arrayType = array.GetType();
        MethodInfo? getter = arrayType.GetMethod("get_Item", BindingFlags.Public | BindingFlags.Instance);
        MethodInfo? setter = arrayType.GetMethod("set_Item", BindingFlags.Public | BindingFlags.Instance);
        if (getter == null || setter == null) return;

        object? expanded = CreateIl2CppArray(arrayType, targetLength);
        if (expanded == null) return;

        for (int i = 0; i < oldLength; ++i)
            setter.Invoke(expanded, new object?[] { i, getter.Invoke(array, new object[] { i }) });

        object? source = getter.Invoke(array, new object[] { oldLength - 1 });
        if (source == null) return;

        object? previous = oldLength > 1 ? getter.Invoke(array, new object[] { oldLength - 2 }) : null;
        for (int i = oldLength; i < targetLength; ++i)
        {
            object? clone = CloneTemplateElementSafely(source);
            Type expected = setter.GetParameters()[1].ParameterType;
            if (clone == null || !expected.IsInstanceOfType(clone))
            {
                Plugin.ModLog?.LogWarning($"{logicalName}: safe clone failed at {i}; original template left unchanged.");
                return;
            }

            RepositionClone(previous, source, clone, i - oldLength + 1);
            setter.Invoke(expanded, new object?[] { i, clone });
            previous = source;
            source = clone;
        }

        if (SetMember(owner, memberName, expanded))
            Plugin.ModLog?.LogInfo($"ROPE TEMPLATE EXPANDED {logicalName}: {oldLength} -> {targetLength}");
    }

    private static object? CloneTemplateElementSafely(object source)
    {
        // Clone the containing GameObject while it is inactive. For Obi components this
        // prevents OnEnable/AddToSolver from registering the clone with the live native solver.
        try
        {
            object? sourceGo = GetPropertyValue(source, "gameObject");
            if (sourceGo == null)
                return CloneUnityObjectTyped(source);

            bool wasActive = ReadBoolProperty(sourceGo, "activeSelf");
            if (wasActive) InvokeMethod(sourceGo, "SetActive", false);

            object? sourceTransform = GetPropertyValue(sourceGo, "transform");
            object? parent = sourceTransform == null ? null : GetPropertyValue(sourceTransform, "parent");
            object? cloneGo = InstantiateUnityObject(sourceGo, parent);

            if (wasActive) InvokeMethod(sourceGo, "SetActive", true);
            if (cloneGo == null) return null;

            // Template clones stay inactive. A later stock Instantiate of the whole RopeStack
            // creates/activates the real simulation objects in the normal game path.
            InvokeMethod(cloneGo, "SetActive", false);

            if (source.GetType().FullName == "UnityEngine.Transform")
                return GetPropertyValue(cloneGo, "transform");

            MethodInfo? getComponent = cloneGo.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == "GetComponent" && !m.IsGenericMethod &&
                                     m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(Type));
            object? component = getComponent?.Invoke(cloneGo, new object[] { source.GetType() });
            return component;
        }
        catch (Exception ex)
        {
            Plugin.ModLog?.LogWarning($"Safe template clone failed for {source.GetType().FullName}: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    // ---- Incremental lobby standing-character extension -------------------
    // v1.0.6 cloned 4 extra LobbyPlayer objects at Start(). v1.0.7 creates only
    // the number of visual slots required by the current Steam lobby membership.
    public static void LobbyPanelAfterJoinSuccess(object __instance)
        => EnsureLobbyPlayerSlots(__instance, "join-success", additionalIncoming: 0);

    public static void LobbyPanelBeforeOtherUserJoined(object __instance)
        => EnsureLobbyPlayerSlots(__instance, "other-user-joined", additionalIncoming: 1);

    public static void LobbyPanelBeforeUserJoined(object __instance)
        => EnsureLobbyPlayerSlots(__instance, "user-joined", additionalIncoming: 0);

    private static void EnsureLobbyPlayerSlots(object panel, string reason, int additionalIncoming)
    {
        try
        {
            object? array = GetMember(panel, "lobbyPlayer");
            if (array == null) return;

            int oldLength = GetArrayLength(array);
            if (oldLength <= 0 || oldLength >= Plugin.MaxPlayers) return;

            int memberCount = GetLobbyMemberCount(panel);
            int target = Math.Clamp(Math.Max(4, memberCount), 4, Plugin.MaxPlayers);
            // Some Steam callbacks fire just before LobbyData.MemberCount is refreshed.
            // In that narrow case reserve exactly one new slot, never several at once.
            if (additionalIncoming > 0 && target <= oldLength && memberCount >= oldLength)
                target = Math.Min(Plugin.MaxPlayers, oldLength + 1);
            if (target <= oldLength) return;

            Plugin.ModLog?.LogInfo($"LOBBY {reason}: members={memberCount}, slots={oldLength}, requested={target}");

            if (ExpandLobbyPlayerArray(array, target, out object? expanded) && expanded != null && SetMember(panel, "lobbyPlayer", expanded))
                Plugin.ModLog?.LogInfo($"LOBBY EXPANDED standing-character slots: {oldLength} -> {target}");
            else
                Plugin.ModLog?.LogWarning($"LOBBY {reason}: safe incremental expansion failed; stock slots preserved.");
        }
        catch (Exception ex)
        {
            Plugin.ModLog?.LogWarning($"LOBBY {reason}: slot expansion failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static int GetLobbyMemberCount(object panel)
    {
        try
        {
            object? lobby = GetMember(panel, "lobby");
            if (lobby == null) return 0;
            object? value = lobby.GetType().GetProperty("MemberCount", BindingFlags.Public | BindingFlags.Instance)?.GetValue(lobby)
                         ?? lobby.GetType().GetProperty("memberCount", BindingFlags.Public | BindingFlags.Instance)?.GetValue(lobby);
            if (value is int i) return i;
            MethodInfo? getter = lobby.GetType().GetMethod("get_MemberCount", BindingFlags.Public | BindingFlags.Instance);
            if (getter?.Invoke(lobby, null) is int j) return j;
        }
        catch { }
        return 0;
    }

    private static bool ExpandLobbyPlayerArray(object array, int targetLength, out object? expanded)
    {
        expanded = null;
        int oldLength = GetArrayLength(array);
        if (oldLength <= 0 || oldLength >= targetLength) return false;

        Type arrayType = array.GetType();
        MethodInfo? getter = arrayType.GetMethod("get_Item", BindingFlags.Public | BindingFlags.Instance);
        MethodInfo? setter = arrayType.GetMethod("set_Item", BindingFlags.Public | BindingFlags.Instance);
        if (getter == null || setter == null) return false;

        expanded = CreateIl2CppArray(arrayType, targetLength);
        if (expanded == null) return false;
        for (int i = 0; i < oldLength; ++i)
            setter.Invoke(expanded, new object?[] { i, getter.Invoke(array, new object[] { i }) });

        object? source = getter.Invoke(array, new object[] { oldLength - 1 });
        object? previous = oldLength > 1 ? getter.Invoke(array, new object[] { oldLength - 2 }) : null;
        if (source == null) return false;

        for (int i = oldLength; i < targetLength; ++i)
        {
            object? clone = CloneLobbyPlayerSafely(source);
            if (clone == null || !setter.GetParameters()[1].ParameterType.IsInstanceOfType(clone))
                return false;

            RepositionClone(previous, source, clone, 1);
            ResetLobbyCloneFields(clone);
            setter.Invoke(expanded, new object?[] { i, clone });
            previous = source;
            source = clone;
        }
        return true;
    }

    private static object? CloneLobbyPlayerSafely(object source)
    {
        try
        {
            object? go = GetPropertyValue(source, "gameObject");
            if (go == null) return null;
            object? transform = GetPropertyValue(go, "transform");
            object? parent = transform == null ? null : GetPropertyValue(transform, "parent");
            object? cloneGo = InstantiateUnityObject(go, parent);
            if (cloneGo == null) return null;

            MethodInfo? getComponent = cloneGo.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == "GetComponent" && !m.IsGenericMethod &&
                                     m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(Type));
            return getComponent?.Invoke(cloneGo, new object[] { source.GetType() });
        }
        catch (Exception ex)
        {
            Plugin.ModLog?.LogWarning($"LOBBY safe clone failed: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private static void ResetLobbyCloneFields(object clone)
    {
        // Do not call LobbyPlayer.UserLeft() on a clone: it can invoke lobby/voice side effects.
        // Clear only occupancy data so UILobbyPanel considers the slot free.
        ResetMemberToDefault(clone, "userData");
        ResetMemberToDefault(clone, "lobbyMemeberData");

        try
        {
            object? nameText = GetMember(clone, "nameText");
            if (nameText != null) SetMember(nameText, "text", string.Empty);
        }
        catch { }
    }

    private static void ResetMemberToDefault(object owner, string name)
    {
        Type t = owner.GetType();
        try
        {
            PropertyInfo? p = t.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (p != null && p.CanWrite)
            {
                object? value = p.PropertyType.IsValueType ? Activator.CreateInstance(p.PropertyType) : null;
                p.SetValue(owner, value);
                return;
            }
        }
        catch { }
        try
        {
            FieldInfo? f = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (f != null)
            {
                object? value = f.FieldType.IsValueType ? Activator.CreateInstance(f.FieldType) : null;
                f.SetValue(owner, value);
            }
        }
        catch { }
    }

    private static object? InstantiateUnityObject(object original, object? parent)
    {
        Type? unityObject = FindLoadedType("UnityEngine.Object");
        if (unityObject == null) return null;
        Type originalType = original.GetType();

        foreach (MethodInfo def in unityObject.GetMethods(BindingFlags.Public | BindingFlags.Static)
                     .Where(m => m.Name == "Instantiate" && m.IsGenericMethodDefinition))
        {
            MethodInfo method;
            try { method = def.MakeGenericMethod(originalType); }
            catch { continue; }
            ParameterInfo[] pp = method.GetParameters();
            try
            {
                if (pp.Length == 2 && parent != null && pp[1].ParameterType.FullName == "UnityEngine.Transform")
                {
                    object? result = method.Invoke(null, new[] { original, parent });
                    if (result != null) return result;
                }
                if (pp.Length == 1)
                {
                    object? result = method.Invoke(null, new[] { original });
                    if (result != null) return result;
                }
            }
            catch { }
        }
        return null;
    }

    private static bool ReadBoolProperty(object owner, string name)
    {
        try
        {
            object? v = owner.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(owner);
            return v is bool b && b;
        }
        catch { return false; }
    }

    private static object? InvokeMethod(object owner, string name, params object?[] args)
    {
        try
        {
            foreach (MethodInfo m in owner.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (m.Name != name || m.GetParameters().Length != args.Length) continue;
                try { return m.Invoke(owner, args); } catch { }
            }
        }
        catch { }
        return null;
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
            foreach (MethodInfo def in unityObject.GetMethods(BindingFlags.Public | BindingFlags.Static)
                         .Where(m => m.Name == "Instantiate" && m.IsGenericMethodDefinition))
            {
                MethodInfo method;
                try { method = def.MakeGenericMethod(original.GetType()); }
                catch { continue; }
                ParameterInfo[] pp = method.GetParameters();
                try
                {
                    if (pp.Length == 1)
                    {
                        object? result = method.Invoke(null, new[] { original });
                        if (result != null && original.GetType().IsInstanceOfType(result)) return result;
                    }
                }
                catch { }
            }
        }
        catch { }
        return null;
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
            object newPos = ctor.Invoke(new object[] { sx + dx * step, sy + dy * step, sz + dz * step });
            cloneLp.SetValue(cloneTransform, newPos);
        }
        catch (Exception ex)
        {
            Plugin.ModLog?.LogWarning($"Clone reposition failed: {ex.Message}");
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
