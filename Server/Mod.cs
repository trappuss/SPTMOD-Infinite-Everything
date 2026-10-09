using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using HarmonyLib;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Models.Eft.Hideout;
using SPTarkov.Server.Core.Services.Commerce;
using SPTarkov.Server.Core.Utils;
using Range = SemanticVersioning.Range;
using Version = SemanticVersioning.Version;

namespace InfiniteEverythingServer;

public sealed record ModMetadata : IModMetadata
{
    public string ModGuid { get; init; } = "com.trappuss.infiniteeverything"; // same GUID as the client plugin (Forge rule)
    public string Name { get; init; } = "InfiniteEverything"; // letters and numbers only (Forge rule)
    public string Author { get; init; } = "trappuss";
    public List<string>? Contributors { get; init; }
    public Version Version { get; init; } = new("2.5.0");
    public Range SptVersion { get; init; } = new("~4.1.0");
    public bool HasPrepatcher { get; init; } = false;
    public List<string>? Incompatibilities { get; init; }
    public Dictionary<string, Range>? ModDependencies { get; init; }
    public string? Url { get; init; } = "https://github.com/trappuss/SPTMOD-Infinite-Everything";
    public string License { get; init; } = "MIT";
}

/// <summary>
/// Per session: is Infinite money / Infinite hideout resources on. Set by the client plugin and saved to state.json in
/// this mod's folder, so a server restart keeps it (the hideout drains for the time the server was off at the first
/// update after start, before the client has said anything).
/// </summary>
public static class InfiniteState
{
    public sealed class Flags
    {
        [JsonPropertyName("money")]
        public bool Money { get; set; }

        [JsonPropertyName("hideout")]
        public bool Hideout { get; set; }
    }

    private static readonly ConcurrentDictionary<string, Flags> Sessions = new();
    private static readonly object FileLock = new();
    private static string? _file;

    [ThreadStatic]
    internal static MongoId? HideoutSession;

    public static bool MoneyOn(MongoId sessionId) => Sessions.TryGetValue(sessionId.ToString(), out var f) && f.Money;

    public static bool HideoutOn(MongoId sessionId) => Sessions.TryGetValue(sessionId.ToString(), out var f) && f.Hideout;

    public static void Load(string file)
    {
        _file = file;
        try
        {
            if (File.Exists(file))
            {
                var saved = JsonSerializer.Deserialize<Dictionary<string, Flags>>(File.ReadAllText(file));
                foreach (var (session, flags) in saved ?? [])
                {
                    Sessions[session] = flags;
                }
            }
        }
        catch
        {
            // a broken file only means everything starts off
        }
    }

