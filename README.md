# Kessler Symptoms

A Kerbal Space Program mod that makes orbital debris a real, escalating hazard.

Every piece of debris you leave in orbit raises the debris density of the altitude band it
sweeps through. The denser a band gets, the more often ships flying through it get hit, and
the harder. It's built for Kessler-style tension that stays playable: debris eventually decays,
damage is repairable, and almost every number is tunable in game.

## Features

### Debris tracking
- Every planet and moon (including Kopernicus planet packs) gets a set of altitude bands,
  from just above the atmosphere (or terrain) out toward the edge of its sphere of influence.
- Orbiting debris adds density to every band its orbit passes through. Parts destroyed in
  orbit add a temporary spike that fades over time, and debris breaking panels adds a smaller one.
- Density maps to three severity tiers: **Sparse**, **Dense**, and **Debris field**.

### Encounters
- While your active vessel is in a cluttered band, it periodically rolls for a debris
  encounter. The chance grows with density, and encounters can sometimes be a lower tier
  than the band.
- An encounter is either a **one-off impact** or a **debris field**: a short stretch of
  rapid hits and grit pelting the hull from one direction, with drifting specks and haze.
  Parts on the exposed side take the hits; parts behind others are shielded.
- Impacts come with sparks, a light flash, sounds heard through the hull, and optional
  fading scorch marks. Heavier tiers push the ship around.
- A warning when you fly into a cluttered band, and an alarm when you're hit.
- Optionally keeps going during time warp: light impacts play out mid-warp, heavier ones
  drop you out of warp first (toggleable).

### Damage
Damage lands on the part that was hit, at most one failure per hit.

| | Tier 2: Dense | Tier 3: Debris field |
|---|---|---|
| Solar panels, antennas, radiators | break (stock breakage) | break, more often |
| Fuel and gas tanks | rare small puncture | bigger, more frequent punctures |
| Batteries | partial short (lose some charge) | full short (charge to zero, reduced capacity) |
| Fixed solar panels | small output loss | larger output loss |
| Fixed antennas | brief signal blip | longer signal loss |
| Fuel cells | | blowout |
| RCS blocks | | dead until repaired |
| Engines | | shut down (relight normally) |
| RTGs | | cracked: reduced output, with a Geiger counter |
| Command pods / probe cores | | SAS knocked offline |

Punctures leak with a visible vapour jet, a hiss, and a little thrust from the hole, and
slowly seal themselves. Leaks keep running correctly through time warp and while the vessel
is unloaded.

### Repair
- **Patch leak**: any kerbal on EVA.
- **Repair**: an Engineer on EVA fixes everything else on the part, including broken
  panels and antennas. No repair kits needed.

### Debris decay
Debris doesn't stay forever: each piece is removed after a lifetime based on its periapsis
height, from a couple of months just above the atmosphere up to 200 years at the edge of the
sphere of influence, with some per-piece randomness. Warping through a bad Kessler period is
a valid strategy. KSP's own *Max Persistent Debris* setting still applies on top. Decay can be
switched off entirely.

## Requirements

- Kerbal Space Program **1.12.x** (built against 1.12.5)
- [Module Manager](https://github.com/sarbian/ModuleManager)
- [Toolbar Controller](https://github.com/linuxgurugamer/ToolbarControl)
- [Click Through Blocker](https://github.com/linuxgurugamer/ClickThroughBlocker)

## Installation

1. Install the requirements above.
2. Copy the `KesslerSymptoms` folder into your KSP `GameData` folder, so you end up with
   `GameData/KesslerSymptoms/Plugins/KesslerSymptoms.dll`.
3. Start the game. Existing saves work; vessels already in flight pick up the damage module
   on load.

The `Source` folder isn't needed to play.

## Using it

Click the Kessler Symptoms toolbar button (flight, map view, tracking station or space
center) to open the window:

- **Debug**: per-band debris count, density, tier and debris lifetime for any body, plus
  tools to add or clear density spikes for testing.
- **Effects**: on/off switches for each tier and feature, a live readout of the active
  vessel's band and encounter chance, and buttons to force any encounter or failure.
- **Settings**: every tunable number, grouped into sections, with tooltips. *Apply* uses
  them right away, *Apply + Save* writes them to `PluginData/settings.cfg`.

Settings live in `GameData/KesslerSymptoms/PluginData/settings.cfg`, which is created with
defaults on first run.

## Building from source

The plugin is plain C# compiled against the game's own assemblies with `mcs` (Mono). With the
mod folder inside your install's `GameData`, run:

```bash
./build.sh
```

It finds the KSP install two folders up (override with `KSP_ROOT=/path/to/KSP ./build.sh`),
picks the right `Managed` folder for Linux, Windows or macOS, and writes
`Plugins/KesslerSymptoms.dll`. Toolbar Controller and Click Through Blocker need to be
installed in the same `GameData`, since the build references them.

## License

GPL-3.0. See [LICENSE](LICENSE).
