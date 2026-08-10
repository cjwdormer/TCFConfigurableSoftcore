using System.Collections.Concurrent;

namespace ConfigurableSoftcore.Server.Services;

/// <summary>
/// Hands off the current raid's map name from <see cref="ConfigurableLocationLifecycleService.EndLocalRaid"/>
/// to <see cref="ConfigurableInRaidHelper.SetInventory"/> - InRaidHelper.SetInventory/DeleteInventory
/// don't receive the map, only LocationLifecycleService.EndLocalRaid does (it derives the name from
/// request.ServerId), so this bridges the two overridden classes for the one raid-end request.
/// </summary>
[Injectable(InjectionType.Singleton)]
public sealed class RaidMapState
{
    private readonly ConcurrentDictionary<string, string> _maps = new();

    public void SetMap(string sessionId, string map)
        => _maps[sessionId] = map;

    public bool TryConsumeMap(string sessionId, out string map)
        => _maps.TryRemove(sessionId, out map!);
}
