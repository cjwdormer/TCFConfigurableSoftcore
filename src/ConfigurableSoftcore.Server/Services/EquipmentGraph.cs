using SPTarkov.Server.Core.Models.Eft.Common.Tables;

namespace ConfigurableSoftcore.Server.Services;

internal static class EquipmentGraph
{
    /// <summary>Item template id of the root "Equipment" container every equipped item hangs off.
    /// Used as a fallback only - BotBaseInventory.Equipment is the authoritative pointer.</summary>
    public const string EquipmentTemplateId = "55d7217a4bdc2d86028b456d";

    public static readonly HashSet<string> ContainerSlots =
        new(StringComparer.OrdinalIgnoreCase) { "Pockets", "TacticalVest", "Backpack" };

    /// <summary>Slots normal Tarkov never loses on death, independent of any restore mode.</summary>
    public static readonly HashSet<string> AlwaysKeptSlots =
        new(StringComparer.OrdinalIgnoreCase) { "SecuredContainer", "Scabbard" };

    public static string? FindEquipmentRootId(BotBaseInventory? inventory)
    {
        if (inventory is null) return null;
        if (!string.IsNullOrEmpty(inventory.Equipment)) return inventory.Equipment;

        return inventory.Items?
            .FirstOrDefault(i => string.Equals(i.Template, EquipmentTemplateId, StringComparison.OrdinalIgnoreCase))
            ?.Id;
    }

    public static Dictionary<string, List<Item>> BuildChildrenByParent(List<Item> items)
    {
        var map = new Dictionary<string, List<Item>>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            if (string.IsNullOrEmpty(item.ParentId) || string.IsNullOrEmpty(item.Id)) continue;
            if (!map.TryGetValue(item.ParentId, out var children))
            {
                children = new List<Item>();
                map[item.ParentId] = children;
            }
            children.Add(item);
        }
        return map;
    }

    public static Dictionary<string, Item> IndexById(List<Item> items)
    {
        var map = new Dictionary<string, Item>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            if (!string.IsNullOrEmpty(item.Id))
                map[item.Id] = item;
        }
        return map;
    }

    public static HashSet<string> CollectSubtreeIds(string rootId, Dictionary<string, List<Item>> childrenByParent)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { rootId };
        var queue = new Queue<string>();
        queue.Enqueue(rootId);

        while (queue.Count > 0)
        {
            var currentId = queue.Dequeue();
            if (!childrenByParent.TryGetValue(currentId, out var children)) continue;

            foreach (var child in children)
            {
                if (!string.IsNullOrEmpty(child.Id) && ids.Add(child.Id))
                    queue.Enqueue(child.Id);
            }
        }

        return ids;
    }
}
