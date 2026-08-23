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
    public const string PluginVersion = "1.0.4";
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

        Log.LogInfo($"{PluginName}: installed {patched} Harmony patches.");
        Log.LogInfo("v1.0.4 uses direct ref-int argument patches and lobby-full bypasses for 5-8 player sessions.");
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
}
