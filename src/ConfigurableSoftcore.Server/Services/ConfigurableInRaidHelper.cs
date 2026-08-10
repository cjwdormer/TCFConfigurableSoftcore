using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Services;
using SPTarkov.Server.Core.Utils.Cloners;
using ConfigurableSoftcore.Server.Config;

namespace ConfigurableSoftcore.Server.Services;

/// <summary>
/// Replaces SPT's InRaidHelper. On death, restores whichever equipped items the
/// configured <see cref="RestoreMode"/> says should survive, then lets normal death
/// processing remove everything that wasn't protected.
/// </summary>
[Injectable(InjectionType.Scoped, typeof(InRaidHelper))]
public class ConfigurableInRaidHelper : InRaidHelper
{
    private readonly ISptLogger<InRaidHelper> _logger;
    private readonly ICloner _cloner;
    private readonly SoftcoreConfigService _config;
    private readonly RestoreState _restoreState;
    private readonly RaidMapState _raidMapState;
    private readonly MailSendService _mailSendService;

    // Set at the top of SetInventory and read by RemoveFiRStatusFromItems, which base.SetInventory
    // calls synchronously partway through - same call stack, so an instance field (this class is
    // DI-scoped per request) is enough to pass the decision across without touching RestoreState.
    private bool _preserveFoundInRaidForThisCall;

    // ConfigServer is only here because InRaidHelper's own constructor still requires one to
    // pass to base() - SPT marked the type obsolete for 4.1 but hasn't changed that signature yet.
#pragma warning disable CS0618
    public ConfigurableInRaidHelper(
        ISptLogger<InRaidHelper> logger,
        InventoryHelper inventoryHelper,
        ConfigServer configServer,
        ICloner cloner,
        DatabaseService databaseService,
        SoftcoreConfigService config,
        RestoreState restoreState,
        RaidMapState raidMapState,
        MailSendService mailSendService)
        : base(logger, inventoryHelper, configServer, cloner, databaseService)
    {
#pragma warning restore CS0618
        _logger = logger;
        _cloner = cloner;
        _config = config;
        _restoreState = restoreState;
        _raidMapState = raidMapState;
        _mailSendService = mailSendService;
    }

    public override void SetInventory(MongoId sessionId, PmcData serverProfile,
        PmcData postRaidProfile, bool isSurvived, bool isTransfer)
    {
        var cfg = _config.Current;
        _raidMapState.TryConsumeMap(sessionId.ToString(), out var map);
        var mode = _config.ResolveMode(sessionId.ToString(), string.IsNullOrEmpty(map) ? null : map);
        var restoring = cfg.Enabled && !isSurvived && !isTransfer && mode != RestoreMode.Vanilla;

        _preserveFoundInRaidForThisCall = restoring && cfg.PreserveFoundInRaid;

        if (restoring)
        {
            var protectedIds = ApplyMode(mode, serverProfile, postRaidProfile);
            _restoreState.MarkProtected(sessionId.ToString(), protectedIds);

            if (cfg.StripInsuranceForKeptItems)
            {
                var unInsured = StripInsuranceForProtectedItems(serverProfile, protectedIds);
                if (cfg.Debug && unInsured > 0)
                {
                    _logger.Debug(
                        $"[ConfigurableSoftcore] {sessionId}: removed {unInsured} kept item(s) from " +
                        $"InsuredItems to prevent an insurance-return duplicate.");
                }
            }

            _logger.Info(
                $"[ConfigurableSoftcore] {sessionId} died on {(map ?? "unknown map")}: mode={mode}, " +
                $"kept {protectedIds.Count} item(s).");

            if (cfg.Debug)
            {
                _logger.Debug(
                    $"[ConfigurableSoftcore] {sessionId}: preserveFoundInRaid={_preserveFoundInRaidForThisCall}, " +
                    $"notifyPlayer={cfg.NotifyPlayer}.");
            }

            if (cfg.NotifyPlayer)
            {
                _mailSendService.SendSystemMessageToPlayer(sessionId, BuildNotificationText(mode, protectedIds.Count), null);
            }
        }

        base.SetInventory(sessionId, serverProfile, postRaidProfile, isSurvived, isTransfer);
    }

