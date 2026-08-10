using System.Text.Json.Serialization;

namespace ConfigurableSoftcore.Server.Config;

public sealed class SoftcoreConfig
{
    public bool Enabled { get; set; } = true;
    public bool Debug { get; set; } = false;

    /// <summary>
    /// SPT strips the Found in Raid flag from everything in the post-raid profile on death
    /// (InRaidHelper.RemoveFiRStatusFromItems) - that runs regardless of what this mod protects
    /// from deletion, so anything kept would otherwise lose its FiR tag anyway. When true, FiR
    /// stripping is skipped for raids this mod is actively restoring gear for.
    /// </summary>
    public bool PreserveFoundInRaid { get; set; } = false;

    /// <summary>Send the player an in-game system mail (with the normal SPT notification
    /// popup/sound) summarising what this mod kept whenever it actively restores gear.</summary>
    public bool NotifyPlayer { get; set; } = true;

    /// <summary>Mode used for any profile not listed in <see cref="ProfileOverrides"/>, and for
    /// any map a listed profile's rule doesn't cover.</summary>
    public RestoreMode DefaultMode { get; set; } = RestoreMode.Vanilla;

    /// <summary>Per-profile rules, keyed by profile id (the filename, minus ".json", under
    /// user/profiles/ on the server).</summary>
    public Dictionary<string, ProfileRule> ProfileOverrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ProfileRule
{
    public RestoreMode Mode { get; set; } = RestoreMode.Vanilla;

    /// <summary>SPT internal map id (e.g. "laboratory", "shoreline") to whether this rule
    /// applies there. A map missing from this dictionary is treated the same as false. Null or
    /// empty means every map.</summary>
    public Dictionary<string, bool>? Maps { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RestoreMode
{
    /// <summary>SPT default: all equipment is lost on death.</summary>
    Vanilla = 0,

    /// <summary>Nothing is lost on death.</summary>
    KeepEverything = 1,

    /// <summary>
    /// Keep the gear entered with - weapons, armor, and the pockets/rig/backpack
    /// containers themselves. Contents of pockets/rig/backpack are always lost.
    /// </summary>
    KeepGearOnly = 2,

    /// <summary>
    /// Keep everything entered with, including pockets/rig/backpack contents.
    /// Only items picked up during the raid are lost.
    /// </summary>
    KeepEntryItems = 3
}
