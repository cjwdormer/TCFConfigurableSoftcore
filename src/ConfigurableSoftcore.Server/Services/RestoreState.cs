using System.Collections.Concurrent;

namespace ConfigurableSoftcore.Server.Services;

/// <summary>
/// Hands off which item ids <see cref="ConfigurableInRaidHelper.SetInventory"/> protected
/// so the matching <see cref="ConfigurableInRaidHelper.DeleteInventory"/> call - later in
/// the same raid-end request - knows what to spare.
/// </summary>
[Injectable(InjectionType.Singleton)]
public sealed class RestoreState
{
    private readonly ConcurrentDictionary<string, HashSet<string>> _protectedIds = new();

    public void MarkProtected(string sessionId, HashSet<string> itemIds)
        => _protectedIds[sessionId] = itemIds;

    public bool TryConsumeProtected(string sessionId, out HashSet<string> itemIds)
        => _protectedIds.TryRemove(sessionId, out itemIds!);
}