    private static string BuildNotificationText(RestoreMode mode, int keptCount) => mode switch
    {
        RestoreMode.KeepEverything =>
            $"Configurable Softcore: you died, but everything was restored ({keptCount} item(s)).",
        RestoreMode.KeepGearOnly =>
            $"Configurable Softcore: you died. Your gear was kept, but pocket/rig/backpack contents " +
            $"were lost ({keptCount} item(s) kept).",
        RestoreMode.KeepEntryItems =>
            $"Configurable Softcore: you died. Your gear and starting items were kept; anything picked " +
            $"up during the raid was lost ({keptCount} item(s) kept).",
        _ => $"Configurable Softcore: {keptCount} item(s) kept."
    };

    /// <summary>
    /// SPT's base implementation strips the Found in Raid flag from everything in the post-raid
    /// profile on death, regardless of what this mod protects from deletion afterward. Skip it
    /// entirely when PreserveFoundInRaid is on and this mod is actively restoring gear for this
    /// raid - otherwise the items we just kept would still lose their FiR tag.
    /// </summary>
    protected override void RemoveFiRStatusFromItems(IEnumerable<Item> items)
    {
        if (_preserveFoundInRaidForThisCall) return;
        base.RemoveFiRStatusFromItems(items);
    }

    public override void DeleteInventory(PmcData pmcData, MongoId sessionId)
    {
        if (_restoreState.TryConsumeProtected(sessionId.ToString(), out var protectedIds))
        {
            var removed = DeleteUnprotected(pmcData, protectedIds);

            _logger.Info(
                $"[ConfigurableSoftcore] {sessionId}: DeleteInventory spared {protectedIds.Count} protected " +
                $"item(s), removed {removed}.");

            return;
        }

        _logger.Warning(
            $"[ConfigurableSoftcore] {sessionId}: DeleteInventory found no protected items for this session - " +
            $"falling through to SPT's normal death processing, all equipment will be lost.");

        base.DeleteInventory(pmcData, sessionId);
    }

    private static int StripInsuranceForProtectedItems(PmcData serverProfile, HashSet<string> protectedIds)
    {
        var insured = serverProfile.InsuredItems;
        if (insured is null || insured.Count == 0 || protectedIds.Count == 0) return 0;

        return insured.RemoveAll(i => protectedIds.Contains(i.ItemId.ToString()));
    }

    private HashSet<string> ApplyMode(RestoreMode mode, PmcData serverProfile, PmcData postRaidProfile)
    {
        var protectedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var entryItems = serverProfile.Inventory?.Items;
        var exitItems = postRaidProfile.Inventory?.Items;
        if (entryItems is null || exitItems is null) return protectedIds;

        var equipmentRootId = EquipmentGraph.FindEquipmentRootId(serverProfile.Inventory);
        if (equipmentRootId is null) return protectedIds;

        var entryChildren = EquipmentGraph.BuildChildrenByParent(entryItems);
        var entryById = EquipmentGraph.IndexById(entryItems);

        // SecuredContainer/Scabbard contents are never lost on death in vanilla Tarkov - the
        // client already reports them intact in postRaidProfile, so just protect whatever's
        // already there. Restoring them from the entry snapshot instead would wipe out anything
        // stashed in the secure container during the raid, which is the opposite of "always kept".
        ProtectAlwaysKeptSlots(equipmentRootId, exitItems, protectedIds);

        if (mode == RestoreMode.KeepEverything)
        {
            var subtreeIds = EquipmentGraph.CollectSubtreeIds(equipmentRootId, entryChildren);
            RestoreSubtree(subtreeIds, entryById, exitItems, protectedIds);

            // "Nothing is lost" also covers items picked up during the raid, which only exist
            // in the post-raid (exit) list - re-walk it after the restore above and protect
            // everything still under the equipment tree, not just the entry-side ids.
            var exitChildren = EquipmentGraph.BuildChildrenByParent(exitItems);
            foreach (var id in EquipmentGraph.CollectSubtreeIds(equipmentRootId, exitChildren))
                protectedIds.Add(id);

            return protectedIds;
        }

        if (!entryChildren.TryGetValue(equipmentRootId, out var slotItems)) return protectedIds;

        foreach (var slotItem in slotItems)
        {
            if (string.IsNullOrEmpty(slotItem.Id)) continue;
            if (!string.IsNullOrEmpty(slotItem.SlotId) && EquipmentGraph.AlwaysKeptSlots.Contains(slotItem.SlotId))
                continue; // already handled above

            var isContainer = !string.IsNullOrEmpty(slotItem.SlotId) && EquipmentGraph.ContainerSlots.Contains(slotItem.SlotId);

            if (!isContainer || mode == RestoreMode.KeepEntryItems)
            {
                var subtreeIds = EquipmentGraph.CollectSubtreeIds(slotItem.Id, entryChildren);
                RestoreSubtree(subtreeIds, entryById, exitItems, protectedIds);
            }
            else
            {
                // KeepGearOnly + container slot: keep the container itself, leave its
                // contents to normal death processing.
                protectedIds.Add(slotItem.Id);
            }
        }

        return protectedIds;
    }

