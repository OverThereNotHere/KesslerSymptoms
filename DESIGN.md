# Kessler Symptoms — Design

Companion to `idea.md`. Records the decisions made during planning (2026-10-02) and the
milestone plan. KSP 1.12.5, C# compiled with `mcs` against the local install.

## Decisions

| Topic | Decision |
|---|---|
| Codebase | Fresh project. The old `Source-KesslerSyndrome` attempt was discarded. |
| Bodies | Every body gets bands automatically (works with Kopernicus packs); per-body overrides in config. |
| Who gets hit | **Active vessel only.** |
| On rails | **No hits during time warp / on rails** (for now). |
| Density inputs | Orbiting **debris vessels** + decaying spikes from **recent explosions** (parts destroyed in orbit). |
| Decay | No cap of our own: KSP's Max Persistent Debris is the hard limit. Debris is **deleted** after an altitude-based lifetime with per-piece randomness; warping through long Kessler periods is intended. |
| Damage | **Reuse stock breakage** (solar panels, antennas, wheels) where it exists; a **small custom PartModule** (MM-patched) for tank leaks, battery shorts, fuel-cell blowouts, RCS failures, engine jams. |
| Repair | **Engineer on EVA, free** (no kits) — matches `noKits.cfg`. |
| Hit frequency | **Fully configurable**; ship sane defaults. |
| Dependencies | ModuleManager, ToolbarControl, ClickThroughBlocker. Ask before adding others (e.g. Harmony). |

## Model

**Bands.** Per body, from `minOrbitalDistance` (clears atmosphere and terrain) up to
`min(SOI, CeilingRadii × radius)`, split into `BandsPerBody` bands with geometric spacing
(thin near the surface where traffic lives, thick higher up).

**Debris weight.** An orbiting debris vessel contributes 1.0 spread across every band its
orbit sweeps between Pe and Ap, proportional to radial overlap. A circular orbit lands
entirely in one band; an eccentric one smears across several. Escape trajectories are ignored.

**Explosion spikes.** A part destroyed while its vessel is orbiting adds `ExplosionSpike`
to that band, decaying with half-life `ExplosionHalfLifeDays`. Persisted per save.

**Density → tier.** `density = debrisWeight × DebrisWeightMultiplier + spikes`, normalised by band volume relative to
the body's lowest band (so a sparse high shell doesn't read as dense just because it's huge).
Thresholds `Tier1At / Tier2At / Tier3At` map density to the severity tiers in `idea.md`.

## Debris decay

Each orbiting debris vessel's age is tracked from when the mod first saw it (persisted per
save). Its lifetime is `DecayBaseHours × e^(h / (DecayScaleHeightFraction × atmosphereDepth))`,
where `h` is periapsis height above the atmosphere, times a fixed per-piece factor
`e^(±DecayRandomness)` seeded from its persistent id. Airless bodies: no drag, never decays.
Kerbin defaults (Earth calendar): 75 km ~2 weeks, 80 km ~2 months, 90 km ~1.6 y, 100 km ~17 y,
120 km+ millennia. Only unloaded debris that isn't the active vessel or target is deleted, and
deletions never count as explosion spikes. The Debug tab shows the lifetime at each band's floor.

## Encounters

All encounters go through `Encounters.Trigger(vessel, tier, forced)`, which enforces the
rules (flight scene, active vessel, loaded and off rails, tier toggles unless forced) and
runs the tier's `IEncounterEffect`. The Effects tab can force any tier for testing.

**Hit rate.** While the active vessel is off rails in a band at tier ≥ 1, hits arrive at
random (Poisson) with mean `HitsPerHourPerDensity × density` per hour. The hit uses the
band's current tier. All knobs live in Settings.

**One-off vs field.** Each encounter is a debris field with chance
`FieldChanceMax × density / (density + FieldHalfDensity)`, otherwise a one-off impact.
A field lasts `FieldDurationMin..Max` game seconds: real tier hits every ~`FieldSecondsPerHit`
(no per-hit alarm), plus `FieldPeltsPerSecond` sound-only micro pelts (quieter, higher pitch).
One alarm + text when it starts, text when it passes; it ends silently if the vessel goes on
rails or you switch vessels. No new encounters roll during a field.

**Time warp.** With `RailsWarpEncounters` on, encounters keep rolling in rails warp (rolled
over the full warped time). Tier 1 one-offs and fields play out without stopping the warp
(no push while packed). Tier 2/3 drop you to 1x and land once the vessel is back in physics.
Rails warp is locked out during a field, except a tier 1 field that arrived mid-warp.
Fields default to at most 20 s so the lock doesn't drag on (`FieldDurationMax`, adjustable).

