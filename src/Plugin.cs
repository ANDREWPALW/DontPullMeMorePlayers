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
    public const string PluginVersion = "1.0.3";
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
        patched += PatchSteamMatchmaking(_harmony);
        patched += PatchFishySteamworks(_harmony);

        Log.LogInfo($"{PluginName}: installed {patched} Harmony patches.");
        if (patched == 0)
            Log.LogWarning("No target methods were found. Please send BepInEx/LogOutput.log to the mod author.");
    }

    private static int PatchLobbyManager(Harmony harmony)
    {
        int count = 0;
        foreach (Type type in FindTypes(t =>
                     t.FullName == "Heathen.SteamworksIntegration.LobbyManager" ||
                     (t.Name == "LobbyManager" && (t.Namespace?.Contains("Heathen", StringComparison.OrdinalIgnoreCase) ?? false))))
        {
            foreach (MethodInfo method in DeclaredMethods(type))
            {
                if (method.Name == "Create" && method.GetParameters().Any(p => p.ParameterType == typeof(int)))
                    count += Patch(harmony, method, nameof(Patches.ForceEightInIntArguments));

                if (method.Name == "set_MaxMembers" && HasSingleIntParameter(method))
                    count += Patch(harmony, method, nameof(Patches.ForceEightInIntArguments));

                if (method.Name == "get_MaxMembers" && method.ReturnType == typeof(int))
                    count += Patch(harmony, method, null, nameof(Patches.MinimumEightResult));
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
                if ((method.Name == "CreateLobby" || method.Name == "SetLobbyMemberLimit") &&
                    method.GetParameters().Any(p => p.ParameterType == typeof(int)))
                {
                    count += Patch(harmony, method, nameof(Patches.ForceEightInIntArguments));
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
                    count += Patch(harmony, method, nameof(Patches.ForceEightInIntArguments));

                // Server-side overload in this game is (string address, ushort port, int maximumClients, bool peerToPeer).
                // Client overloads do not contain an Int32 maximum-clients parameter, so they are ignored.
                if (method.Name == "StartConnection" &&
                    method.GetParameters().Length >= 4 &&
                    method.GetParameters().Any(p => p.ParameterType == typeof(int)))
                {
                    count += Patch(harmony, method, nameof(Patches.ForceEightInIntArguments));
                }

                if (method.Name == "GetMaximumClients" && method.ReturnType == typeof(int))
                    count += Patch(harmony, method, null, nameof(Patches.MinimumEightResult));
            }
        }
        return count;
    }

    private static int Patch(Harmony harmony, MethodInfo target, string? prefixName = null, string? postfixName = null)
    {
        try
        {
            HarmonyMethod? prefix = prefixName is null
                ? null
                : new HarmonyMethod(AccessTools.Method(typeof(Patches), prefixName));
            HarmonyMethod? postfix = postfixName is null
                ? null
                : new HarmonyMethod(AccessTools.Method(typeof(Patches), postfixName));

            harmony.Patch(target, prefix: prefix, postfix: postfix);
            ModLog?.LogInfo($"Patched {target.DeclaringType?.FullName}.{target.Name}({string.Join(", ", target.GetParameters().Select(p => p.ParameterType.Name))})");
            return 1;
        }
        catch (Exception ex)
        {
            ModLog?.LogWarning($"Failed to patch {target.DeclaringType?.FullName}.{target.Name}: {ex.GetType().Name}: {ex.Message}");
            return 0;
        }
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
            return type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
        }
        catch
        {
            return Array.Empty<MethodInfo>();
        }
    }
}

internal static class Patches
{
    // Harmony's __args array allows argument replacement without compile-time references
    // to Dont Pull Me's IL2CPP interop assemblies.
    public static void ForceEightInIntArguments(object[] __args)
    {
        for (int i = 0; i < __args.Length; i++)
        {
            if (__args[i] is int value && value < Plugin.MaxPlayers)
            {
                Plugin.ModLog?.LogDebug($"Raising multiplayer integer argument {value} -> {Plugin.MaxPlayers}.");
                __args[i] = Plugin.MaxPlayers;
            }
        }
    }

    public static void MinimumEightResult(ref int __result)
    {
        if (__result < Plugin.MaxPlayers)
            __result = Plugin.MaxPlayers;
    }
}
