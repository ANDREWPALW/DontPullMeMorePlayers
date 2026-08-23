using System.Reflection;
using System.Diagnostics;
using System.Runtime.InteropServices;
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
    public const string PluginVersion = "1.0.9";
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
        patched += PatchDiagnostics(_harmony);

        Log.LogInfo($"{PluginName}: installed {patched} Harmony patches.");
        Log.LogInfo("v1.0.9 SAFE DIAGNOSTIC: network behavior is based on proven v1.0.4. RopeStack and lobbyPlayer arrays are NEVER modified.");
        Diagnostics.DumpNativeMap();
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
                     t.FullName?.StartsWith("FishySteamworks", StringComparison.OrdinalIgnoreCase) == true))
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


    private static int PatchDiagnostics(Harmony harmony)
    {
        int count = 0;
        foreach (Type type in FindTypes(t => t.FullName == "GameAssets.Scripts.Managers.RopeManagerFishnet"))
        {
            foreach (MethodInfo method in DeclaredMethods(type))
            {
                if (method.Name == "HandleServerCharacterSpawned")
                    count += Patch(harmony, method, postfixName: nameof(Diagnostics.RopePlayerSpawned));
            }
        }
        foreach (Type type in FindTypes(t => t.FullName == "UILobbyPanel"))
        {
            foreach (MethodInfo method in DeclaredMethods(type))
            {
                if (method.Name == "OtherUserJoined")
                    count += Patch(harmony, method, prefixName: nameof(Diagnostics.BeforeOtherUserJoined));
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

internal static class Diagnostics
{
    private static bool _nativeDumpDone;
    private static bool _ropeFiveDumpDone;
    private static bool _lobbyFiveDumpDone;

    public static void DumpNativeMap()
    {
        if (_nativeDumpDone) return;
        _nativeDumpDone = true;
        try
        {
            IntPtr baseAddr = IntPtr.Zero;
            try
            {
                foreach (ProcessModule m in Process.GetCurrentProcess().Modules)
                {
                    if (string.Equals(Path.GetFileName(m.FileName), "GameAssembly.dll", StringComparison.OrdinalIgnoreCase))
                    {
                        baseAddr = m.BaseAddress;
                        Plugin.ModLog?.LogInfo($"DIAG GameAssembly base=0x{baseAddr.ToInt64():X}");
                        break;
                    }
                }
            }
            catch (Exception ex) { Plugin.ModLog?.LogWarning($"DIAG module base failed: {ex.Message}"); }

            string[] names = {
                "GameAssets.Scripts.Managers.RopeManagerFishnet",
                "UILobbyPanel",
                "LobbyPlayer"
            };
            foreach (string n in names)
            {
                Type? t = FindLoadedType(n);
                if (t != null) DumpTypeNativePointers(t, baseAddr);
            }

            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            foreach (Type t in SafeTypes(a))
            {
                string fn = t.FullName ?? "";
                if (fn.Contains("DelayedRopeSetup", StringComparison.OrdinalIgnoreCase) ||
                    fn.Contains("DelayedRopeChainRebuild", StringComparison.OrdinalIgnoreCase))
                    DumpTypeNativePointers(t, baseAddr);
            }
        }
        catch (Exception ex)
        {
            Plugin.ModLog?.LogWarning($"DIAG native map failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void DumpTypeNativePointers(Type t, IntPtr baseAddr)
    {
        Plugin.ModLog?.LogInfo($"DIAG TYPE {t.FullName}");
        foreach (FieldInfo f in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
        {
            if (!f.Name.StartsWith("NativeMethodInfoPtr_", StringComparison.Ordinal)) continue;
            try
            {
                object? v = f.GetValue(null);
                if (v is not IntPtr methodInfo || methodInfo == IntPtr.Zero) continue;
                IntPtr native = Marshal.ReadIntPtr(methodInfo);
                long rva = baseAddr == IntPtr.Zero ? 0 : native.ToInt64() - baseAddr.ToInt64();
                Plugin.ModLog?.LogInfo($"DIAG METHOD {t.FullName}.{f.Name} methodInfo=0x{methodInfo.ToInt64():X} native=0x{native.ToInt64():X} gameRva=0x{rva:X}");
            }
            catch (Exception ex)
            {
                Plugin.ModLog?.LogWarning($"DIAG METHOD {t.FullName}.{f.Name} failed: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    public static void RopePlayerSpawned(object __instance)
    {
        try
        {
            int count = GetCollectionCount(GetMember(__instance, "_players"));
            if (count < 5 || _ropeFiveDumpDone) return;
            _ropeFiveDumpDone = true;
            Plugin.ModLog?.LogInfo($"DIAG ROPE 5TH PLAYER DETECTED count={count}. READ-ONLY dump follows.");
            DumpRopeStack("template", GetMember(__instance, "ropeStack"));
            DumpRopeStack("current", GetMember(__instance, "_currentRopeStack"));
        }
        catch (Exception ex)
        {
            Plugin.ModLog?.LogWarning($"DIAG rope dump failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    public static void BeforeOtherUserJoined(object __instance)
    {
        try
        {
            object? arr = GetMember(__instance, "lobbyPlayer");
            int len = GetArrayLength(arr);
            int occupied = 0;
            if (arr != null)
            {
                MethodInfo? getter = arr.GetType().GetMethod("get_Item", BindingFlags.Public | BindingFlags.Instance);
                if (getter != null)
                {
                    for (int i = 0; i < len; i++)
                    {
                        object? lp = getter.Invoke(arr, new object[] { i });
                        if (lp == null) continue;
                        object? go = GetProperty(lp, "gameObject");
                        object? active = go == null ? null : GetProperty(go, "activeSelf");
                        if (active is bool b && b) occupied++;
                    }
                }
            }
            if (len == 4 && occupied >= 4 && !_lobbyFiveDumpDone)
            {
                _lobbyFiveDumpDone = true;
                Plugin.ModLog?.LogInfo($"DIAG LOBBY 5TH JOIN about to hit stock 4-slot panel. slots={len}, occupied={occupied}. READ-ONLY dump follows.");
                DumpArray("lobbyPlayer", arr);
            }
        }
        catch (Exception ex)
        {
            Plugin.ModLog?.LogWarning($"DIAG lobby dump failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void DumpRopeStack(string label, object? stack)
    {
        if (stack == null)
        {
            Plugin.ModLog?.LogInfo($"DIAG ROPE {label}=null");
            return;
        }
        Plugin.ModLog?.LogInfo($"DIAG ROPE {label} type={stack.GetType().FullName}");
        DumpArray($"{label}.ropes", GetMember(stack, "ropes"));
        DumpArray($"{label}.ropeEdgeColliders", GetMember(stack, "ropeEdgeColliders"));
    }

    private static void DumpArray(string label, object? arr)
    {
        int len = GetArrayLength(arr);
        Plugin.ModLog?.LogInfo($"DIAG ARRAY {label} type={arr?.GetType().FullName ?? "null"} length={len}");
        if (arr == null || len <= 0) return;
        MethodInfo? getter = arr.GetType().GetMethod("get_Item", BindingFlags.Public | BindingFlags.Instance);
        if (getter == null) return;
        for (int i = 0; i < len; i++)
        {
            try
            {
                object? item = getter.Invoke(arr, new object[] { i });
                if (item == null)
                {
                    Plugin.ModLog?.LogInfo($"DIAG ITEM {label}[{i}]=null");
                    continue;
                }
                object? go = GetProperty(item, "gameObject");
                object? tr = go == null ? GetProperty(item, "transform") : GetProperty(go, "transform");
                object? parent = tr == null ? null : GetProperty(tr, "parent");
                Plugin.ModLog?.LogInfo($"DIAG ITEM {label}[{i}] type={item.GetType().FullName} ptr={PointerHex(item)} go={ObjectName(go)} active={ActiveState(go)} parent={ObjectName(parent)} pos={PositionString(tr)}");
            }
            catch (Exception ex)
            {
                Plugin.ModLog?.LogWarning($"DIAG ITEM {label}[{i}] failed: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    private static string PointerHex(object o)
    {
        try
        {
            for (Type? t=o.GetType(); t!=null; t=t.BaseType)
            {
                PropertyInfo? p=t.GetProperty("Pointer", BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.DeclaredOnly);
                if (p?.GetValue(o) is IntPtr ptr) return $"0x{ptr.ToInt64():X}";
            }
        }
        catch { }
        return "?";
    }

    private static object? GetProperty(object owner, string name)
    {
        try { return owner.GetType().GetProperty(name, BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance)?.GetValue(owner); }
        catch { return null; }
    }

    private static string ObjectName(object? o)
    {
        if (o == null) return "null";
        try { return GetProperty(o, "name")?.ToString() ?? o.GetType().Name; }
        catch { return o.GetType().Name; }
    }

    private static string ActiveState(object? go)
    {
        if (go == null) return "n/a";
        try { return GetProperty(go, "activeSelf")?.ToString() ?? "?"; }
        catch { return "?"; }
    }

    private static string PositionString(object? tr)
    {
        if (tr == null) return "n/a";
        try { return GetProperty(tr, "localPosition")?.ToString() ?? GetProperty(tr, "position")?.ToString() ?? "?"; }
        catch { return "?"; }
    }

    private static object? GetMember(object owner, string name)
    {
        Type t=owner.GetType();
        try
        {
            PropertyInfo? p=t.GetProperty(name, BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance);
            if (p!=null) return p.GetValue(owner);
            FieldInfo? f=t.GetField(name, BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance);
            return f?.GetValue(owner);
        }
        catch { return null; }
    }

    private static int GetCollectionCount(object? o)
    {
        if (o==null) return 0;
        try
        {
            PropertyInfo? p=o.GetType().GetProperty("Count", BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance);
            if (p?.GetValue(o) is int i) return i;
        }
        catch { }
        return 0;
    }

    private static int GetArrayLength(object? o)
    {
        if (o==null) return 0;
        try
        {
            PropertyInfo? p=o.GetType().GetProperty("Length", BindingFlags.Public|BindingFlags.Instance);
            if (p?.GetValue(o) is int i) return i;
        }
        catch { }
        return 0;
    }

    private static Type? FindLoadedType(string fullName)
    {
        foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            foreach (Type t in SafeTypes(a))
                if (t.FullName == fullName) return t;
        return null;
    }

    private static IEnumerable<Type> SafeTypes(Assembly a)
    {
        try { return a.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.OfType<Type>(); }
        catch { return Array.Empty<Type>(); }
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
}