**Impulse.** Tier 2/3 hits push the struck part inward at the impact point
(`Tier2Impulse` / `Tier3Impulse`, tonne·m/s). Tier 1 never pushes.

### Tier 1: Sparse (implemented)

- **Ping:** `litepelt` sound (pitch varied) + streaking sparks + brief light flash at a random
  point on a random part. No gameplay effect. Tiers 2/3 currently reuse this with the
  `hardpelt` sounds and bigger sparks as a cosmetic placeholder.
- **Impact alert:** each encounter shows a text message and plays `alert.wav`, rate-limited by
  `AlertCooldownSeconds` (forced encounters ignore the cooldown).
- **Band warning:** text only, when the active vessel's tier rises (entering cluttered space
  or switching to a vessel inside it). Crossing between same-tier bands stays quiet.
- **Sounds:** `Sounds/litepelt1-2`, `hardpelt1-2`, `alert` (mono WAV). Stock fallbacks if missing.

### Tier 2: Dense (implemented, pending test)

Damage lands on **the part that got hit** (the same random part the sparks hit), with a
per-part-type chance. Every real hit can damage, including each hit inside a field.
Tier 2/3 already push the part and drop you out of warp.

- **Deployables** (solar panels, antennas, radiators): break with stock breakage
  (`breakPanels`), **extended or retracted**. Chance `Tier2BreakChance` (default 0.35).
- **Tank puncture (rare):** chance `Tier2PunctureChance` (default 0.05) when the hit part holds
  leakable resources (density > 0 and flowable: LF, Ox, Mono, Xenon, modded fluids; not
  SolidFuel, Ablator, Ore, EC).
  - Drains **only the punctured tank**.
  - **Self-sealing:** leak rate starts at a random fraction of capacity per minute and decays
    exponentially (`LeakRateMin..Max` %/min, `LeakSealHalfLifeMinutes`), so total loss is
    bounded (default ~5–20%).
  - **Thrust** at the hole, opposite the vent direction: mass flow x `LeakVentSpeed` (800 m/s),
    so a ~1%/min leak pushes gently and a ~4%/min leak noticeably more, fading as it seals.
  - **Effects:** vapour jet out of the hole plus a looping hiss (`leak1/2` for leaks starting
    under 2%/min, `leak3` above), both scaled by the current rate.
  - Damage alerts play `partalarm` (trimmed to one 2.75 s cycle, -8 dB), not the impact alert.
  - Computed in closed form from UT, so it's correct through warp and across unload/reload.
- **Notification:** screen message naming the part, the part glows red for a few seconds,
  and the alarm plays (ignoring the cooldown). Dropping out of warp already happens for tier 2.
- **Repair:** free, with stock's own skill rule: if Kerbal experience is on in difficulty
  settings, the EVA kerbal needs repair skill >= 1 (an Engineer); otherwise anyone can.
  Broken deployables use stock's *Repair* button; `ModuleKesslerDamage` zeroes the internal
  `repairKitsNecessary` after stock's OnStart sets it (mass-based, capped by KSP's
  `PART_REPAIR_MAX_KIT_AMOUNT`). Leaks get our *Patch leak* EVA action with the same skill rule.
  `noKits.cfg` sets `repairKitsRequired`, which no stock module reads, so it has no effect.
- **Persistence:** `ModuleKesslerDamage` (added by `KesslerDamage.cfg`, an MM `:FINAL` patch,
  to every part with a deployable or any resource) stores leak state in the save.
- **Tier 3** uses tier 2's damage chances until it gets its own spec.
- **Debug:** Effects tab has *Break a panel/antenna* and *Puncture a tank* buttons.

## Milestones

1. ~~Band tracking + debug window~~ done. Debug / Effects / Settings tabs.
2. ~~Debris lifetime deletion~~ done (pending test), plus time warp encounters.
3. ~~Hit scheduler + tier 1 effects~~ done (pending in-game test).
4. Tier 2 (stock panel/antenna breakage, rare tank puncture via custom module). ← next
5. Tier 3 + EVA engineer repair for custom failures.

## Layout

| Path | Role |
|---|---|
| `Source/` | C# sources; `build.sh` compiles them |
| `Plugins/KesslerSymptoms.dll` | Build output (git-ignored) |
| `PluginData/settings.cfg` | Tunables (generated with defaults on first run; editable in the Settings tab) |
| `Textures/` | Toolbar icons |
| `Sounds/` | Custom sounds (user-provided; stock fallbacks used until present) |
| `noKits.cfg` | MM patch: free repairs |
