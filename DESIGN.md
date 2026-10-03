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

**Density → tier.** `density = debrisWeight + spikes`, normalised by band volume relative to
the body's lowest band (so a sparse high shell doesn't read as dense just because it's huge).
Thresholds `Tier1At / Tier2At / Tier3At` map density to the severity tiers in `idea.md`.

## Milestones

1. **Band tracking + debug window** ← current. Bands for every body, live debris scan,
   explosion spikes with persistence, toolbar button + diagnostics window.
2. Debris lifetime deletion.
3. Hit scheduler (active vessel, off rails only) + tier 1 effects (pings, sound, warning).
4. Tier 2 (stock panel/antenna breakage, rare tank puncture via custom module).
5. Tier 3 + EVA engineer repair for custom failures.

## Layout

| Path | Role |
|---|---|
| `Source/` | C# sources; `build.sh` compiles them |
| `Plugins/KesslerSymptoms.dll` | Build output (git-ignored) |
| `PluginData/settings.cfg` | Tunables (generated with defaults on first run) |
| `Textures/` | Toolbar icons |
| `noKits.cfg` | MM patch: free repairs |
