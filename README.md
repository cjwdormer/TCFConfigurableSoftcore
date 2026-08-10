## TCFConfigurableSoftcore Configurable Softcore

Configurable death-loss rules for SPT. Overrides `InRaidHelper` so raid-end restoration
happens before the normal death penalty runs, instead of guessing at raid state from
profile files on disk.

Built for **SPT 4.0.13**.

---

## Modes (`config.jsonc`, written next to the mod DLL on first load)

| Mode | Behaviour |
|---|---|
| `Vanilla` | SPT default - lose all equipment on death |
| `KeepEverything` | Nothing is lost on death |
| `KeepGearOnly` | Keep the gear entered with (weapons, armor, pockets/rig/backpack as containers); contents of pockets/rig/backpack are always lost |
| `KeepEntryItems` | Keep everything entered with, including pockets/rig/backpack contents; only items picked up during the raid are lost |

`SecuredContainer` and `Scabbard` are always preserved, matching normal Tarkov rules,
independent of mode.

`DefaultMode` applies to any profile not listed in `ProfileOverrides`, which maps a profile
id (the filename, minus `.json`, under `user/profiles/` on the server) to a rule for that
profile. Each rule's `Maps` is every SPT map id set to `true`/`false`: the rule's `Mode` only
takes effect on maps listed `true` there; a map left out or set to `false` falls back to
`DefaultMode` - same as an unlisted profile. Leave `Maps` out entirely (or empty) to apply the
rule on every map:

```jsonc
"DefaultMode": "Vanilla",
"ProfileOverrides": {
  "68abf1e2c9c2a1f4b5d6e7f8": {
    "Mode": "KeepEverything",
    "Maps": {
      "laboratory": true,
      "factory4_night": true,
      "shoreline": false
    }
  }
}
```

Map ids: `bigmap`, `woods`, `factory4_day`, `factory4_night`, `interchange`, `rezervbase`,
`shoreline`, `laboratory`, `lighthouse`, `tarkovstreets`, `sandbox`, `sandbox_high`. Keys are
matched case-insensitively.

`PreserveFoundInRaid` (default `false`): SPT clears the Found in Raid flag from everything in
the post-raid profile on death (`InRaidHelper.RemoveFiRStatusFromItems`), independent of what
this mod protects from deletion - so without this, an item kept via `KeepEverything`/
`KeepEntryItems` still loses its FiR tag. Set `true` to skip that stripping for raids this mod
is actively restoring gear for, so kept items stay flea-sellable as if you'd extracted with them.

`NotifyPlayer` (default `true`): sends an in-game system mail whenever this mod actively
restores gear - shows up as the normal SPT mail notification (popup + sound), with text
summarising the mode and how many items were kept. There's no client mod, so this - a mail
message via `MailSendService.SendSystemMessageToPlayer` - is the closest thing to a toast
notification a server-only mod can send. Set `false` to go back to silent.

---

## How it works

`ConfigurableInRaidHelper` (`src/ConfigurableSoftcore.Server/Services`) replaces SPT's
`InRaidHelper` via DI. `SetInventory` compares the pre-raid server profile (still holding
entry-state gear at that point) against the post-raid client profile, restores whatever the
configured mode says should survive, and marks those item ids protected. `DeleteInventory`
then only removes items that weren't marked protected.

Neither of those receives the raid's map name, so `ConfigurableLocationLifecycleService`
replaces SPT's `LocationLifecycleService` too, purely to capture it: `EndLocalRaid` parses
the map out of `request.ServerId` (the same parsing SPT's own code does internally) before
calling `base.EndLocalRaid`, and hands it to `ConfigurableInRaidHelper` via `RaidMapState` -
a small singleton bridging the two, since they're DI-scoped differently and don't otherwise
share state.

## Console logging

Every time this mod actively restores gear on death, it logs one line at Info level (visible
in the server console by default, no config needed): session id, map, mode, and item count.
Set `Debug: true` for a second, more detailed line per event (FiR-preservation and
notification state) plus map-capture logging from `ConfigurableLocationLifecycleService`.

---

## Known limitations (v1)

- Doesn't exclude insured items, so there may be interaction with insurance payouts on items
  this mod restores (regardless of the `PreserveFoundInRaid` setting).