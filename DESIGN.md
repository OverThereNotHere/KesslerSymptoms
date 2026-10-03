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
| Decay | Debris older than a lifetime is **actually deleted** from the save, low bands first. |
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

## Milestones

1. ~~Band tracking + debug window~~ done. Debug / Effects / Settings tabs.
2. Debris lifetime deletion.
3. ~~Hit scheduler + tier 1 effects~~ done (pending in-game test).
4. Tier 2 (stock panel/antenna breakage, rare tank puncture via custom module).
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
