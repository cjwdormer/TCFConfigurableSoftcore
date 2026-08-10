using SPTarkov.Server.Core.Extensions;
using SPTarkov.Server.Core.Generators;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Match;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Services;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Core.Utils.Cloners;
using ConfigurableSoftcore.Server.Config;

namespace ConfigurableSoftcore.Server.Services;

/// <summary>
/// Replaces SPT's LocationLifecycleService purely to capture the raid's map name before handing
/// off to the (also replaced) InRaidHelper - SetInventory/DeleteInventory never receive it, only
/// EndLocalRaid does, via the same request.ServerId parsing SPT itself uses internally.
/// </summary>
// ConfigServer is only here because LocationLifecycleService's own constructor still requires
// one to pass to base() - SPT marked the type obsolete for 4.1 but hasn't changed that signature yet
// (CS0618). logger is deliberately used both here and forwarded to base() - this class needs its
// own copy to log from EndLocalRaid, same value the base class also keeps for its own logging
// (CS9107, benign double-capture).
#pragma warning disable CS0618
#pragma warning disable CS9107
[Injectable(InjectionType.Singleton, typeof(LocationLifecycleService))]
public class ConfigurableLocationLifecycleService(
    ISptLogger<LocationLifecycleService> logger,
    RewardHelper rewardHelper,
    ConfigServer configServer,
    TimeUtil timeUtil,
    DatabaseService databaseService,
    ProfileHelper profileHelper,
    BackupService backupService,
    ProfileActivityService profileActivityService,
    BotNameService botNameService,
    ICloner cloner,
    RaidTimeAdjustmentService raidTimeAdjustmentService,
    LocationLootGenerator locationLootGenerator,
    ServerLocalisationService serverLocalisationService,
    BotLootCacheService botLootCacheService,
    LootGenerator lootGenerator,
    MailSendService mailSendService,
    TraderHelper traderHelper,
    RandomUtil randomUtil,
    InRaidHelper inRaidHelper,
    PlayerScavGenerator playerScavGenerator,
    SaveServer saveServer,
    HealthHelper healthHelper,
    PmcChatResponseService pmcChatResponseService,
    PmcWaveGenerator pmcWaveGenerator,
    QuestHelper questHelper,
    InsuranceService insuranceService,
    MatchBotDetailsCacheService matchBotDetailsCacheService,
    BtrDeliveryService btrDeliveryService,
    RaidMapState raidMapState,
    SoftcoreConfigService config
)
    : LocationLifecycleService(
        logger,
        rewardHelper,
        configServer,
        timeUtil,
        databaseService,
        profileHelper,
        backupService,
        profileActivityService,
        botNameService,
        cloner,
        raidTimeAdjustmentService,
        locationLootGenerator,
        serverLocalisationService,
        botLootCacheService,
        lootGenerator,
        mailSendService,
        traderHelper,
        randomUtil,
        inRaidHelper,
        playerScavGenerator,
        saveServer,
        healthHelper,
        pmcChatResponseService,
        pmcWaveGenerator,
        questHelper,
        insuranceService,
        matchBotDetailsCacheService,
        btrDeliveryService
    )
#pragma warning restore CS0618
#pragma warning restore CS9107
{
    public override void EndLocalRaid(MongoId sessionId, EndLocalRaidRequestData request)
    {
        // Same parsing SPT's own EndLocalRaid uses internally to get locationName - ServerId is
        // "{location}.{playerSide} {timestamp}", set by StartLocalRaid.
        var serverDetails = request.ServerId?.Split('.');
        if (serverDetails is { Length: > 0 })
        {
            var map = serverDetails[0].ToLowerInvariant();
            raidMapState.SetMap(sessionId.ToString(), map);

            if (config.Current.Debug)
                logger.Debug($"[ConfigurableSoftcore] {sessionId}: captured map '{map}' from ServerId '{request.ServerId}'.");
        }
        else if (config.Current.Debug)
        {
            logger.Debug($"[ConfigurableSoftcore] {sessionId}: could not parse a map from ServerId '{request.ServerId}'.");
        }

        base.EndLocalRaid(sessionId, request);
    }
}
