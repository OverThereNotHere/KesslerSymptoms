
# Kessler Symptoms: Readable Concept Summary

> i'm no C# programmer but i sure hope claude is

A mod that makes orbital debris a real, escalating hazard in KSP. Debris left in orbit builds up a "density" value for each altitude band around each body, and the higher it climbs, the more often and the more severely your ships get hit. It's designed to create Kessler-style tension without ever becoming unplayable.

## How it works

- **Tracking:** Every debris part in orbit is counted and sorted into buckets by body and altitude band.
- **Escalation:** Higher debris density means more frequent and harsher impact events.
- **Decay and caps:** Old debris stops counting or gets deleted over time, and severity is hard-capped so the game stays playable.

## Severity tiers

| Tier | What you get |
|------|--------------|
| **1: Sparse** | Micrometeorite pings, sound effects, and a warning popup |
| **2: Dense** | Dense micrometeorite patches that can break solar panels and antennas and rarely puncture fuel tanks |
| **3: Debris field** | Large debris chunks that often puncture tanks (leaks with tiny thrust), short batteries, blow fuel cells, fail RCS blocks, and lodge in engines to force a shutdown or ignition, ect |

## Recovery

Damage isn't permanent. You can isolate leaking tank sections, cut fuel flow to an engine, or send a Kerbal on EVA to repair parts.