    private static void ProtectAlwaysKeptSlots(string equipmentRootId, List<Item> exitItems, HashSet<string> protectedIds)
    {
        var exitChildren = EquipmentGraph.BuildChildrenByParent(exitItems);
        if (!exitChildren.TryGetValue(equipmentRootId, out var slotItems)) return;

        foreach (var slotItem in slotItems)
        {
            if (string.IsNullOrEmpty(slotItem.Id) || string.IsNullOrEmpty(slotItem.SlotId)) continue;
            if (!EquipmentGraph.AlwaysKeptSlots.Contains(slotItem.SlotId)) continue;

            foreach (var id in EquipmentGraph.CollectSubtreeIds(slotItem.Id, exitChildren))
                protectedIds.Add(id);
        }
    }

    private void RestoreSubtree(HashSet<string> subtreeIds, Dictionary<string, Item> entryById,
        List<Item> exitItems, HashSet<string> protectedIds)
    {
        exitItems.RemoveAll(i => !string.IsNullOrEmpty(i.Id) && subtreeIds.Contains(i.Id));

        foreach (var id in subtreeIds)
        {
            if (entryById.TryGetValue(id, out var entryItem))
                exitItems.Add(_cloner.Clone(entryItem)!);
        }

        foreach (var id in subtreeIds)
            protectedIds.Add(id);
    }

    private int DeleteUnprotected(PmcData pmcData, HashSet<string> protectedIds)
    {
        var items = pmcData.Inventory?.Items;
        if (items is null)
        {
            _logger.Warning(
                "[ConfigurableSoftcore] DeleteInventory: profile inventory items were null, nothing removed.");
            return 0;
        }

        var equipmentRootId = EquipmentGraph.FindEquipmentRootId(pmcData.Inventory);
        if (equipmentRootId is null)
        {
            _logger.Warning(
                "[ConfigurableSoftcore] DeleteInventory: no equipment root found on the profile, nothing removed.");
            return 0;
        }

        var childrenByParent = EquipmentGraph.BuildChildrenByParent(items);
        var allEquippedIds = EquipmentGraph.CollectSubtreeIds(equipmentRootId, childrenByParent);
        allEquippedIds.Remove(equipmentRootId);

        var toRemove = new HashSet<string>(
            allEquippedIds.Where(id => !protectedIds.Contains(id)),
            StringComparer.OrdinalIgnoreCase);

        if (_config.Current.Debug)
        {
            _logger.Debug(
                $"[ConfigurableSoftcore] DeleteInventory: equipmentRoot={equipmentRootId}, " +
                $"{allEquippedIds.Count} equipped item(s) on profile, {protectedIds.Count} protected, " +
                $"{toRemove.Count} unprotected.");
        }

        if (toRemove.Count == 0) return 0;

        return items.RemoveAll(i => !string.IsNullOrEmpty(i.Id) && toRemove.Contains(i.Id));
    }
}