    public static void Set(MongoId sessionId, bool money, bool hideout)
    {
        Sessions[sessionId.ToString()] = new Flags { Money = money, Hideout = hideout };
        if (_file is null)
        {
            return;
        }

        lock (FileLock)
        {
            File.WriteAllText(_file, JsonSerializer.Serialize(Sessions.ToDictionary(p => p.Key, p => p.Value), new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}

public record StateRequest : IRequestData
{
    [JsonPropertyName("money")]
    public bool Money { get; set; }

    [JsonPropertyName("hideout")]
    public bool Hideout { get; set; }
}

/// <summary>POST /infiniteeverything/state {"money":bool,"hideout":bool} -> the same back.</summary>
[Injectable(TypePriority = OnLoadOrder.Routers + 1)]
public class StateRouter(JsonUtil jsonUtil, ISptLogger<StateRouter> logger)
    : StaticRouter(
        jsonUtil,
        [
            new RouteAction<StateRequest>(
                "/infiniteeverything/state",
                (url, info, sessionId, output, cancellationToken) =>
                {
                    InfiniteState.Set(sessionId, info.Money, info.Hideout);
                    logger.Info($"[Infinite Everything] session {sessionId}: infinite money {(info.Money ? "ON" : "OFF")}, infinite hideout resources {(info.Hideout ? "ON" : "OFF")}");
                    return new ValueTask<string>($"{{\"money\":{(info.Money ? "true" : "false")},\"hideout\":{(info.Hideout ? "true" : "false")}}}");
                }
            ),
        ]
    ) { }

/// <summary>
/// Every money payment in SPT 4.1.6 ends in PaymentService.AddPaymentToOutput (called by PayMoney for trader and
/// flea purchases, flea listing fees, repairs, insurance, healing and clothing, and directly by the repeatable quest
/// reroll). It finds the currency stacks and removes the amount. With Infinite money on for that session it is
/// skipped, so no money leaves the profile; barter items (non-money) are still taken by PayMoney as usual.
/// </summary>
public class SkipPaymentPatch : AbstractPatch
{
    internal static ISptLogger<InfiniteEverythingLoader>? Logger;

    protected override MethodBase? GetTargetMethod()
    {
        return AccessTools.Method(
            typeof(PaymentService),
            nameof(PaymentService.AddPaymentToOutput),
            [typeof(PmcData), typeof(MongoId), typeof(double), typeof(MongoId), typeof(ItemEventRouterResponse), typeof(IReadOnlySet<MongoId>)]
        );
    }

    [PatchPrefix]
    public static bool Prefix(MongoId currencyTpl, double amountToPay, MongoId sessionID)
    {
        if (!InfiniteState.MoneyOn(sessionID))
        {
            return true;
        }

        Logger?.Info($"[Infinite Everything] payment of {amountToPay} ({currencyTpl}) skipped");
        return false;
    }
}

/// <summary>
/// Hideout resources. HideoutHelper.UpdatePlayerHideout(sessionId) runs on every hideout request and on profile load;
/// it drains generator fuel (UpdateFuel), water filters (UpdateWaterFilters) and air filters (UpdateAirFilters) for the
/// time since the last run. The session is noted for the duration of UpdatePlayerHideout and the three drains are
/// skipped while Infinite hideout resources is on for it. Production progress (crafts, water, bitcoin) is untouched.
/// </summary>
public class HideoutSessionPatch : AbstractPatch
{
    protected override MethodBase? GetTargetMethod()
    {
        return AccessTools.Method(typeof(HideoutHelper), nameof(HideoutHelper.UpdatePlayerHideout));
    }

    [PatchPrefix]
    public static void Prefix(MongoId sessionId)
    {
        InfiniteState.HideoutSession = sessionId;
    }

    [PatchFinalizer]
    public static void Finalizer()
    {
        InfiniteState.HideoutSession = null;
    }
}

public class HideoutDrainPatch(string methodName) : AbstractPatch("com.trappuss.infiniteeverything.server.drain." + methodName)
{
    protected override MethodBase? GetTargetMethod()
    {
        return AccessTools.Method(typeof(HideoutHelper), methodName);
    }

    [PatchPrefix]
    public static bool Prefix()
    {
        var session = InfiniteState.HideoutSession;
        return !(session.HasValue && InfiniteState.HideoutOn(session.Value));
    }
}

[Injectable(TypePriority = OnLoadOrder.Preload + 1)]
public sealed class InfiniteEverythingLoader(ISptLogger<InfiniteEverythingLoader> logger, ModHelper modHelper) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        InfiniteState.Load(Path.Combine(modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly()), "state.json"));
        SkipPaymentPatch.Logger = logger;
        new SkipPaymentPatch().Enable();
        new HideoutSessionPatch().Enable();
        new HideoutDrainPatch("UpdateFuel").Enable();
        new HideoutDrainPatch("UpdateWaterFilters").Enable();
        new HideoutDrainPatch("UpdateAirFilters").Enable();
        logger.Success("[Infinite Everything] server part 2.5.0 loaded (infinite money + hideout resource patches enabled)");
        return Task.CompletedTask;
    }
}
