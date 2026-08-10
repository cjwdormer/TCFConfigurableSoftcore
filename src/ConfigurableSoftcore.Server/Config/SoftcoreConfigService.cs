using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ConfigurableSoftcore.Server.Config;

[Injectable(InjectionType.Singleton)]
public sealed class SoftcoreConfigService
{
    private readonly string _path;
    private readonly JsonSerializerOptions _opts;

    public SoftcoreConfig Current { get; private set; } = new();

    public SoftcoreConfigService()
    {
        var modDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".";
        _path = Path.Combine(modDir, "config.jsonc");

        _opts = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };
    }

    public SoftcoreConfig LoadOrCreate()
    {
        if (!File.Exists(_path))
        {
            File.WriteAllText(_path, DefaultConfigText, Encoding.UTF8);
            Current = new SoftcoreConfig();
            return Current;
        }

        try
        {
            var text = File.ReadAllText(_path, Encoding.UTF8);
            Current = JsonSerializer.Deserialize<SoftcoreConfig>(text, _opts) ?? new SoftcoreConfig();
        }
        catch
        {
            Current = new SoftcoreConfig();
        }

        Normalise(Current);
        return Current;
    }

    /// <summary>
    /// System.Text.Json deserialises dictionaries with a case-sensitive comparer regardless of
    /// PropertyNameCaseInsensitive (that option only covers object properties, not dictionary
    /// keys) - rebuild both dictionaries case-insensitively so "Shoreline" in config.jsonc still
    /// matches the lowercase map ids SPT actually uses.
    /// </summary>
    private static void Normalise(SoftcoreConfig cfg)
    {
        cfg.ProfileOverrides = new Dictionary<string, ProfileRule>(cfg.ProfileOverrides, StringComparer.OrdinalIgnoreCase);

        foreach (var rule in cfg.ProfileOverrides.Values)
        {
            if (rule.Maps is not null)
                rule.Maps = new Dictionary<string, bool>(rule.Maps, StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Mode for a given profile on a given map. A profile's rule only applies if its Maps
    /// dictionary is empty (all maps) or has this map set to true - a map that's missing or set
    /// to false falls through to DefaultMode, same as a profile with no rule at all.
    /// </summary>
    public RestoreMode ResolveMode(string profileId, string? map)
    {
        if (!Current.ProfileOverrides.TryGetValue(profileId, out var rule))
            return Current.DefaultMode;

        if (rule.Maps is null || rule.Maps.Count == 0)
            return rule.Mode;

        var enabledHere = map is not null
            && rule.Maps.TryGetValue(map, out var enabled)
            && enabled;

        return enabledHere ? rule.Mode : Current.DefaultMode;
    }

    private const string DefaultConfigText = """
    {
      // Modes:
      //   "Vanilla"        - SPT default: lose all equipment on death.
      //   "KeepEverything" - nothing is lost on death.
      //   "KeepGearOnly"   - keep the gear entered with (weapons, armor, and the
      //                      pockets/rig/backpack containers themselves); anything
      //                      stored inside those containers is always lost.
      //   "KeepEntryItems" - keep everything entered with, including pockets/rig/
      //                      backpack contents; only items picked up during the
      //                      raid are lost.
      "Enabled": true,
      "Debug": false,

      // SPT clears the Found in Raid flag from everything on death, even items this mod
      // keeps. Set true to let kept/restored items keep their FiR tag (so they can still be
      // flea-sold as if you'd extracted with them).
      "PreserveFoundInRaid": false,

      // Sends the player an in-game system mail (standard SPT notification popup + sound)
      // summarising what was kept, whenever this mod actively restores gear.
      "NotifyPlayer": true,

      // Used for any profile not listed below, and for any map a listed profile's rule
      // doesn't cover.
      "DefaultMode": "Vanilla",

      // Per-profile rules, keyed by profile id - the filename (without ".json") of that
      // profile under user/profiles/ on the server. "Maps" sets the rule's Mode to apply only
      // on maps listed as true - a map left out or set to false falls back to DefaultMode.
      // Leave "Maps" out entirely (or empty) to apply the rule on every map.
      //
      // "ProfileOverrides": {
      //   "68abf1e2c9c2a1f4b5d6e7f8": {
      //     "Mode": "KeepEverything",
      //     "Maps": {
      //       "bigmap": false,
      //       "woods": false,
      //       "factory4_day": false,
      //       "factory4_night": true,
      //       "interchange": false,
      //       "rezervbase": false,
      //       "shoreline": false,
      //       "laboratory": true,
      //       "lighthouse": false,
      //       "tarkovstreets": false,
      //       "sandbox": false,
      //       "sandbox_high": false
      //     }
      //   }
      // }
      "ProfileOverrides": {}
    }
    """;
}
